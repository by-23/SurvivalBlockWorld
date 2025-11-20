using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(Entity))]
public class EntityMeshCombiner : MonoBehaviour
{
    [SerializeField] private Entity _entity;
    private GameObject _combinedMeshObject;
    private Cube[] _cubes;
    private Rigidbody _rb;
    private bool _isKinematicOriginalState;
    private bool _isCombined;
    private bool _isCombining;

    public bool IsCombined => _isCombined;
    public bool IsCombining => _isCombining;
    public int AsyncCombineThreshold => _asyncCombineThreshold;

    private struct CachedCubeComponents
    {
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
        public ColorCube ColorCube;
        public Color32 Color;
        public Mesh Mesh;
        public bool IsValid;
    }

    private CachedCubeComponents[] _cachedComponents;


    private MaterialPropertyBlock _propertyBlock;
    [SerializeField] private int _asyncCombineThreshold = 256;

    private Material _sourceMaterial;

    private Dictionary<Color32, System.Collections.Generic.List<CombineInstance>> _colorGroups =
        new Dictionary<Color32, System.Collections.Generic.List<CombineInstance>>(16);

    private readonly System.Collections.Generic.List<Color32> _colorKeys =
        new System.Collections.Generic.List<Color32>(16);

    private Queue<Mesh> _meshPool = new Queue<Mesh>();

    private class CombinedSegment
    {
        public GameObject Object;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
    }

    private readonly System.Collections.Generic.List<CombinedSegment> _segments =
        new System.Collections.Generic.List<CombinedSegment>(16);

    private readonly Dictionary<int, Stack<CombineInstance[]>> _combineArrayPool =
        new Dictionary<int, Stack<CombineInstance[]>>(16);

    private readonly List<MeshRenderer> _renderersToDisable = new List<MeshRenderer>(256);

    private void Awake()
    {
        if (_entity == null)
            _entity = GetComponent<Entity>();
        _rb = GetComponent<Rigidbody>();
        _propertyBlock = new MaterialPropertyBlock();
    }

    private bool TrySetEntityKinematic(bool state)
    {
        if (_entity != null)
        {
            if (_entity.IsKinematic == state)
                return true;

            return _entity.SetKinematicState(state, true);
        }

        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        if (_rb == null)
            return false;

        if (_rb.isKinematic == state)
            return true;

        _rb.isKinematic = state;
        return true;
    }

    private bool GetEntityKinematic()
    {
        if (_entity != null)
            return _entity.IsKinematic;

        return _rb != null && _rb.isKinematic;
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_entity == null)
            _entity = GetComponent<Entity>(); // Автоподстановка в редакторе
    }
