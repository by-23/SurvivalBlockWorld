using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(Entity))]
public class EntityMeshCombiner : MonoBehaviour
{
    private GameObject _combinedMeshObject;
    private Cube[] _cubes;
    private Rigidbody _rb;
    private bool _isKinematicOriginalState;
    private bool _isCombined;

    // Кэшированные компоненты для оптимизации
    private struct CachedCubeComponents
    {
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
        public ColorCube ColorCube;
        public Color Color;
    }

    private CachedCubeComponents[] _cachedComponents;
    private Dictionary<Color, List<CombineInstance>> _cubesByColorCache;
    private MaterialPropertyBlock _propertyBlock;
    private Material _sourceMaterial;

    // Object pooling для мешей
    private Queue<Mesh> _meshPool = new Queue<Mesh>();
    private Queue<GameObject> _gameObjectPool = new Queue<GameObject>();

    // Кэш для дочерних объектов combined mesh
    private List<GameObject> _cachedChildObjects = new List<GameObject>();
    private List<MeshFilter> _cachedMeshFilters = new List<MeshFilter>();

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _propertyBlock = new MaterialPropertyBlock();
        _cubesByColorCache = new Dictionary<Color, List<CombineInstance>>();
    }

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
        _cubes = GetComponentsInChildren<Cube>();
        if (_cubes.Length == 0) return;

        _cachedComponents = new CachedCubeComponents[_cubes.Length];

        for (int i = 0; i < _cubes.Length; i++)
        {
            var cube = _cubes[i];
            var meshFilter = cube.GetComponent<MeshFilter>();
            var meshRenderer = cube.GetComponent<MeshRenderer>();
            var colorCube = cube.GetComponent<ColorCube>();

            _cachedComponents[i] = new CachedCubeComponents
            {
                MeshFilter = meshFilter,
                MeshRenderer = meshRenderer,
                ColorCube = colorCube,
                Color = colorCube != null ? colorCube.GetColor32() : Color.white
            };
        }
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

    private GameObject GetPooledGameObject(string name)
    {
        if (_gameObjectPool.Count > 0)
        {
            var go = _gameObjectPool.Dequeue();
            go.name = name;
            go.SetActive(true);
            return go;
        }

        return new GameObject(name);
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
        _cachedChildObjects.Clear();
        _cachedMeshFilters.Clear();

        if (_combinedMeshObject != null)
        {
            int childCount = _combinedMeshObject.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = _combinedMeshObject.transform.GetChild(i).gameObject;
                _cachedChildObjects.Add(child);

                var meshFilter = child.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    _cachedMeshFilters.Add(meshFilter);
                }
            }
        }
    }

    public void CombineMeshes()
    {
        if (_isCombined) return;

        // Кэшируем компоненты кубов один раз
        CacheCubeComponents();
        if (_cachedComponents == null || _cachedComponents.Length == 0) return;

        // Проверяем и разделяем кубы на отдельные Entity если необходимо
        Entity entity = GetComponent<Entity>();
        if (entity != null)
        {
            // Принудительно обновляем данные кубов перед проверкой групп
            entity.UpdateMassAndCubes();

            // Автоматически разделяем кубы на отдельные Entity если обнаружено несколько групп
            var newEntities = entity.SplitIntoSeparateEntities();

            // Если были созданы новые Entity, прерываем сборку меша для текущего Entity
            if (newEntities.Count > 0)
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
        _cubesByColorCache.Clear();
        _sourceMaterial = null;

        // Проходим по кэшированным компонентам
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

            // Переиспользуем существующие списки или создаем новые
            if (!_cubesByColorCache.ContainsKey(cached.Color))
            {
                _cubesByColorCache[cached.Color] = new List<CombineInstance>();
            }

            var ci = new CombineInstance
            {
                mesh = cached.MeshFilter.sharedMesh,
                transform = transform.worldToLocalMatrix * cached.MeshFilter.transform.localToWorldMatrix
            };
            _cubesByColorCache[cached.Color].Add(ci);
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

        foreach (var colorGroup in _cubesByColorCache)
        {
            var color = colorGroup.Key;
            var instances = colorGroup.Value;

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances.ToArray(), true, true);

            // Убираем дорогие операции RecalculateNormals и Optimize
            // Они не нужны для простых кубов и сильно замедляют процесс
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

        // Кэшируем дочерние объекты для быстрого доступа в ShowCubes
        CacheChildObjects();

        _isCombined = true;
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
            for (int i = 0; i < _cachedMeshFilters.Count; i++)
            {
                var meshFilter = _cachedMeshFilters[i];
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    ReturnMeshToPool(meshFilter.sharedMesh);
                }
            }

            // Возвращаем дочерние GameObject в pool используя кэш
            for (int i = 0; i < _cachedChildObjects.Count; i++)
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
            for (int i = 0; i < _cachedMeshFilters.Count; i++)
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
            for (int i = 0; i < _cachedChildObjects.Count; i++)
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

        // Кэшируем компоненты кубов один раз
        CacheCubeComponents();
        if (_cachedComponents == null || _cachedComponents.Length == 0) yield break;

        // Проверяем и разделяем кубы на отдельные Entity если необходимо
        Entity entity = GetComponent<Entity>();
        if (entity != null)
        {
            // Принудительно обновляем данные кубов перед проверкой групп
            entity.UpdateMassAndCubes();

            // Автоматически разделяем кубы на отдельные Entity если обнаружено несколько групп
            var newEntities = entity.SplitIntoSeparateEntities();

            // Если были созданы новые Entity, прерываем сборку меша для текущего Entity
            if (newEntities.Count > 0)
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
        _cubesByColorCache.Clear();
        _sourceMaterial = null;

        // Проходим по кэшированным компонентам
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

            // Переиспользуем существующие списки или создаем новые
            if (!_cubesByColorCache.ContainsKey(cached.Color))
            {
                _cubesByColorCache[cached.Color] = new List<CombineInstance>();
            }

            var ci = new CombineInstance
            {
                mesh = cached.MeshFilter.sharedMesh,
                transform = transform.worldToLocalMatrix * cached.MeshFilter.transform.localToWorldMatrix
            };
            _cubesByColorCache[cached.Color].Add(ci);

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

        int colorIndex = 0;
        foreach (var colorGroup in _cubesByColorCache)
        {
            var color = colorGroup.Key;
            var instances = colorGroup.Value;

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances.ToArray(), true, true);
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

        // Кэшируем дочерние объекты для быстрого доступа в ShowCubes
        CacheChildObjects();

        _isCombined = true;
    }
}