using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// ArrayPool не подходит для Mesh.CombineMeshes (использует всю длину массива)

[RequireComponent(typeof(Entity))]
public class EntityMeshCombiner : MonoBehaviour
{
    [SerializeField] private Entity _entity; // Кэш ссылки на Entity
    private GameObject _combinedMeshObject;
    private Cube[] _cubes;
    private Rigidbody _rb;
    private bool _isKinematicOriginalState;
    private bool _isCombined;

    // Публичное свойство для проверки состояния объединения из Entity
    public bool IsCombined => _isCombined;

    // Кэшированные компоненты для оптимизации
    private struct CachedCubeComponents
    {
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
        public ColorCube ColorCube;
        public Color Color;
    }

    private CachedCubeComponents[] _cachedComponents;

    // Кэш массивов точного размера по цветам, чтобы избежать повторных аллокаций
    private Dictionary<Color, CombineInstance[]> _instancesCache = new Dictionary<Color, CombineInstance[]>();
    private MaterialPropertyBlock _propertyBlock;

    private Material _sourceMaterial;

    // Переиспользуемые словари для подсчётов
    private Dictionary<Color, int> _colorCounts = new Dictionary<Color, int>();
    private Dictionary<Color, int> _writeIndices = new Dictionary<Color, int>();

    // Object pooling для мешей
    private Queue<Mesh> _meshPool = new Queue<Mesh>();
    private Queue<GameObject> _gameObjectPool = new Queue<GameObject>();

    // Кэш для дочерних объектов combined mesh
    private GameObject[] _cachedChildObjects = System.Array.Empty<GameObject>();
    private MeshFilter[] _cachedMeshFilters = System.Array.Empty<MeshFilter>();

    private void Awake()
    {
        if (_entity == null)
            _entity = GetComponent<Entity>();
        _rb = GetComponent<Rigidbody>();
        _propertyBlock = new MaterialPropertyBlock();
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
        // Очистка object pool
        while (_meshPool.Count > 0)
        {
            var mesh = _meshPool.Dequeue();
            if (mesh != null) DestroyImmediate(mesh);
        }

        while (_gameObjectPool.Count > 0)
        {
            var go = _gameObjectPool.Dequeue();
            if (go != null) DestroyImmediate(go);
        }
    }

    private void CacheCubeComponents()
    {
        // Берем кэш из Entity если доступен, иначе fallback на поиск в иерархии
        _cubes = (_entity != null && _entity.Cubes != null) ? _entity.Cubes : GetComponentsInChildren<Cube>();
        if (_cubes == null || _cubes.Length == 0) return;

        _cachedComponents = new CachedCubeComponents[_cubes.Length];

        for (int i = 0; i < _cubes.Length; i++)
        {
            var cube = _cubes[i];
            var meshFilter = cube.MeshFilter;
            var meshRenderer = cube.MeshRenderer;
            var colorCube = cube.ColorCube;

            _cachedComponents[i] = new CachedCubeComponents
            {
                MeshFilter = meshFilter,
                MeshRenderer = meshRenderer,
                ColorCube = colorCube,
                Color = cube.Color
            };
        }
    }

    // Инвалидируем кэш компонентов кубов (пересоздастся при следующем комбинировании)
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

    private GameObject GetPooledGameObject(string objectName)
    {
        if (_gameObjectPool.Count > 0)
        {
            var go = _gameObjectPool.Dequeue();
            go.name = objectName;
            go.SetActive(true);
            return go;
        }

        return new GameObject(objectName);
    }

    private void ReturnGameObjectToPool(GameObject go)
    {
        if (go != null)
        {
            go.SetActive(false);
            _gameObjectPool.Enqueue(go);
        }
    }

