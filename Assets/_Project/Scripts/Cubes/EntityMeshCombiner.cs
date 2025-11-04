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
        public int ColorIndex; // индекс цвета в _uniqueColors (убирает TryGetValue)
        public bool IsValid; // флаг валидности (убирает проверки на null)
    }

    private CachedCubeComponents[] _cachedComponents;

    // Кэш массивов точного размера по цветам, чтобы избежать повторных аллокаций
    private Dictionary<Color32, CombineInstance[]> _instancesCache = new Dictionary<Color32, CombineInstance[]>();

    // Быстрый доступ по индексу уникального цвета в текущем Combine (без Dictionary.get_Item)
    private System.Collections.Generic.List<CombineInstance[]> _instancesByIndex =
        new System.Collections.Generic.List<CombineInstance[]>(16);

    private MaterialPropertyBlock _propertyBlock;

    private Material _sourceMaterial;

    // Переиспользуемые структуры для подсчётов без дорогих Dictionary.set по каждому кубу
    private Dictionary<Color32, int> _colorToIndex = new Dictionary<Color32, int>(16);

    private readonly System.Collections.Generic.List<Color32> _uniqueColors =
        new System.Collections.Generic.List<Color32>(16);

    private readonly System.Collections.Generic.List<int> _colorCountsList =
        new System.Collections.Generic.List<int>(16);

    private int[] _writeIndicesArray = System.Array.Empty<int>();

    private Color32[] _tempColors = System.Array.Empty<Color32>();

    // Маппинг индекс куба -> индекс цвета в _uniqueColors (убирает TryGetValue во втором проходе)
    private int[] _cubeToColorIndex = System.Array.Empty<int>();

    // Object pooling для мешей
    private Queue<Mesh> _meshPool = new Queue<Mesh>();
    private Queue<GameObject> _gameObjectPool = new Queue<GameObject>();

    // Переиспользуемый список цветов для обхода
    private readonly System.Collections.Generic.List<Color32> _colorsToIterate =
        new System.Collections.Generic.List<Color32>(32);

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

    // Компаратор для быстрой сортировки Color32
    private sealed class Color32Comparer : System.Collections.Generic.IComparer<Color32>
    {
        public static readonly Color32Comparer Instance = new Color32Comparer();
        private static uint Pack(Color32 c) => ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;

        public int Compare(Color32 x, Color32 y)
        {
            uint a = Pack(x);
            uint b = Pack(y);
            return a < b ? -1 : (a > b ? 1 : 0);
        }
    }

    // Компаратор для сортировки индексов по цветам
    private sealed class ColorIndexComparer : System.Collections.Generic.IComparer<int>
    {
        private readonly Color32[] _colors;

        public ColorIndexComparer(Color32[] colors)
        {
            _colors = colors;
        }

        private static uint Pack(Color32 c) => ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;

        public int Compare(int x, int y)
        {
            uint a = Pack(_colors[x]);
            uint b = Pack(_colors[y]);
            return a < b ? -1 : (a > b ? 1 : 0);
        }
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

            // Кэшируем mesh сразу (убирает 6075 вызовов get_sharedMesh)
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;

            _cachedComponents[i] = new CachedCubeComponents
            {
                MeshFilter = meshFilter,
                MeshRenderer = meshRenderer,
                ColorCube = colorCube,
                Color = (Color32)cube.Color,
                Mesh = mesh,
                ColorIndex = -1, // установится в первом проходе
                IsValid = meshFilter != null && mesh != null && meshRenderer != null
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

        // Первый проход: собираем цвета и выключаем рендеры (без Dictionary.set на каждый куб)
        int validCount = 0;
        if (_tempColors.Length < _cachedComponents.Length)
            _tempColors = new Color32[_cachedComponents.Length];
        if (_cubeToColorIndex.Length < _cachedComponents.Length)
        {
            _cubeToColorIndex = new int[_cachedComponents.Length];
            // Инициализируем все индексы как невалидные
            for (int k = 0; k < _cubeToColorIndex.Length; k++)
                _cubeToColorIndex[k] = -1;
        }
        else
        {
            // Сбрасываем индексы для невалидных кубов
            for (int k = 0; k < _cubeToColorIndex.Length; k++)
                _cubeToColorIndex[k] = -1;
        }

        // Сначала собираем валидные кубы с их индексами
        var validCubeIndices = new int[_cachedComponents.Length]; // маппинг validCount -> оригинальный индекс куба
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];

            // Используем флаг валидности вместо проверок на null
            if (!cached.IsValid || cached.Mesh == null) continue;

            if (cached.MeshRenderer.enabled)
            {
                if (_sourceMaterial == null)
                {
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                }

                cached.MeshRenderer.enabled = false;
            }

            _tempColors[validCount] = cached.Color;
            validCubeIndices[validCount] = i; // сохраняем оригинальный индекс куба
            validCount++;
        }

        // Считаем количества по цветам через сортировку и линейный проход
        _uniqueColors.Clear();
        _colorCountsList.Clear();
        _colorToIndex.Clear();
        if (validCount > 0)
        {
            // Сортируем индексы по цветам
            var sortedIndices = new int[validCount];
            for (int i = 0; i < validCount; i++) sortedIndices[i] = i;
            System.Array.Sort(sortedIndices, 0, validCount, new ColorIndexComparer(_tempColors));

            Color32 current = _tempColors[sortedIndices[0]];
            int groupStart = 0;
            int count = 1;

            for (int i = 1; i < validCount; i++)
            {
                int idx = sortedIndices[i];
                var c = _tempColors[idx];

                if (c.r == current.r && c.g == current.g && c.b == current.b && c.a == current.a)
                {
                    count++;
                }
                else
                {
                    int colorIdx = _uniqueColors.Count;
                    _uniqueColors.Add(current);
                    _colorCountsList.Add(count);
                    _colorToIndex[current] = colorIdx;

                    // Устанавливаем индекс цвета для всех кубов этой группы (по оригинальным индексам)
                    for (int j = groupStart; j < groupStart + count; j++)
                    {
                        int origCubeIdx = validCubeIndices[sortedIndices[j]];
                        _cubeToColorIndex[origCubeIdx] = colorIdx;
                    }

                    current = c;
                    groupStart = i;
                    count = 1;
                }
            }

            // последний диапазон
            int lastColorIdx = _uniqueColors.Count;
            _uniqueColors.Add(current);
            _colorCountsList.Add(count);
            _colorToIndex[current] = lastColorIdx;

            // Устанавливаем индекс для последней группы
            for (int j = groupStart; j < groupStart + count; j++)
            {
                int origCubeIdx = validCubeIndices[sortedIndices[j]];
                _cubeToColorIndex[origCubeIdx] = lastColorIdx;
            }
        }

        // Обеспечиваем массивы точного размера в кэше
        _instancesByIndex.Clear();
        if (_instancesByIndex.Capacity < _uniqueColors.Count) _instancesByIndex.Capacity = _uniqueColors.Count;
        for (int i = 0; i < _uniqueColors.Count; i++)
        {
            var col = _uniqueColors[i];
            int cnt = _colorCountsList[i];
            if (!_instancesCache.TryGetValue(col, out var arr) || arr == null || arr.Length != cnt)
            {
                _instancesCache[col] = new CombineInstance[cnt];
                arr = _instancesCache[col];
            }
            else
            {
                arr = _instancesCache[col];
            }

            _instancesByIndex.Add(arr);
        }

        // Подготовим индексы записи (массив по индексам цветов)
        if (_writeIndicesArray.Length < _uniqueColors.Count)
            _writeIndicesArray = new int[_uniqueColors.Count];
        else
            System.Array.Clear(_writeIndicesArray, 0, _uniqueColors.Count);

        // Второй проход: заполняем массивы CombineInstance (без get_sharedMesh и TryGetValue)
        var worldToLocal = transform.worldToLocalMatrix; // кэшируем матрицу
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (!cached.IsValid || cached.Mesh == null) continue;

            // Используем кэшированный mesh (без get_sharedMesh)
            var ci = new CombineInstance
            {
                mesh = cached.Mesh,
                transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
            };

            // Используем маппинг индексов (без TryGetValue)
            int colorIdx = _cubeToColorIndex[i];
            if (colorIdx >= 0 && colorIdx < _instancesByIndex.Count)
            {
                int write = _writeIndicesArray[colorIdx];
                _instancesByIndex[colorIdx][write] = ci;
                _writeIndicesArray[colorIdx] = write + 1;
            }
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
        for (int ci = 0; ci < _uniqueColors.Count; ci++)
        {
            var color = _uniqueColors[ci];
            var instances = _instancesByIndex[ci];

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances, true, true);

            // Для кубов задаём границы напрямую при наличии данных
            if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                subMesh.bounds = fastBounds;
            else
                subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{ci}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            _propertyBlock.SetColor("_BaseColor", (Color)color);
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        // Чистим устаревшие записи цветов из кэша, чтобы не собирать лишние меши в будущем
        var toRemove = new System.Collections.Generic.List<Color32>();
        foreach (var kv in _instancesCache)
        {
            // удаляем те цвета, которых нет в текущем наборе
            if (!_colorToIndex.ContainsKey(kv.Key))
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

        // Первый проход: собираем цвета и выключаем рендеры (без Dictionary.set на каждый куб)
        int validCount = 0;
        if (_tempColors.Length < _cachedComponents.Length)
            _tempColors = new Color32[_cachedComponents.Length];
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];

            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            if (cached.MeshRenderer != null && cached.MeshRenderer.enabled)
            {
                if (_sourceMaterial == null)
                {
                    _sourceMaterial = cached.MeshRenderer.sharedMaterial;
                }

                cached.MeshRenderer.enabled = false;
            }

            _tempColors[validCount++] = cached.Color;

            // Yield каждые 10 кубов для предотвращения фризов
            if (i % 10 == 0)
            {
                yield return null;
            }
        }

        // Считаем количества по цветам через сортировку и линейный проход
        _uniqueColors.Clear();
        _colorCountsList.Clear();
        _colorToIndex.Clear();
        if (validCount > 0)
        {
            System.Array.Sort(_tempColors, 0, validCount, Color32Comparer.Instance);

            Color32 current = _tempColors[0];
            int cnt = 1;
            for (int i = 1; i < validCount; i++)
            {
                var c = _tempColors[i];
                if (c.r == current.r && c.g == current.g && c.b == current.b && c.a == current.a)
                {
                    cnt++;
                }
                else
                {
                    int idx = _uniqueColors.Count;
                    _uniqueColors.Add(current);
                    _colorCountsList.Add(cnt);
                    _colorToIndex[current] = idx;
                    current = c;
                    cnt = 1;
                }
            }

            int lastIdx = _uniqueColors.Count;
            _uniqueColors.Add(current);
            _colorCountsList.Add(cnt);
            _colorToIndex[current] = lastIdx;
        }

        // Обеспечиваем массивы точного размера в кэше
        _instancesByIndex.Clear();
        if (_instancesByIndex.Capacity < _uniqueColors.Count) _instancesByIndex.Capacity = _uniqueColors.Count;
        for (int i = 0; i < _uniqueColors.Count; i++)
        {
            var col = _uniqueColors[i];
            int cnt = _colorCountsList[i];
            if (!_instancesCache.TryGetValue(col, out var arr) || arr == null || arr.Length != cnt)
            {
                _instancesCache[col] = new CombineInstance[cnt];
                arr = _instancesCache[col];
            }
            else
            {
                arr = _instancesCache[col];
            }

            _instancesByIndex.Add(arr);
        }

        // Подготовим индексы записи (массив по индексам цветов)
        if (_writeIndicesArray.Length < _uniqueColors.Count)
            _writeIndicesArray = new int[_uniqueColors.Count];
        else
            System.Array.Clear(_writeIndicesArray, 0, _uniqueColors.Count);

        // Второй проход: заполняем массивы CombineInstance
        var worldToLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < _cachedComponents.Length; i++)
        {
            var cached = _cachedComponents[i];
            if (cached.MeshFilter == null || cached.MeshFilter.sharedMesh == null) continue;

            var ci = new CombineInstance
            {
                mesh = cached.MeshFilter.sharedMesh,
                transform = worldToLocal * cached.MeshFilter.transform.localToWorldMatrix
            };

            if (_colorToIndex.TryGetValue(cached.Color, out int colorIdx))
            {
                int write = _writeIndicesArray[colorIdx];
                _instancesByIndex[colorIdx][write] = ci;
                _writeIndicesArray[colorIdx] = write + 1;
            }

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
        _colorsToIterate.Clear();
        for (int i = 0; i < _uniqueColors.Count; i++) _colorsToIterate.Add(_uniqueColors[i]);
        for (int ci = 0; ci < _colorsToIterate.Count; ci++)
        {
            var color = _colorsToIterate[ci];
            var instances = _instancesByIndex[ci];

            var subMesh = GetPooledMesh();
            subMesh.CombineMeshes(instances, true, true);
            if (_entity != null && _entity.TryGetLocalBounds(out var fastBounds))
                subMesh.bounds = fastBounds;
            else
                subMesh.RecalculateBounds();

            var colorMeshObject = GetPooledGameObject($"CombinedMesh_{ci}");
            colorMeshObject.transform.SetParent(_combinedMeshObject.transform, false);

            var meshFilter = colorMeshObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = colorMeshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = subMesh;

            var meshRenderer = colorMeshObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = colorMeshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _sourceMaterial;

            _propertyBlock.SetColor("_BaseColor", (Color)color);
            meshRenderer.SetPropertyBlock(_propertyBlock);

            // Yield каждые 2 цвета для предотвращения фризов
            if (colorIndex % 2 == 0)
            {
                yield return null;
            }

            colorIndex++;
        }

        // Чистим устаревшие записи цветов из кэша
        var toRemove = new System.Collections.Generic.List<Color32>();
        foreach (var kv in _instancesCache)
        {
            if (!_colorToIndex.ContainsKey(kv.Key))
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