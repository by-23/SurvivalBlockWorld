using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(EntityMeshCombiner))]
public class Entity : MonoBehaviour
{
    public bool _StartCheck = true;

    private static int _nextEntityId = 1;
    public int EntityId { get; private set; }
    private bool _isLoading = false;

    private Rigidbody _rb;
    private int[,,] _cubesInfo;
    private Vector3 _cubesInfoStartPosition;
    private Cube[] _cubes;

    // Кэшированные размеры массива для оптимизации
    private int _cubesInfoSizeX;
    private int _cubesInfoSizeY;
    private int _cubesInfoSizeZ;

    // Кэш для быстрого поиска кубов по ID
    private Dictionary<int, int> _cubeIdToIndex;
    private bool _cacheValid = false;

    // Переиспользуемые массивы для избежания аллокаций
    private Cube[] _tempActiveCubes;
    private int[] _tempCubeIds;
    private bool[] _tempVisited;
    private int[] _tempQueue;
    private int[] _tempCurrentGroup;
    private int[][] _tempGroups;
    private int[] _tempGroupSizes;
    private int[] _tempGroupIndices;

    // Батчинг для множественных DetouchCube операций
    private List<Cube> _pendingDetouchCubes;
    private bool _detouchBatchPending = false;

    private EntityVehicleConnector _vehicleConnector;
    private EntityHookManager _hookManager;
    private EntityMeshCombiner _meshCombiner;
    private Coroutine _recombineCoroutine;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _vehicleConnector = GetComponent<EntityVehicleConnector>();
        _hookManager = GetComponent<EntityHookManager>();
        _meshCombiner = GetComponent<EntityMeshCombiner>();

        if (EntityId == 0)
            EntityId = _nextEntityId++;