    private void CacheChildObjects()
    {
        if (_combinedMeshObject != null)
        {
            int childCount = _combinedMeshObject.transform.childCount;
            _cachedChildObjects = new GameObject[childCount];

            int meshFilterCount = 0;
            for (int i = 0; i < childCount; i++)
            {
                var child = _combinedMeshObject.transform.GetChild(i).gameObject;
                _cachedChildObjects[i] = child;

                if (child.GetComponent<MeshFilter>() != null)
                {
                    meshFilterCount++;
                }
            }

            _cachedMeshFilters = new MeshFilter[meshFilterCount];
            int writeIndex = 0;
            for (int i = 0; i < childCount; i++)
            {
                var meshFilter = _cachedChildObjects[i].GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    _cachedMeshFilters[writeIndex++] = meshFilter;
                }
            }
        }
        else
        {
            _cachedChildObjects = System.Array.Empty<GameObject>();
            _cachedMeshFilters = System.Array.Empty<MeshFilter>();
        }
    }

    public void CombineMeshes()
    {
        if (_isCombined) return;

        // Гарантируем валидность кэша кубов в Entity
        if (_entity != null)
            _entity.EnsureCacheValid();

        // Перестраиваем кэш только при необходимости (надежно и без лишней работы)
        var entityCubes = _entity != null ? _entity.Cubes : GetComponentsInChildren<Cube>();
        if (_cachedComponents == null || entityCubes == null || _cachedComponents.Length != entityCubes.Length)
        {
            CacheCubeComponents();
        }

        if (_cachedComponents == null || _cachedComponents.Length == 0) return;

        // Проверяем и разделяем кубы на отдельные Entity если необходимо
        if (_entity != null)
        {
            // Дешёвая гарантия валидности кэша вместо тяжёлого пересчёта
            _entity.EnsureCacheValid();

            // Если структура не менялась, пересборка не нужна
            if (!_entity.IsStructureDirty && _isCombined)
            {
                return;
            }

            // Автоматически разделяем кубы на отдельные Entity если обнаружено несколько групп
            var newEntities = _entity.SplitIntoSeparateEntities();

            // Если были созданы новые Entity, прерываем сборку меша для текущего Entity
            if (newEntities.Length > 0)
            {
                return;
            }
        }

        if (_rb != null)
        {
            _isKinematicOriginalState = _rb.isKinematic;
            _rb.isKinematic = true;
        }

        // Очищаем кэш и переиспользуем структуры
        _sourceMaterial = null;
        _sourceMaterial = null;

        // Первый проход: считаем количества по цветам и выключаем рендеры
        _colorCounts.Clear();
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];

            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            if (cached.MeshRenderer != null)
            {
                if (_sourceMaterial == null)
                {
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                }

                cached.MeshRenderer.enabled = false;
            }

            if (_colorCounts.TryGetValue(cached.Color, out int count))
                _colorCounts[cached.Color] = count + 1;
            else
                _colorCounts[cached.Color] = 1;
        }

        // Обеспечиваем массивы точного размера в кэше
        foreach (var kvp in _colorCounts)
        {
            if (!_instancesCache.TryGetValue(kvp.Key, out var arr) || arr == null || arr.Length != kvp.Value)
            {
                _instancesCache[kvp.Key] = new CombineInstance[kvp.Value];
            }
        }

        // Подготовим индексы записи
        _writeIndices.Clear();
        foreach (var kvp in _colorCounts)
        {
            _writeIndices[kvp.Key] = 0;
        }

        // Второй проход: заполняем массивы CombineInstance
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            var ci = new CombineInstance
            {
                mesh = cached.MeshFilter.sharedMesh,
                transform = transform.worldToLocalMatrix * cached.MeshFilter.transform.localToWorldMatrix
            };

            int idx = _writeIndices[cached.Color];
            _instancesCache[cached.Color][idx] = ci;
            _writeIndices[cached.Color] = idx + 1;
        }

        if (_sourceMaterial == null)
        {
            ShowCubes();
            if (_rb != null) _rb.isKinematic = _isKinematicOriginalState;
            _isCombined = false;
            return;
        }

        _combinedMeshObject = GetPooledGameObject("CombinedMesh");
        _combinedMeshObject.transform.SetParent(transform, false);

        // Очищаем MaterialPropertyBlock для переиспользования
        _propertyBlock.Clear();

        // Обходим только актуальные цвета; старые группы удаляем из кэша
        var colorsToIterate = new System.Collections.Generic.List<Color>(_colorCounts.Keys);
        for (int ci = 0; ci < colorsToIterate.Count; ci++)
        {
            var color = colorsToIterate[ci];
            var instances = _instancesCache[color];

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances, true, true);

            // Для кубов хватает пересчёта границ
            subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{color.ToString()}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            _propertyBlock.SetColor("_BaseColor", color);
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        // Чистим устаревшие записи цветов из кэша, чтобы не собирать лишние меши в будущем
        var toRemove = new System.Collections.Generic.List<Color>();
        foreach (var kv in _instancesCache)
        {
            if (!_colorCounts.ContainsKey(kv.Key))
                toRemove.Add(kv.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
            _instancesCache.Remove(toRemove[i]);

        // Кэшируем дочерние объекты для быстрого доступа в ShowCubes
        CacheChildObjects();

        _isCombined = true;

        // Сбрасываем флаг грязности после успешной сборки
        if (_entity != null)
            _entity.ClearStructureDirty();
    }

    public void ShowCubes()
    {
        if (!_isCombined) return;

        // Используем кэшированные компоненты для быстрого восстановления
        if (_cachedComponents != null)
        {
            for (int i = 0; i < _cachedComponents.Length; i++)
            {
                var cached = _cachedComponents[i];
                if (cached.MeshRenderer != null)
                {
                    cached.MeshRenderer.enabled = true;
                }
            }
        }

        // Возвращаем объекты в pool используя кэшированные данные
        if (_combinedMeshObject != null)
        {
            // Возвращаем меши в pool используя кэшированные MeshFilter
            for (int i = 0; i < _cachedMeshFilters.Length; i++)
            {
                var meshFilter = _cachedMeshFilters[i];
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    ReturnMeshToPool(meshFilter.sharedMesh);
                }
            }

            // Возвращаем дочерние GameObject в pool используя кэш
            for (int i = 0; i < _cachedChildObjects.Length; i++)
            {
                var child = _cachedChildObjects[i];
                if (child != null)
                {
                    ReturnGameObjectToPool(child);
                }
            }

            ReturnGameObjectToPool(_combinedMeshObject);
            _combinedMeshObject = null;
        }

        if (_rb != null)
        {
            _rb.isKinematic = _isKinematicOriginalState;
        }

        _isCombined = false;
    }

    // Асинхронная версия ShowCubes для больших объектов
    public void ShowCubesAsync()
    {
        if (!_isCombined) return;

        StartCoroutine(ShowCubesCoroutine());
    }

    private IEnumerator ShowCubesCoroutine()
    {
        if (!_isCombined) yield break;

        // Используем кэшированные компоненты для быстрого восстановления
        if (_cachedComponents != null)
        {
            for (int i = 0; i < _cachedComponents.Length; i++)
            {
                var cached = _cachedComponents[i];
                if (cached.MeshRenderer != null)
                {
                    cached.MeshRenderer.enabled = true;
                }

                // Yield каждые 20 кубов для предотвращения фризов
                if (i % 20 == 0)
                {
                    yield return null;
                }
            }
        }

        // Возвращаем объекты в pool используя кэшированные данные
        if (_combinedMeshObject != null)
        {
            // Возвращаем меши в pool используя кэшированные MeshFilter
            for (int i = 0; i < _cachedMeshFilters.Length; i++)
            {
                var meshFilter = _cachedMeshFilters[i];
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    ReturnMeshToPool(meshFilter.sharedMesh);
                }

                // Yield каждые 5 мешей
                if (i % 5 == 0)
                {
                    yield return null;
                }
            }

            // Возвращаем дочерние GameObject в pool используя кэш
            for (int i = 0; i < _cachedChildObjects.Length; i++)
            {
                var child = _cachedChildObjects[i];
                if (child != null)
                {
                    ReturnGameObjectToPool(child);
                }

                // Yield каждые 5 объектов
                if (i % 5 == 0)
                {
                    yield return null;
                }
            }

            ReturnGameObjectToPool(_combinedMeshObject);
            _combinedMeshObject = null;
        }

        if (_rb != null)
        {
            _rb.isKinematic = _isKinematicOriginalState;
        }

        _isCombined = false;
    }

    // Асинхронная версия для больших мешей
    public void CombineMeshesAsync()
    {
        if (_isCombined) return;

        StartCoroutine(CombineMeshesCoroutine());
    }

    private IEnumerator CombineMeshesCoroutine()
    {
        if (_isCombined) yield break;

        // Гарантируем валидность кэша кубов в Entity
        if (_entity != null)
            _entity.EnsureCacheValid();

        // Перестраиваем кэш только при необходимости
        var entityCubes = _entity != null ? _entity.Cubes : GetComponentsInChildren<Cube>();
        if (_cachedComponents == null || entityCubes == null || _cachedComponents.Length != entityCubes.Length)
        {
            CacheCubeComponents();
        }

        if (_cachedComponents == null || _cachedComponents.Length == 0) yield break;

        // Проверяем и разделяем кубы на отдельные Entity если необходимо
        if (_entity != null)
        {
            _entity.EnsureCacheValid();

            if (!_entity.IsStructureDirty && _isCombined)
            {
                yield break;
            }

            var newEntities = _entity.SplitIntoSeparateEntities();
            if (newEntities.Length > 0)
            {
                yield break;
            }
        }

        if (_rb != null)
        {
            _isKinematicOriginalState = _rb.isKinematic;
            _rb.isKinematic = true;
        }

        // Очищаем кэш и переиспользуем структуры
        _sourceMaterial = null;

        // Первый проход: считаем количества по цветам и выключаем рендеры
        _colorCounts.Clear();
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];

            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            if (cached.MeshRenderer != null)
            {
                if (_sourceMaterial == null)
                {
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                }

                cached.MeshRenderer.enabled = false;
            }

            if (_colorCounts.TryGetValue(cached.Color, out int count))
                _colorCounts[cached.Color] = count + 1;
            else
                _colorCounts[cached.Color] = 1;

            // Yield каждые 10 кубов для предотвращения фризов
            if (i % 10 == 0)
            {
                yield return null;
            }
        }

        // Обеспечиваем массивы точного размера в кэше
        foreach (var kvp in _colorCounts)
        {
            if (!_instancesCache.TryGetValue(kvp.Key, out var arr) || arr == null || arr.Length != kvp.Value)
            {
                _instancesCache[kvp.Key] = new CombineInstance[kvp.Value];
            }
        }

        // Подготовим индексы записи
        _writeIndices.Clear();
        foreach (var kvp in _colorCounts)
        {
            _writeIndices[kvp.Key] = 0;
        }

        // Второй проход: заполняем массивы CombineInstance
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            var ci = new CombineInstance
            {
                mesh = cached.MeshFilter.sharedMesh,
                transform = transform.worldToLocalMatrix * cached.MeshFilter.transform.localToWorldMatrix
            };

            int idx = _writeIndices[cached.Color];
            _instancesCache[cached.Color][idx] = ci;
            _writeIndices[cached.Color] = idx + 1;

            // Yield каждые 10 кубов для предотвращения фризов
            if (i % 10 == 0)
            {
                yield return null;
            }
        }

        if (_sourceMaterial == null)
        {
            ShowCubes();
            if (_rb != null) _rb.isKinematic = _isKinematicOriginalState;
            _isCombined = false;
            yield break;
        }

        _combinedMeshObject = GetPooledGameObject("CombinedMesh");
        _combinedMeshObject.transform.SetParent(transform, false);

        // Очищаем MaterialPropertyBlock для переиспользования
        _propertyBlock.Clear();

        // Обходим только актуальные цвета; старые группы удаляем из кэша
        int colorIndex = 0;
        var colorsToIterate = new System.Collections.Generic.List<Color>(_colorCounts.Keys);
        for (int ci = 0; ci < colorsToIterate.Count; ci++)
        {
            var color = colorsToIterate[ci];
            var instances = _instancesCache[color];

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances, true, true);
            subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{color.ToString()}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            _propertyBlock.SetColor("_BaseColor", color);
            meshRenderer.SetPropertyBlock(_propertyBlock);

            // Yield каждые 2 цвета для предотвращения фризов
            if (colorIndex % 2 == 0)
            {
                yield return null;
            }

            colorIndex++;
        }

        // Чистим устаревшие записи цветов из кэша
        var toRemove = new System.Collections.Generic.List<Color>();
        foreach (var kv in _instancesCache)
        {
            if (!_colorCounts.ContainsKey(kv.Key))
                toRemove.Add(kv.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
            _instancesCache.Remove(toRemove[i]);

        // Кэшируем дочерние объекты для быстрого доступа в ShowCubes
        CacheChildObjects();

        _isCombined = true;

        if (_entity != null)
            _entity.ClearStructureDirty();
    }
}