#endif

    private void OnDestroy()
    {
        while (_meshPool.Count > 0)
        {
            var mesh = _meshPool.Dequeue();
            if (mesh != null) DestroyImmediate(mesh);
        }

        for (int i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Filter != null && segment.Filter.sharedMesh != null)
            {
                DestroyImmediate(segment.Filter.sharedMesh);
                segment.Filter.sharedMesh = null;
            }

            if (segment.Object != null)
                DestroyImmediate(segment.Object);
        }

        if (_combinedMeshObject != null)
            DestroyImmediate(_combinedMeshObject);
    }

    private void CacheCubeComponents()
    {
        _cubes = (_entity != null && _entity.Cubes != null) ? _entity.Cubes : GetComponentsInChildren<Cube>();
        if (_cubes == null || _cubes.Length == 0) return;

        _cachedComponents = new CachedCubeComponents[_cubes.Length];

        for (int i = 0; i < _cubes.Length; i++)
        {
            var cube = _cubes[i];
            if (cube == null || cube.Detouched) continue;

            // Используем прямые ссылки (убирает проверки свойств)
            var meshFilter = cube.DirectMeshFilter;
            var meshRenderer = cube.DirectMeshRenderer;

            Mesh mesh = cube.CachedMesh;
            Color32 color = cube.ColorCube != null ? cube.ColorCube.GetColor32() : new Color32(255, 255, 255, 255);

            bool isValid = meshFilter != null && mesh != null && meshRenderer != null;

            _cachedComponents[i] = new CachedCubeComponents
            {
                MeshFilter = meshFilter,
                MeshRenderer = meshRenderer,
                ColorCube = cube.ColorCube,
                Color = color,
                Mesh = mesh,
                IsValid = isValid
            };
        }
    }

    public void InvalidateCubeCache()
    {
        _cachedComponents = null;
        _cubes = null;
    }

    private Mesh GetPooledMesh()
    {
        if (_meshPool.Count > 0)
        {
            var mesh = _meshPool.Dequeue();
            mesh.Clear();
            return mesh;
        }

        var newMesh = new Mesh();
        newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        return newMesh;
    }

    private void ReturnMeshToPool(Mesh mesh)
    {
        if (mesh != null)
        {
            _meshPool.Enqueue(mesh);
        }
    }

    private CombineInstance[] RentCombineArray(int size)
    {
        if (size <= 0)
            return System.Array.Empty<CombineInstance>();

        if (_combineArrayPool.TryGetValue(size, out var stack) && stack.Count > 0)
            return stack.Pop();

        return new CombineInstance[size];
    }

    private void ReturnCombineArray(CombineInstance[] array)
    {
        if (array == null || array.Length == 0)
            return;

        if (!_combineArrayPool.TryGetValue(array.Length, out var stack))
        {
            stack = new Stack<CombineInstance[]>(2);
            _combineArrayPool[array.Length] = stack;
        }

        stack.Push(array);
    }

    private void QueueRendererDisable(MeshRenderer renderer)
    {
        if (renderer == null || !renderer.enabled)
            return;
        _renderersToDisable.Add(renderer);
    }

    private void DisableQueuedRenderers()
    {
        if (_renderersToDisable.Count == 0)
            return;

        for (int i = 0; i < _renderersToDisable.Count; i++)
        {
            var renderer = _renderersToDisable[i];
            if (renderer != null)
                renderer.enabled = false;
        }

        _renderersToDisable.Clear();
    }

    private void ClearRendererDisableQueue()
    {
        _renderersToDisable.Clear();
    }

    private GameObject EnsureCombinedRoot()
    {
        if (_combinedMeshObject != null)
            return _combinedMeshObject;

        _combinedMeshObject = new GameObject("CombinedMesh");
        _combinedMeshObject.transform.SetParent(transform, false);
        return _combinedMeshObject;
    }

    private CombinedSegment EnsureSegment(int index)
    {
        EnsureCombinedRoot();

        while (_segments.Count <= index)
        {
            var segmentIndex = _segments.Count;
            var go = new GameObject($"CombinedMesh_{segmentIndex}");
            go.transform.SetParent(_combinedMeshObject.transform, false);
            go.SetActive(false);

            var filter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            _segments.Add(new CombinedSegment
            {
                Object = go,
                Filter = filter,
                Renderer = meshRenderer
            });
        }

        return _segments[index];
    }

    private void DeactivateUnusedSegments(int startIndex)
    {
        // Снимаем меши и скрываем сегменты без изменения иерархии
        for (int i = startIndex; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.Filter.sharedMesh != null)
            {
                ReturnMeshToPool(segment.Filter.sharedMesh);
                segment.Filter.sharedMesh = null;
            }

            if (segment.Object.activeSelf)
                segment.Object.SetActive(false);
        }
    }

    private void ActivateSegments(int count)
    {
        int limit = Mathf.Min(count, _segments.Count);
        for (int i = 0; i < limit; i++)
        {
            var segment = _segments[i];
            if (!segment.Object.activeSelf)
                segment.Object.SetActive(true);
        }
    }

    private void UpdateEntityMeshReferences(int activeSegments)
    {
        if (_entity == null) return;

        if (activeSegments == 0)
        {
            _entity.SetCombinedMeshes(null);
            return;
        }

        var meshes = new Mesh[activeSegments];
        for (int i = 0; i < activeSegments; i++)
        {
            if (i < _segments.Count)
            {
                meshes[i] = _segments[i].Filter.sharedMesh;
            }
        }

        _entity.SetCombinedMeshes(meshes);
    }

    public bool CombineMeshesAdaptive(int cubeCount)
    {
        if (_isCombined)
            return false;

        if (_isCombining)
            return true;

        if (cubeCount >= _asyncCombineThreshold)
        {
            CombineMeshesAsync();
            return true;
        }

        CombineMeshes();
        return false;
    }

    public void CombineMeshes()
    {
        if (_isCombined || _isCombining) return;

        _isCombining = true;

        try
        {
            var entityCubes = _entity != null ? _entity.Cubes : GetComponentsInChildren<Cube>();
            if (_cachedComponents == null || entityCubes == null || _cachedComponents.Length != entityCubes.Length)
            {
                CacheCubeComponents();
            }

            if (_cachedComponents == null || _cachedComponents.Length == 0)
            {
                _isCombining = false;
                return;
            }

            if (_entity != null)
            {
                _entity.EnsureCacheValid();

                if (_entity.IsStructureDirty)
                {
                    var newEntities = _entity.SplitIntoSeparateEntities();
                    if (newEntities.Length > 0)
                    {
                        _isCombining = false;
                        return;
                    }
                }
            }

            if (_rb != null || _entity != null)
            {
                _isKinematicOriginalState = GetEntityKinematic();
                TrySetEntityKinematic(true);
            }

            // Очищаем кэш и переиспользуем структуры
            _sourceMaterial = null;

            // Очищаем группы цветов (переиспользуем списки)
            foreach (var list in _colorGroups.Values)
                list.Clear();
            _colorGroups.Clear();
            _colorKeys.Clear();
            ClearRendererDisableQueue();

            // Проверяем валидность transform и _cachedComponents перед использованием
            if (transform == null || _cachedComponents == null || _cachedComponents.Length == 0)
            {
                ShowCubes();
                TrySetEntityKinematic(false);
                _isCombined = false;
                ClearRendererDisableQueue();
                return;
            }

            var worldToLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < _cachedComponents.Length; i++)
            {
                var cached = _cachedComponents[i];
                if (!cached.IsValid || cached.Mesh == null) continue;

                // Проверяем, что куб не был удален между кэшированием и комбинированием
                if (_cubes != null && i < _cubes.Length)
                {
                    var cube = _cubes[i];
                    if (cube == null || cube.Detouched) continue;
                }

                // Проверяем валидность MeshFilter и его transform
                if (cached.MeshFilter == null || cached.MeshFilter.transform == null)
                    continue;

                // Выключаем рендеры
                if (cached.MeshRenderer != null && cached.MeshRenderer.enabled)
                {
                    if (_sourceMaterial == null)
                        _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                    QueueRendererDisable(cached.MeshRenderer);
                }

                var ci = new CombineInstance
                {
                    mesh = cached.Mesh,
                    transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
                };

                if (!_colorGroups.TryGetValue(cached.Color, out var list))
                {
                    list = new System.Collections.Generic.List<CombineInstance>(32);
                    _colorGroups[cached.Color] = list;
                    _colorKeys.Add(cached.Color);
                }

                list.Add(ci);
            }

            if (_sourceMaterial == null)
            {
                ShowCubes();
                TrySetEntityKinematic(false);
                _isCombined = false;
                ClearRendererDisableQueue();
                return;
            }

            _combinedMeshObject = EnsureCombinedRoot();
            if (!_combinedMeshObject.activeSelf)
                _combinedMeshObject.SetActive(true);

            // Очищаем MaterialPropertyBlock для переиспользования
            _propertyBlock.Clear();

            int activeSegments = 0;

            // Обходим группы цветов и создаём объединённые меши
            for (int i = 0; i < _colorKeys.Count; i++)
            {
                var color = _colorKeys[i];
                var instances = _colorGroups[color];

                int instanceCount = instances.Count;
                if (instanceCount == 0) continue;

                var combineArray = RentCombineArray(instanceCount);
                for (int j = 0; j < instanceCount; j++)
                {
                    combineArray[j] = instances[j];
                }

                var subMesh = GetPooledMesh();
                subMesh.CombineMeshes(combineArray, true, true);

                if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                    subMesh.bounds = fastBounds;
                else
                    subMesh.RecalculateBounds();

                var segment = EnsureSegment(activeSegments++);
                if (segment.Object.activeSelf)
                    segment.Object.SetActive(false);

                if (segment.Filter.sharedMesh != null)
                {
                    ReturnMeshToPool(segment.Filter.sharedMesh);
                    segment.Filter.sharedMesh = null;
                }

                segment.Filter.sharedMesh = subMesh;
                segment.Renderer.sharedMaterial = _sourceMaterial;

                // Очищаем PropertyBlock перед установкой цвета для каждого объекта
                _propertyBlock.Clear();
                _propertyBlock.SetColor("_BaseColor", (Color)color);
                _propertyBlock.SetColor("_Color", (Color)color); // Fallback для стандартных шейдеров
                segment.Renderer.SetPropertyBlock(_propertyBlock);

                ReturnCombineArray(combineArray);
                instances.Clear();
            }

            DisableQueuedRenderers();
            ActivateSegments(activeSegments);
            DeactivateUnusedSegments(activeSegments);

            UpdateEntityMeshReferences(activeSegments);

            _isCombined = true;

            // Не сбрасываем флаг грязности, если структура была изменена во время комбинирования
            if (_entity != null && !_entity.IsStructureDirty)
                _entity.ClearStructureDirty();

            // Устанавливаем isKinematic в false после объединения
            TrySetEntityKinematic(false);
        }
        finally
        {
            _isCombining = false;
        }
    }

    public void ShowCubes()
    {
        if (!_isCombined) return;

        if (_entity != null)
        {
            _entity.EnsureCacheValid();
        }

        var currentCubes = (_entity != null && _entity.Cubes != null) ? _entity.Cubes : GetComponentsInChildren<Cube>();

        if (currentCubes != null)
        {
            for (int i = 0; i < currentCubes.Length; i++)
            {
                var cube = currentCubes[i];
                if (cube == null || cube.Detouched) continue;

                var meshRenderer = cube.DirectMeshRenderer;
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = true;
                }
            }
        }
        else if (_cachedComponents != null)
        {
            // Fallback на кэш если актуальный список недоступен
            for (int i = 0; i < _cachedComponents.Length; i++)
            {
                var cached = _cachedComponents[i];
                if (cached.MeshRenderer != null && _cubes != null && i < _cubes.Length)
                {
                    var cube = _cubes[i];
                    if (cube != null && !cube.Detouched)
                    {
                        cached.MeshRenderer.enabled = true;
                    }
                }
                else if (cached.MeshRenderer != null)
                {
                    cached.MeshRenderer.enabled = true;
                }
            }
        }

        // Возвращаем объекты в pool используя кэшированные данные
        if (_combinedMeshObject != null)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var segment = _segments[i];
                if (segment.Filter.sharedMesh != null)
                {
                    ReturnMeshToPool(segment.Filter.sharedMesh);
                    segment.Filter.sharedMesh = null;
                }

                if (segment.Object.activeSelf)
                    segment.Object.SetActive(false);
            }

            if (_combinedMeshObject.activeSelf)
                _combinedMeshObject.SetActive(false);
        }

        UpdateEntityMeshReferences(0);

        TrySetEntityKinematic(false);

        _isCombined = false;
    }

    public void ShowCubesAsync()
    {
        if (!_isCombined) return;

        StartCoroutine(ShowCubesCoroutine());
    }

    private IEnumerator ShowCubesCoroutine()
    {
        if (!_isCombined) yield break;

        if (_entity != null)
        {
            _entity.EnsureCacheValid();
        }

        var currentCubes = (_entity != null && _entity.Cubes != null) ? _entity.Cubes : GetComponentsInChildren<Cube>();

        if (currentCubes != null)
        {
            for (int i = 0; i < currentCubes.Length; i++)
            {
                var cube = currentCubes[i];
                if (cube == null || cube.Detouched) continue;

                var meshRenderer = cube.DirectMeshRenderer;
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = true;
                }

                if (i % 20 == 0)
                {
                    yield return null;
                }
            }
        }
        else if (_cachedComponents != null)
        {
            // Fallback на кэш если актуальный список недоступен
            for (int i = 0; i < _cachedComponents.Length; i++)
            {
                var cached = _cachedComponents[i];
                if (cached.MeshRenderer != null && _cubes != null && i < _cubes.Length)
                {
                    var cube = _cubes[i];
                    if (cube != null && !cube.Detouched)
                    {
                        cached.MeshRenderer.enabled = true;
                    }
                }
                else if (cached.MeshRenderer != null)
                {
                    cached.MeshRenderer.enabled = true;
                }

                if (i % 20 == 0)
                {
                    yield return null;
                }
            }
        }

        // Возвращаем объекты в pool используя кэшированные данные
        if (_combinedMeshObject != null)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var segment = _segments[i];
                if (segment.Filter.sharedMesh != null)
                {
                    ReturnMeshToPool(segment.Filter.sharedMesh);
                    segment.Filter.sharedMesh = null;
                }

                if (segment.Object.activeSelf)
                    segment.Object.SetActive(false);

                if (i % 5 == 0)
                {
                    yield return null;
                }
            }

            if (_combinedMeshObject.activeSelf)
                _combinedMeshObject.SetActive(false);
        }

        UpdateEntityMeshReferences(0);

        TrySetEntityKinematic(false);

        _isCombined = false;
    }

    public void CombineMeshesAsync()
    {
        if (_isCombined) return;

        StartCoroutine(CombineMeshesCoroutine());
    }

    private IEnumerator CombineMeshesCoroutine()
    {
        if (_isCombined || _isCombining) yield break;

        _isCombining = true;

        try
        {
            if (_entity != null)
                _entity.EnsureCacheValid();

            var entityCubes = _entity != null ? _entity.Cubes : GetComponentsInChildren<Cube>();
            if (_cachedComponents == null || entityCubes == null || _cachedComponents.Length != entityCubes.Length)
            {
                CacheCubeComponents();
            }

            if (_cachedComponents == null || _cachedComponents.Length == 0)
            {
                _isCombining = false;
                yield break;
            }

            if (_entity != null)
            {
                _entity.EnsureCacheValid();

                if (_entity.IsStructureDirty)
                {
                    var newEntities = _entity.SplitIntoSeparateEntities();
                    if (newEntities.Length > 0)
                    {
                        _isCombining = false;
                        yield break;
                    }
                }
            }

            if (_rb != null || _entity != null)
            {
                _isKinematicOriginalState = GetEntityKinematic();
                TrySetEntityKinematic(true);
            }

            // Очищаем кэш и переиспользуем структуры
            _sourceMaterial = null;

            // Очищаем группы цветов (переиспользуем списки)
            foreach (var list in _colorGroups.Values)
                list.Clear();
            _colorGroups.Clear();
            _colorKeys.Clear();
            ClearRendererDisableQueue();

            // Проверяем валидность transform и _cachedComponents перед использованием
            if (transform == null || _cachedComponents == null || _cachedComponents.Length == 0)
            {
                ShowCubes();
                TrySetEntityKinematic(false);
                _isCombined = false;
                ClearRendererDisableQueue();
                yield break;
            }

            var worldToLocal = transform.worldToLocalMatrix;
            int cachedLength = _cachedComponents != null ? _cachedComponents.Length : 0;
            for (int i = 0; i < cachedLength; i++)
            {
                if (_cachedComponents == null || transform == null || i >= _cachedComponents.Length)
                {
                    ShowCubes();
                    TrySetEntityKinematic(false);
                    _isCombined = false;
                    _isCombining = false;
                    yield break;
                }

                var cached = _cachedComponents[i];
                if (!cached.IsValid || cached.Mesh == null) continue;

                // Проверяем, что куб не был удален между кэшированием и комбинированием
                if (_cubes != null && i < _cubes.Length)
                {
                    var cube = _cubes[i];
                    if (cube == null || cube.Detouched) continue;
                }

                // Проверяем валидность MeshFilter и его transform
                if (cached.MeshFilter == null || cached.MeshFilter.transform == null)
                    continue;

                // Выключаем рендеры
                if (cached.MeshRenderer != null && cached.MeshRenderer.enabled)
                {
                    if (_sourceMaterial == null)
                        _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                    QueueRendererDisable(cached.MeshRenderer);
                }

                var ci = new CombineInstance
                {
                    mesh = cached.Mesh,
                    transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
                };

                if (!_colorGroups.TryGetValue(cached.Color, out var list))
                {
                    list = new System.Collections.Generic.List<CombineInstance>(32);
                    _colorGroups[cached.Color] = list;
                    _colorKeys.Add(cached.Color);
                }

                list.Add(ci);

                if (i % 10 == 0)
                {
                    yield return null;
                }
            }

            if (_sourceMaterial == null)
            {
                ShowCubes();
                TrySetEntityKinematic(false);
                _isCombined = false;
                ClearRendererDisableQueue();
                yield break;
            }

            _combinedMeshObject = EnsureCombinedRoot();
            if (!_combinedMeshObject.activeSelf)
                _combinedMeshObject.SetActive(true);

            // Очищаем MaterialPropertyBlock для переиспользования
            _propertyBlock.Clear();

            int activeSegments = 0;

            // Обходим группы цветов и создаём объединённые меши
            for (int i = 0; i < _colorKeys.Count; i++)
            {
                var color = _colorKeys[i];
                var instances = _colorGroups[color];

                int instanceCount = instances.Count;
                if (instanceCount == 0) continue;

                var combineArray = RentCombineArray(instanceCount);
                for (int j = 0; j < instanceCount; j++)
                {
                    combineArray[j] = instances[j];
                }

                var subMesh = GetPooledMesh();
                subMesh.CombineMeshes(combineArray, true, true);
                if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                    subMesh.bounds = fastBounds;
                else
                    subMesh.RecalculateBounds();

                var segment =
                    EnsureSegment(activeSegments++); // Переиспользуем дочерние объекты, чтобы не трогать SetParent
                if (segment.Object.activeSelf)
                    segment.Object.SetActive(false);

                if (segment.Filter.sharedMesh != null)
                {
                    ReturnMeshToPool(segment.Filter.sharedMesh);
                    segment.Filter.sharedMesh = null;
                }

                segment.Filter.sharedMesh = subMesh;
                segment.Renderer.sharedMaterial = _sourceMaterial;

                _propertyBlock.Clear();
                _propertyBlock.SetColor("_BaseColor", (Color)color);
                _propertyBlock.SetColor("_Color", (Color)color);
                segment.Renderer.SetPropertyBlock(_propertyBlock);

                ReturnCombineArray(combineArray);
                instances.Clear();

                if (i % 2 == 0)
                {
                    yield return null;
                }
            }

            DisableQueuedRenderers();
            ActivateSegments(activeSegments);
            DeactivateUnusedSegments(activeSegments);

            UpdateEntityMeshReferences(activeSegments);

            _isCombined = true;

            // Не сбрасываем флаг грязности, если структура была изменена во время комбинирования
            if (_entity != null && !_entity.IsStructureDirty)
                _entity.ClearStructureDirty();

            // Устанавливаем isKinematic в false после объединения
            TrySetEntityKinematic(false);
        }
        finally
        {
            _isCombining = false;
        }
    }
}