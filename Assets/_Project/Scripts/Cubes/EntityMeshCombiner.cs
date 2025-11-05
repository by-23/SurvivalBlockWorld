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
        public Color32 Color; // используем Color32 как ключ
        public Mesh Mesh; // кэш меша (убирает 6075 вызовов get_sharedMesh)
        public bool IsValid; // флаг валидности (убирает проверки на null)
    }

    private CachedCubeComponents[] _cachedComponents;


    private MaterialPropertyBlock _propertyBlock;

    private Material _sourceMaterial;

    // Упрощённая структура: Dictionary напрямую хранит списки CombineInstance по цветам
    // Pre-allocate capacity для избежания реаллокаций при группировке
    private Dictionary<Color32, System.Collections.Generic.List<CombineInstance>> _colorGroups =
        new Dictionary<Color32, System.Collections.Generic.List<CombineInstance>>(16);

    // Переиспользуемый список для ключей при обходе
    private readonly System.Collections.Generic.List<Color32> _colorKeys =
        new System.Collections.Generic.List<Color32>(16);

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
            if (cube == null) continue;

            // Используем прямые ссылки (убирает проверки свойств)
            var meshFilter = cube.DirectMeshFilter;
            var meshRenderer = cube.DirectMeshRenderer;

            // Используем кэшированные значения (убирает 2003 вызова get_sharedMesh и 2029 вызовов get_Color)
            Mesh mesh = cube.CachedMesh;
            Color32 color = cube.CachedColor32;

            // Быстрая проверка валидности без лишних вызовов
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

        // Перестраиваем кэш только при необходимости (надежно и без лишней работы)
        var entityCubes = _entity != null ? _entity.Cubes : GetComponentsInChildren<Cube>();
        if (_cachedComponents == null || entityCubes == null || _cachedComponents.Length != entityCubes.Length)
        {
            CacheCubeComponents();
        }

        if (_cachedComponents == null || _cachedComponents.Length == 0) return;

        // Мало кубов — выгоды от Combine нет
        if (_cachedComponents.Length <= 8) return;

        // Гарантируем валидность кэша кубов в Entity (один раз)
        if (_entity != null)
        {
            _entity.EnsureCacheValid();

            // Разделяем только если структура менялась
            if (_entity.IsStructureDirty)
            {
                var newEntities = _entity.SplitIntoSeparateEntities();
                if (newEntities.Length > 0)
                {
                    return;
                }
            }
        }

        if (_rb != null)
        {
            _isKinematicOriginalState = _rb.isKinematic;
            _rb.isKinematic = true;
        }

        // Очищаем кэш и переиспользуем структуры
        _sourceMaterial = null;

        // Очищаем группы цветов (переиспользуем списки)
        foreach (var list in _colorGroups.Values)
            list.Clear();
        _colorGroups.Clear();
        _colorKeys.Clear();

        // Один проход: группируем кубы по цветам и сразу создаём CombineInstance
        var worldToLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (!cached.IsValid || cached.Mesh == null) continue;

            // Выключаем рендеры
            if (cached.MeshRenderer.enabled)
            {
                if (_sourceMaterial == null)
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                cached.MeshRenderer.enabled = false;
            }

            // Создаём CombineInstance сразу
            var ci = new CombineInstance
            {
                mesh = cached.Mesh,
                transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
            };

            // Группируем по цвету напрямую (Dictionary с pre-allocated capacity)
            if (!_colorGroups.TryGetValue(cached.Color, out var list))
            {
                list = new System.Collections.Generic.List<CombineInstance>(
                    32); // pre-allocate для типичного количества кубов одного цвета
                _colorGroups[cached.Color] = list;
                _colorKeys.Add(cached.Color);
            }

            list.Add(ci);
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

        // Обходим группы цветов и создаём объединённые меши
        for (int i = 0; i < _colorKeys.Count; i++)
        {
            var color = _colorKeys[i];
            var instances = _colorGroups[color];

            if (instances.Count == 0) continue;

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances.ToArray(), true, true);

            // Для кубов задаём границы напрямую при наличии данных
            if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                subMesh.bounds = fastBounds;
            else
                subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{i}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            // Очищаем PropertyBlock перед установкой цвета для каждого объекта
            _propertyBlock.Clear();
            _propertyBlock.SetColor("_BaseColor", (Color)color);
            _propertyBlock.SetColor("_Color", (Color)color); // Fallback для стандартных шейдеров
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

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
        if (_cachedComponents.Length <= 8) yield break;

        // Проверяем и разделяем кубы на отдельные Entity если необходимо
        if (_entity != null)
        {
            _entity.EnsureCacheValid();

            // Разделяем только если структура менялась
            if (_entity.IsStructureDirty)
            {
                var newEntities = _entity.SplitIntoSeparateEntities();
                if (newEntities.Length > 0)
                {
                    yield break;
                }
            }
        }

        if (_rb != null)
        {
            _isKinematicOriginalState = _rb.isKinematic;
            _rb.isKinematic = true;
        }

        // Очищаем кэш и переиспользуем структуры
        _sourceMaterial = null;

        // Очищаем группы цветов (переиспользуем списки)
        foreach (var list in _colorGroups.Values)
            list.Clear();
        _colorGroups.Clear();
        _colorKeys.Clear();

        // Один проход: группируем кубы по цветам и сразу создаём CombineInstance
        var worldToLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (!cached.IsValid || cached.Mesh == null) continue;

            // Выключаем рендеры
            if (cached.MeshRenderer.enabled)
            {
                if (_sourceMaterial == null)
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                cached.MeshRenderer.enabled = false;
            }

            // Создаём CombineInstance сразу
            var ci = new CombineInstance
            {
                mesh = cached.Mesh,
                transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
            };

            // Группируем по цвету напрямую
            if (!_colorGroups.TryGetValue(cached.Color, out var list))
            {
                list = new System.Collections.Generic.List<CombineInstance>(32);
                _colorGroups[cached.Color] = list;
                _colorKeys.Add(cached.Color);
            }

            list.Add(ci);

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

        // Обходим группы цветов и создаём объединённые меши
        for (int i = 0; i < _colorKeys.Count; i++)
        {
            var color = _colorKeys[i];
            var instances = _colorGroups[color];

            if (instances.Count == 0) continue;

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances.ToArray(), true, true);
            if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                subMesh.bounds = fastBounds;
            else
                subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{i}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            // Очищаем PropertyBlock перед установкой цвета для каждого объекта
            _propertyBlock.Clear();
            _propertyBlock.SetColor("_BaseColor", (Color)color);
            _propertyBlock.SetColor("_Color", (Color)color); // Fallback для стандартных шейдеров
            meshRenderer.SetPropertyBlock(_propertyBlock);

            // Yield каждые 2 цвета для предотвращения фризов
            if (i % 2 == 0)
            {
                yield return null;
            }
        }

        // Кэшируем дочерние объекты для быстрого доступа в ShowCubes
        CacheChildObjects();

        _isCombined = true;

        if (_entity != null)
            _entity.ClearStructureDirty();
    }
}