        if (_StartCheck)
            StartSetup();
    }

    public void StartSetup()
    {
        UpdateMassAndCubes();
        _meshCombiner.CombineMeshes();
    }

    public void AddCube()
    {
        UpdateMassAndCubes();
        RequestDelayedCombine();
    }

    public void UpdateMassAndCubes()
    {
        if (_rb)
            _rb.mass = transform.childCount / 10;

        CollectCubes();
    }

    private void CollectCubes()
    {
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        _cubes = GetComponentsInChildren<Cube>();
        int childCount = _cubes.Length;

        if (childCount == 0)
        {
            _cubesInfo = new int[0, 0, 0];
            _cubesInfoSizeX = 0;
            _cubesInfoSizeY = 0;
            _cubesInfoSizeZ = 0;
            _cubesInfoStartPosition = Vector3.zero;
            _cubeIdToIndex = new Dictionary<int, int>();
            _cacheValid = true;
            return;
        }

        for (int i = 0; i < childCount; i++)
        {
            Transform child = _cubes[i].transform;
            min = Vector3.Min(min, child.localPosition);
            max = Vector3.Max(max, child.localPosition);
        }

        Vector3Int delta = Vector3Int.RoundToInt(max - min);
        _cubesInfoSizeX = delta.x + 1;
        _cubesInfoSizeY = delta.y + 1;
        _cubesInfoSizeZ = delta.z + 1;
        _cubesInfo = new int[_cubesInfoSizeX, _cubesInfoSizeY, _cubesInfoSizeZ];
        _cubesInfoStartPosition = min;

        // Создаем кэш для быстрого поиска кубов по ID
        _cubeIdToIndex = new Dictionary<int, int>(childCount);

        for (int i = 0; i < childCount; i++)
        {
            Vector3Int grid = GridPosition(_cubes[i].transform.localPosition);
            _cubesInfo[grid.x, grid.y, grid.z] = i + 1;
            if (_cubes[i] != null)
            {
                _cubes[i].Id = i + 1;
                _cubes[i].SetEntity(this);
                _cubeIdToIndex[i + 1] = i; // ID -> индекс в массиве _cubes
            }
        }

        _cacheValid = true;

        if (_hookManager)
            _hookManager.DetachAllHooks(_cubes);
    }

    private void RecalculateCubes()
    {
        // Дополнительная защита от вызова во время загрузки
        if (_isLoading || !_cacheValid)
        {
            return;
        }

        // Проверяем, что мы не в процессе загрузки сцены
        if (Time.timeSinceLevelLoad < 1f)
        {
            return;
        }

        // Быстрый подсчет активных кубов
        int activeCubeCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                activeCubeCount++;
        }

        if (activeCubeCount == 0)
        {
            Destroy(gameObject);
            return;
        }

        // Ранний выход для малых групп (если кубов мало, скорее всего они связаны)
        if (activeCubeCount <= 3)
        {
            return;
        }

        // Переиспользуем массивы для избежания аллокаций
        if (_tempActiveCubes == null || _tempActiveCubes.Length < activeCubeCount)
        {
            _tempActiveCubes = new Cube[activeCubeCount];
            _tempCubeIds = new int[activeCubeCount];
            _tempVisited = new bool[activeCubeCount];
            _tempQueue = new int[activeCubeCount];
            _tempCurrentGroup = new int[activeCubeCount];
        }

        // Заполняем массивы активными кубами
        int index = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
            {
                _tempCubeIds[index] = _cubes[i].Id;
                _tempActiveCubes[index] = _cubes[i];
                index++;
            }
        }

        // Очищаем массив посещенных
        for (int i = 0; i < activeCubeCount; i++)
        {
            _tempVisited[i] = false;
        }

        // Массив для хранения групп
        if (_tempGroups == null || _tempGroups.Length < activeCubeCount)
        {
            _tempGroups = new int[activeCubeCount][];
        }

        int groupCount = 0;

        for (int i = 0; i < activeCubeCount; i++)
        {
            if (_tempVisited[i])
                continue;

            // Начинаем новую группу
            int currentGroupSize = 0;
            int queueStart = 0;
            int queueEnd = 0;

            // Добавляем первый куб в очередь
            _tempQueue[queueEnd++] = i;
            _tempVisited[i] = true;

            // BFS для поиска связанных кубов
            while (queueStart < queueEnd)
            {
                int currentIndex = _tempQueue[queueStart++];
                _tempCurrentGroup[currentGroupSize++] = _tempCubeIds[currentIndex];

                Cube currentCube = _tempActiveCubes[currentIndex];
                if (currentCube == null) continue;

                // Кэшируем позицию куба
                Vector3Int gridPosition = GridPosition(currentCube.transform.localPosition);

                // Проверяем всех соседей с оптимизированным поиском
                CheckNeighborOptimized(gridPosition, 0, 1, 0); // up
                CheckNeighborOptimized(gridPosition, 0, -1, 0); // down
                CheckNeighborOptimized(gridPosition, -1, 0, 0); // left
                CheckNeighborOptimized(gridPosition, 1, 0, 0); // right
                CheckNeighborOptimized(gridPosition, 0, 0, 1); // forward
                CheckNeighborOptimized(gridPosition, 0, 0, -1); // back
            }

            // Сохраняем группу
            if (currentGroupSize > 0)
            {
                _tempGroups[groupCount] = new int[currentGroupSize];
                for (int j = 0; j < currentGroupSize; j++)
                {
                    _tempGroups[groupCount][j] = _tempCurrentGroup[j];
                }

                groupCount++;
            }

            void CheckNeighborOptimized(Vector3Int currentPos, int dx, int dy, int dz)
            {
                int x = currentPos.x + dx;
                int y = currentPos.y + dy;
                int z = currentPos.z + dz;

                if (x < 0 || y < 0 || z < 0 ||
                    x >= _cubesInfoSizeX || y >= _cubesInfoSizeY || z >= _cubesInfoSizeZ)
                    return;

                int neighborId = _cubesInfo[x, y, z];
                if (neighborId <= 0) return;

                // Используем кэш для быстрого поиска
                if (_cubeIdToIndex.ContainsKey(neighborId))
                {
                    // Находим индекс в массиве активных кубов
                    for (int j = 0; j < activeCubeCount; j++)
                    {
                        if (_tempCubeIds[j] == neighborId && !_tempVisited[j])
                        {
                            _tempVisited[j] = true;
                            _tempQueue[queueEnd++] = j;
                            break;
                        }
                    }
                }
            }
        }

        if (groupCount < 2)
        {
            return;
        }

        // Быстрая сортировка групп по размеру
        if (_tempGroupSizes == null || _tempGroupSizes.Length < groupCount)
        {
            _tempGroupSizes = new int[groupCount];
            _tempGroupIndices = new int[groupCount];
        }

        for (int i = 0; i < groupCount; i++)
        {
            _tempGroupSizes[i] = _tempGroups[i].Length;
            _tempGroupIndices[i] = i;
        }

        // Оптимизированная сортировка вставками
        for (int i = 1; i < groupCount; i++)
        {
            int keySize = _tempGroupSizes[i];
            int keyIndex = _tempGroupIndices[i];
            int j = i - 1;

            while (j >= 0 && _tempGroupSizes[j] < keySize)
            {
                _tempGroupSizes[j + 1] = _tempGroupSizes[j];
                _tempGroupIndices[j + 1] = _tempGroupIndices[j];
                j--;
            }

            _tempGroupSizes[j + 1] = keySize;
            _tempGroupIndices[j + 1] = keyIndex;
        }

        // Создаем новые entity для групп (кроме первой - самой большой)
        for (int i = 1; i < groupCount; i++)
        {
            int groupIndex = _tempGroupIndices[i];
            int[] group = _tempGroups[groupIndex];

            if (group.Length == 0) continue;

            GameObject newEntityObject = new GameObject("Entity");

            // Используем кэш для быстрого поиска первого куба
            if (_cubeIdToIndex.TryGetValue(group[0], out int firstCubeIndex))
            {
                Cube firstCubeInGroup = _cubes[firstCubeIndex];
                if (firstCubeInGroup != null)
                {
                    newEntityObject.transform.SetPositionAndRotation(firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation);
                    newEntityObject.transform.localScale = transform.localScale;

                    // Перемещаем кубы в новое entity с использованием кэша
                    for (int j = 0; j < group.Length; j++)
                    {
                        if (_cubeIdToIndex.TryGetValue(group[j], out int cubeIndex))
                        {
                            Cube cubeToMove = _cubes[cubeIndex];
                            if (cubeToMove != null)
                            {
                                cubeToMove.transform.parent = newEntityObject.transform;
                            }
                        }
                    }

                    Entity newEntity = newEntityObject.AddComponent<Entity>();
                    newEntity.StartSetup();

                    if (_vehicleConnector)
                    {
                        _vehicleConnector.OnEntityRecalculated();
                    }
                }
            }
        }

        CollectCubes();
    }


    public void DetouchCube(Cube cube)
    {
        if (cube == null) return;

        // Инициализируем список батчинга если нужно
        if (_pendingDetouchCubes == null)
        {
            _pendingDetouchCubes = new List<Cube>();
        }

        // Добавляем куб в батч
        _pendingDetouchCubes.Add(cube);

        // Если батч еще не обрабатывается, запускаем обработку
        if (!_detouchBatchPending)
        {
            StartCoroutine(ProcessDetouchBatch());
        }
    }

    private IEnumerator ProcessDetouchBatch()
    {
        _detouchBatchPending = true;

        // Ждем один кадр для сбора всех кубов в батч
        yield return null;

        if (_pendingDetouchCubes.Count == 0)
        {
            _detouchBatchPending = false;
            yield break;
        }

        // Обрабатываем все кубы в батче
        var cubesToDetouch = _pendingDetouchCubes.ToArray();
        _pendingDetouchCubes.Clear();

        // Показываем кубы перед детачем
        _meshCombiner.ShowCubes();

        // Обрабатываем каждый куб
        foreach (var cube in cubesToDetouch)
        {
            if (cube == null) continue;

            // Оптимизированное вычисление позиции без создания Vector3Int
            Vector3 localPos = cube.transform.localPosition;
            int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
            int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
            int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

            // Проверяем границы перед обращением к массиву
            if (x >= 0 && y >= 0 && z >= 0 &&
                x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
            {
                _cubesInfo[x, y, z] = 0;
            }

            // Обновляем массив кубов
            int cubeIndex = cube.Id - 1;
            if (cubeIndex >= 0 && cubeIndex < _cubes.Length)
            {
                _cubes[cubeIndex] = null;
            }

            // Обновляем кэш
            if (_cubeIdToIndex != null)
            {
                _cubeIdToIndex.Remove(cube.Id);
            }

            // Отсоединяем куб
            cube.transform.parent = null;

            // Оптимизированное создание Rigidbody
            var rb = cube.gameObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cube.gameObject.AddComponent<Rigidbody>();
                // Настраиваем Rigidbody для лучшей производительности
                rb.mass = 1f;
                rb.drag = 0.5f;
                rb.angularDrag = 0.5f;
            }

            // Отсоединяем хук если есть
            if (_hookManager)
                _hookManager.DetachHookFromCube(cube);

            // Запускаем анимацию уничтожения
            StartCoroutine(ScaleDownAndDestroyOptimized(cube.transform, 2f));
        }

        // Инвалидируем кэш один раз для всего батча
        _cacheValid = false;

        // Пересчитываем кубы один раз для всего батча
        RecalculateCubes();
        RequestDelayedCombine();

        // Отключаем isKinematic после удаления кубов игроком
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        _detouchBatchPending = false;
    }

    // Метод для принудительной обработки всех кубов в батче
    public void FlushDetouchBatch()
    {
        if (_pendingDetouchCubes != null && _pendingDetouchCubes.Count > 0)
        {
            StartCoroutine(ProcessDetouchBatchImmediate());
        }
    }

    private IEnumerator ProcessDetouchBatchImmediate()
    {
        if (_pendingDetouchCubes.Count == 0) yield break;

        var cubesToDetouch = _pendingDetouchCubes.ToArray();
        _pendingDetouchCubes.Clear();

        // Обрабатываем все кубы немедленно
        foreach (var cube in cubesToDetouch)
        {
            if (cube == null) continue;

            // Оптимизированное вычисление позиции
            Vector3 localPos = cube.transform.localPosition;
            int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
            int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
            int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

            if (x >= 0 && y >= 0 && z >= 0 &&
                x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
            {
                _cubesInfo[x, y, z] = 0;
            }

            int cubeIndex = cube.Id - 1;
            if (cubeIndex >= 0 && cubeIndex < _cubes.Length)
            {
                _cubes[cubeIndex] = null;
            }

            if (_cubeIdToIndex != null)
            {
                _cubeIdToIndex.Remove(cube.Id);
            }

            cube.transform.parent = null;

            var rb = cube.gameObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cube.gameObject.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.drag = 0.5f;
                rb.angularDrag = 0.5f;
            }

            if (_hookManager)
                _hookManager.DetachHookFromCube(cube);

            StartCoroutine(ScaleDownAndDestroyOptimized(cube.transform, 2f));
        }

        _cacheValid = false;
        RecalculateCubes();
        RequestDelayedCombine();

        // Отключаем isKinematic после удаления кубов игроком
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }
    }

    private void RequestDelayedCombine()
    {
        if (_recombineCoroutine != null)
        {
            StopCoroutine(_recombineCoroutine);
        }

        _recombineCoroutine = StartCoroutine(DelayedCombineMeshes());
    }

    private IEnumerator DelayedCombineMeshes()
    {
        yield return new WaitForSeconds(0.2f);
        _meshCombiner.CombineMeshes();
        _recombineCoroutine = null;
    }

    private IEnumerator ScaleDownAndDestroyOptimized(Transform target, float duration)
    {
        if (target == null) yield break;

        Vector3 initialScale = target.localScale;
        float timer = 0f;
        float invDuration = 1f / duration; // Предвычисляем для избежания деления

        while (timer < duration && target != null)
        {
            float progress = timer * invDuration;
            // Используем более быструю интерполяцию
            float scale = 1f - progress;
            target.localScale = initialScale * scale;
            timer += Time.deltaTime;
            yield return null;
        }

        if (target != null)
        {
            target.localScale = Vector3.zero;
            Destroy(target.gameObject);
        }
    }

    // Старый метод для обратной совместимости
    private IEnumerator ScaleDownAndDestroy(Transform target, float duration)
    {
        Vector3 initialScale = target.localScale;
        Vector3 targetScale = Vector3.zero;
        float timer = 0f;

        while (timer < duration)
        {
            target.localScale = Vector3.Lerp(initialScale, targetScale, timer / duration);
            timer += Time.deltaTime;
            yield return null;
        }

        target.localScale = targetScale;
        Destroy(target.gameObject);
    }

    private Vector3Int GridPosition(Vector3 localPosition)
    {
        // Оптимизированная версия без создания промежуточного Vector3
        float x = Mathf.RoundToInt(localPosition.x - _cubesInfoStartPosition.x);
        float y = Mathf.RoundToInt(localPosition.y - _cubesInfoStartPosition.y);
        float z = Mathf.RoundToInt(localPosition.z - _cubesInfoStartPosition.z);
        return new Vector3Int((int)x, (int)y, (int)z);
    }


    public CubeData[] GetSaveData()
    {
        if (_cubes == null || _cubes.Length == 0)
            return new CubeData[0];

        List<CubeData> cubeDataList = new List<CubeData>();
        Vector3 entityWorldPos = transform.position;

        foreach (var cube in _cubes)
        {
            if (cube != null)
            {
                cubeDataList.Add(cube.GetSaveData(entityWorldPos, EntityId));
            }
        }

        return cubeDataList.ToArray();
    }

    public async System.Threading.Tasks.Task LoadFromDataAsync(CubeData[] cubes, CubeSpawner spawner,
        bool deferredSetup = true)
    {
        if (cubes == null || cubes.Length == 0)
            return;

        _isLoading = true;
        _StartCheck = true; // Включаем StartCheck для загружаемых entity
        Vector3 entityPos = transform.position;
        float entityScale = transform.localScale.x; // Assuming uniform scaling

        foreach (var cubeData in cubes)
        {
            GameObject cubeObj = spawner.SpawnCube(cubeData, transform);
            if (cubeObj != null)
            {
                Vector3 localPos = (cubeData.Position - entityPos) / entityScale;
                cubeObj.transform.localPosition = localPos;
                // Поворот уже установлен в CubeSpawner, но нужно убедиться, что он локальный
                cubeObj.transform.localRotation = cubeData.Rotation;

                // Убеждаемся, что куб знает о своем entity
                Cube cubeComponent = cubeObj.GetComponent<Cube>();
                if (cubeComponent != null)
                {
                    cubeComponent.SetEntity(this);
                }
            }
        }

        if (!deferredSetup)
        {
            await System.Threading.Tasks.Task.Yield();
            StartSetup();

            if (_rb)
                _rb.isKinematic = true;
        }

        _isLoading = false;
    }

    public void FinalizeLoad()
    {
        _isLoading = true;
        _StartCheck = true; // Включаем StartCheck для загружаемых entity
        StartSetup();
        _isLoading = false;

        if (_rb)
            _rb.isKinematic = true;
    }

    /// <summary>
    /// Подсчитывает количество групп кубов, которые соприкасаются между собой, но не контактируют с другими группами
    /// </summary>
    /// <returns>Количество изолированных групп кубов</returns>
    public int CountConnectedGroups()
    {
        // Обновляем данные кубов перед подсчетом групп
        CollectCubes();

        if (_isLoading || _cubes == null)
        {
            return 0;
        }

        // Быстрый подсчет активных кубов
        int activeCubeCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                activeCubeCount++;
        }

        if (activeCubeCount == 0)
        {
            return 0;
        }

        // Переиспользуем массивы для избежания аллокаций
        if (_tempActiveCubes == null || _tempActiveCubes.Length < activeCubeCount)
        {
            _tempActiveCubes = new Cube[activeCubeCount];
            _tempCubeIds = new int[activeCubeCount];
            _tempVisited = new bool[activeCubeCount];
            _tempQueue = new int[activeCubeCount];
            _tempCurrentGroup = new int[activeCubeCount];
        }

        // Заполняем массивы активными кубами
        int index = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
            {
                _tempCubeIds[index] = _cubes[i].Id;
                _tempActiveCubes[index] = _cubes[i];
                index++;
            }
        }

        // Очищаем массив посещенных
        for (int i = 0; i < activeCubeCount; i++)
        {
            _tempVisited[i] = false;
        }

        int groupCount = 0;

        for (int i = 0; i < activeCubeCount; i++)
        {
            if (_tempVisited[i])
                continue;

            // Начинаем новую группу
            int queueStart = 0;
            int queueEnd = 0;

            // Добавляем первый куб в очередь
            _tempQueue[queueEnd++] = i;
            _tempVisited[i] = true;

            // BFS для поиска связанных кубов
            while (queueStart < queueEnd)
            {
                int currentIndex = _tempQueue[queueStart++];

                Cube currentCube = _tempActiveCubes[currentIndex];
                if (currentCube == null) continue;

                // Кэшируем позицию куба
                Vector3Int gridPosition = GridPosition(currentCube.transform.localPosition);

                // Проверяем всех соседей с оптимизированным поиском
                CheckNeighborOptimized(gridPosition, 0, 1, 0); // up
                CheckNeighborOptimized(gridPosition, 0, -1, 0); // down
                CheckNeighborOptimized(gridPosition, -1, 0, 0); // left
                CheckNeighborOptimized(gridPosition, 1, 0, 0); // right
                CheckNeighborOptimized(gridPosition, 0, 0, 1); // forward
                CheckNeighborOptimized(gridPosition, 0, 0, -1); // back
            }

            groupCount++;

            void CheckNeighborOptimized(Vector3Int currentPos, int dx, int dy, int dz)
            {
                int x = currentPos.x + dx;
                int y = currentPos.y + dy;
                int z = currentPos.z + dz;

                if (x < 0 || y < 0 || z < 0 ||
                    x >= _cubesInfoSizeX || y >= _cubesInfoSizeY || z >= _cubesInfoSizeZ)
                    return;

                int neighborId = _cubesInfo[x, y, z];
                if (neighborId <= 0) return;

                // Используем кэш для быстрого поиска
                if (_cubeIdToIndex.ContainsKey(neighborId))
                {
                    // Находим индекс в массиве активных кубов
                    for (int j = 0; j < activeCubeCount; j++)
                    {
                        if (_tempCubeIds[j] == neighborId && !_tempVisited[j])
                        {
                            _tempVisited[j] = true;
                            _tempQueue[queueEnd++] = j;
                            break;
                        }
                    }
                }
            }
        }

        return groupCount;
    }

    /// <summary>
    /// Автоматически разделяет кубы на отдельные Entity если обнаружено несколько групп
    /// </summary>
    /// <returns>Список созданных Entity (не включая текущий)</returns>
    public List<Entity> SplitIntoSeparateEntities()
    {
        // Обновляем данные кубов перед подсчетом групп
        CollectCubes();

        if (_isLoading || _cubes == null)
        {
            return new List<Entity>();
        }

        // Быстрый подсчет активных кубов
        int activeCubeCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                activeCubeCount++;
        }


        if (activeCubeCount == 0)
        {
            return new List<Entity>();
        }

        // Переиспользуем массивы для избежания аллокаций
        if (_tempActiveCubes == null || _tempActiveCubes.Length < activeCubeCount)
        {
            _tempActiveCubes = new Cube[activeCubeCount];
            _tempCubeIds = new int[activeCubeCount];
            _tempVisited = new bool[activeCubeCount];
            _tempQueue = new int[activeCubeCount];
            _tempCurrentGroup = new int[activeCubeCount];
        }

        // Заполняем массивы активными кубами
        int index = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
            {
                _tempCubeIds[index] = _cubes[i].Id;
                _tempActiveCubes[index] = _cubes[i];
                index++;
            }
        }

        // Очищаем массив посещенных
        for (int i = 0; i < activeCubeCount; i++)
        {
            _tempVisited[i] = false;
        }

        // Массив для хранения групп
        if (_tempGroups == null || _tempGroups.Length < activeCubeCount)
        {
            _tempGroups = new int[activeCubeCount][];
        }

        int groupCount = 0;

        for (int i = 0; i < activeCubeCount; i++)
        {
            if (_tempVisited[i])
                continue;

            // Начинаем новую группу
            int currentGroupSize = 0;
            int queueStart = 0;
            int queueEnd = 0;

            // Добавляем первый куб в очередь
            _tempQueue[queueEnd++] = i;
            _tempVisited[i] = true;

            // BFS для поиска связанных кубов
            while (queueStart < queueEnd)
            {
                int currentIndex = _tempQueue[queueStart++];
                _tempCurrentGroup[currentGroupSize++] = _tempCubeIds[currentIndex];

                Cube currentCube = _tempActiveCubes[currentIndex];
                if (currentCube == null) continue;

                // Кэшируем позицию куба
                Vector3Int gridPosition = GridPosition(currentCube.transform.localPosition);

                // Проверяем всех соседей с оптимизированным поиском
                CheckNeighborOptimized(gridPosition, 0, 1, 0); // up
                CheckNeighborOptimized(gridPosition, 0, -1, 0); // down
                CheckNeighborOptimized(gridPosition, -1, 0, 0); // left
                CheckNeighborOptimized(gridPosition, 1, 0, 0); // right
                CheckNeighborOptimized(gridPosition, 0, 0, 1); // forward
                CheckNeighborOptimized(gridPosition, 0, 0, -1); // back
            }

            // Сохраняем группу
            if (currentGroupSize > 0)
            {
                _tempGroups[groupCount] = new int[currentGroupSize];
                for (int j = 0; j < currentGroupSize; j++)
                {
                    _tempGroups[groupCount][j] = _tempCurrentGroup[j];
                }

                groupCount++;
            }

            void CheckNeighborOptimized(Vector3Int currentPos, int dx, int dy, int dz)
            {
                int x = currentPos.x + dx;
                int y = currentPos.y + dy;
                int z = currentPos.z + dz;

                if (x < 0 || y < 0 || z < 0 ||
                    x >= _cubesInfoSizeX || y >= _cubesInfoSizeY || z >= _cubesInfoSizeZ)
                    return;

                int neighborId = _cubesInfo[x, y, z];
                if (neighborId <= 0) return;

                // Используем кэш для быстрого поиска
                if (_cubeIdToIndex.ContainsKey(neighborId))
                {
                    // Находим индекс в массиве активных кубов
                    for (int j = 0; j < activeCubeCount; j++)
                    {
                        if (_tempCubeIds[j] == neighborId && !_tempVisited[j])
                        {
                            _tempVisited[j] = true;
                            _tempQueue[queueEnd++] = j;
                            break;
                        }
                    }
                }
            }
        }


        if (groupCount <= 1)
        {
            return new List<Entity>();
        }

        List<Entity> newEntities = new List<Entity>();

        // Быстрая сортировка групп по размеру
        if (_tempGroupSizes == null || _tempGroupSizes.Length < groupCount)
        {
            _tempGroupSizes = new int[groupCount];
            _tempGroupIndices = new int[groupCount];
        }

        for (int i = 0; i < groupCount; i++)
        {
            _tempGroupSizes[i] = _tempGroups[i].Length;
            _tempGroupIndices[i] = i;
        }

        // Оптимизированная сортировка вставками
        for (int i = 1; i < groupCount; i++)
        {
            int keySize = _tempGroupSizes[i];
            int keyIndex = _tempGroupIndices[i];
            int j = i - 1;

            while (j >= 0 && _tempGroupSizes[j] < keySize)
            {
                _tempGroupSizes[j + 1] = _tempGroupSizes[j];
                _tempGroupIndices[j + 1] = _tempGroupIndices[j];
                j--;
            }

            _tempGroupSizes[j + 1] = keySize;
            _tempGroupIndices[j + 1] = keyIndex;
        }

        // Создаем новые Entity для групп (кроме первой - самой большой)
        for (int i = 1; i < groupCount; i++)
        {
            int groupIndex = _tempGroupIndices[i];
            int[] group = _tempGroups[groupIndex];

            if (group.Length == 0) continue;

            GameObject newEntityObject = new GameObject("Entity");

            // Используем кэш для быстрого поиска первого куба
            if (_cubeIdToIndex.TryGetValue(group[0], out int firstCubeIndex))
            {
                Cube firstCubeInGroup = _cubes[firstCubeIndex];
                if (firstCubeInGroup != null)
                {
                    newEntityObject.transform.SetPositionAndRotation(firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation);
                    newEntityObject.transform.localScale = transform.localScale;

                    // Перемещаем кубы в новое entity с использованием кэша
                    for (int j = 0; j < group.Length; j++)
                    {
                        if (_cubeIdToIndex.TryGetValue(group[j], out int cubeIndex))
                        {
                            Cube cubeToMove = _cubes[cubeIndex];
                            if (cubeToMove != null)
                            {
                                cubeToMove.transform.parent = newEntityObject.transform;
                            }
                        }
                    }

                    Entity newEntity = newEntityObject.AddComponent<Entity>();
                    newEntity.StartSetup();

                    // Сбрасываем isKinematic для нового Entity
                    var newRb = newEntity.GetComponent<Rigidbody>();
                    if (newRb != null)
                    {
                        newRb.isKinematic = false;
                    }

                    newEntities.Add(newEntity);

                    if (_vehicleConnector)
                    {
                        _vehicleConnector.OnEntityRecalculated();
                    }
                }
            }
        }

        // Обновляем данные текущего Entity (остается только самая большая группа)
        CollectCubes();

        // Сбрасываем isKinematic для текущего Entity
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        return newEntities;
    }

    private void OnDestroy()
    {
        // Очищаем батч при уничтожении объекта
        if (_pendingDetouchCubes != null)
        {
            _pendingDetouchCubes.Clear();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _cubesInfo == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
        // Используем кэшированные размеры вместо GetLength()
        int xLength = _cubesInfoSizeX;
        int yLength = _cubesInfoSizeY;
        int zLength = _cubesInfoSizeZ;

        for (int x = 0; x < xLength; x++)
        {
            for (int y = 0; y < yLength; y++)
            {
                for (int z = 0; z < zLength; z++)
                {
                    Vector3 position = _cubesInfoStartPosition + new Vector3(x, y, z);
                    if (_cubesInfo[x, y, z] == 0)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(position, 0.1f);
                    }
                    else
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(position, 0.2f);
                    }
                }
            }
        }
    }
}
