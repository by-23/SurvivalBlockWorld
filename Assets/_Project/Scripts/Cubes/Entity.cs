using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(EntityMeshCombiner))]
public class Entity : MonoBehaviour
{
    public bool _StartCheck = true;

    private static int _nextEntityId = 1;
    [SerializeField, HideInInspector] private int _entityId;

    public int EntityId
    {
        get => _entityId;
        private set => _entityId = value;
    }

    private bool _isLoading = false;
    private bool _isGhost = false;

    public bool IsGhost => _isGhost;

    public void SetGhostMode(bool isGhost)
    {
        _isGhost = isGhost;
    }

    private Rigidbody _rb;
    private int[,,] _cubesInfo;
    private Vector3 _cubesInfoStartPosition;
    private Cube[] _cubes;
    public Cube[] Cubes => _cubes; // Публичный доступ к кэшу кубов

    private int _cubesInfoSizeX;
    private int _cubesInfoSizeY;
    private int _cubesInfoSizeZ;

    private Dictionary<int, int> _cubeIdToIndex;
    private bool _cacheValid = false;

    // Кэш для оптимизации CollectCubes - отслеживаем изменения структуры
    private int _lastChildCount = -1;

    // Переиспользуемый массив для кэширования позиций (избегаем аллокаций)
    private Vector3[] _cachedPositionsBuffer;

    // Используем пулы массивов для избежания выделений памяти при BFS
    private Cube[] _tempActiveCubes;
    private int[] _tempCubeIds;
    private bool[] _tempVisited;
    private int[] _tempQueue;
    private int[] _tempCurrentGroup;
    private int[][] _tempGroups;
    private int[] _tempGroupSizes;

    private int[] _tempGroupIndices;

    // Быстрый маппинг id куба -> индекс в активных массивах BFS
    private int[] _idToActiveIndex;

    // Кэш позиций кубов в сетке (убирает вызовы GridPosition в BFS)
    private Vector3Int[] _tempGridPositions;

    // Группируем отсоединение кубов: используем массивный буфер вместо List для минимизации аллокаций
    private Cube[] _pendingDetouchBuffer;
    private int _pendingDetouchCount;

    private bool _detouchBatchPending = false;

    // Рабочий буфер для батчей отделения кубов (переиспользуем, чтобы избежать GC)
    private Cube[] _detouchWorkBuffer;

    // Максимальное количество кубов, обрабатываемых за кадр при отсоединении
    // Предотвращает создание сотен Rigidbody одновременно и WaitForJobGroupID
    private const int MAX_CUBES_PER_FRAME_DETOUCH = 15;

    private EntityVehicleConnector _vehicleConnector;
    private EntityHookManager _hookManager;
    private EntityMeshCombiner _meshCombiner;

    private Coroutine _recombineCoroutine;

    // Флаг изменения структуры: влияет на необходимость пересборки меша
    private bool _structureDirty;

    // Локальные области влияния после удаления кубов
    private List<Vector3Int> _affectedCells;

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

        // Если Entity kinematic или нет Rigidbody, объединяем сразу
        // Иначе используем отложенное объединение с проверкой движения
        if (_rb == null || _rb.isKinematic)
        {
            _meshCombiner.CombineMeshes();
        }
        else
        {
            RequestDelayedCombine();
        }
    }

    public void AddCube()
    {
        UpdateMassAndCubes();
        _structureDirty = true; // структура изменилась
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
        // Быстрая проверка: если количество детей не изменилось и кэш валиден, пропускаем GetComponentsInChildren
        int currentChildCount = transform.childCount;
        if (_cacheValid && currentChildCount == _lastChildCount && _cubes != null && _cubes.Length == currentChildCount)
        {
            // Проверяем что кубы ещё валидны (быстрая проверка без GetComponentsInChildren)
            bool allValid = true;
            for (int i = 0; i < Mathf.Min(3, currentChildCount); i++) // проверяем только первые 3
            {
                if (_cubes[i] == null)
                {
                    allValid = false;
                    break;
                }
            }

            if (allValid)
            {
                // Структура не изменилась - используем существующий кэш
                // Но проверяем границы сетки (если кубы переместились)
                // Это дешёвая проверка, но позволяет обнаружить изменения позиций
                return;
            }
        }

        // Получаем кубы (только если действительно нужно)
        _cubes = GetComponentsInChildren<Cube>();
        int childCount = _cubes.Length;

        if (childCount == 0)
        {
            _cubesInfo = new int[0, 0, 0];
            _cubesInfoSizeX = 0;
            _cubesInfoSizeY = 0;
            _cubesInfoSizeZ = 0;
            _cubesInfoStartPosition = Vector3.zero;
            if (_cubeIdToIndex == null)
                _cubeIdToIndex = new Dictionary<int, int>();
            else
                _cubeIdToIndex.Clear();
            _cacheValid = true;
            _lastChildCount = 0;
            return;
        }

        // Один проход: находим границы и заполняем сетку одновременно
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        // Переиспользуем Dictionary с pre-allocated capacity
        if (_cubeIdToIndex == null)
            _cubeIdToIndex = new Dictionary<int, int>(childCount);
        else
            _cubeIdToIndex.Clear();

        // Переиспользуем массив позиций (избегаем аллокаций)
        if (_cachedPositionsBuffer == null || _cachedPositionsBuffer.Length < childCount)
            _cachedPositionsBuffer = new Vector3[childCount];

        int validCount = 0;

        for (int i = 0; i < childCount; i++)
        {
            var cube = _cubes[i];
            if (cube == null) continue;

            var pos = cube.transform.localPosition;
            _cachedPositionsBuffer[validCount] = pos;

            // Обновляем границы (прямые сравнения быстрее чем Vector3.Min/Max)
            if (pos.x < min.x) min.x = pos.x;
            if (pos.y < min.y) min.y = pos.y;
            if (pos.z < min.z) min.z = pos.z;
            if (pos.x > max.x) max.x = pos.x;
            if (pos.y > max.y) max.y = pos.y;
            if (pos.z > max.z) max.z = pos.z;

            validCount++;
        }

        if (validCount == 0)
        {
            _cubesInfo = new int[0, 0, 0];
            _cubesInfoSizeX = 0;
            _cubesInfoSizeY = 0;
            _cubesInfoSizeZ = 0;
            _cubesInfoStartPosition = Vector3.zero;
            _cacheValid = true;
            _lastChildCount = 0;
            return;
        }

        // Вычисляем размеры сетки
        Vector3Int delta = Vector3Int.RoundToInt(max - min);
        int newSizeX = delta.x + 1;
        int newSizeY = delta.y + 1;
        int newSizeZ = delta.z + 1;

        // Переиспользуем массив если размер не изменился
        if (_cubesInfo == null || _cubesInfoSizeX != newSizeX || _cubesInfoSizeY != newSizeY ||
            _cubesInfoSizeZ != newSizeZ)
        {
            _cubesInfo = new int[newSizeX, newSizeY, newSizeZ];
        }
        else
        {
            // Очищаем существующий массив (быстрее чем пересоздание)
            System.Array.Clear(_cubesInfo, 0, _cubesInfo.Length);
        }

        _cubesInfoSizeX = newSizeX;
        _cubesInfoSizeY = newSizeY;
        _cubesInfoSizeZ = newSizeZ;
        _cubesInfoStartPosition = min;

        // Второй проход: заполняем сетку (используем кэшированные позиции)
        int cubeIndex = 0;
        for (int i = 0; i < childCount; i++)
        {
            var cube = _cubes[i];
            if (cube == null) continue;

            Vector3Int grid = GridPosition(_cachedPositionsBuffer[cubeIndex]);
            int id = cubeIndex + 1;

            _cubesInfo[grid.x, grid.y, grid.z] = id;
            cube.Id = id;
            cube.SetEntity(this);
            _cubeIdToIndex[id] = i;

            cubeIndex++;
        }

        _cacheValid = true;
        _lastChildCount = childCount;

        // Инвалидируем кэш комбинирования мешей после любого пересчёта структуры
        if (_meshCombiner)
            _meshCombiner.InvalidateCubeCache();
    }

    private void RecalculateCubes()
    {
        // Предотвращаем пересчет во время загрузки - может вызвать баги при асинхронном спавне
        if (_isLoading || !_cacheValid)
        {
            return;
        }

        // Игнорируем пересчет сразу после загрузки сцены - может быть остаточная активность
        if (Time.timeSinceLevelLoad < 1f)
        {
            return;
        }

        int activeCubeCount = FillActiveCubesArrays();

        if (activeCubeCount == 0)
        {
            Destroy(gameObject);
            return;
        }

        // Маленькие группы скорее всего цельные - не трогаем их
        if (activeCubeCount <= 3)
        {
            return;
        }

        int groupCount = FindConnectedGroups(activeCubeCount, out int[][] groups);

        if (groupCount < 2)
        {
            return;
        }

        SortGroupsBySize(groupCount, groups, out int[] groupIndices);

        // Создаем отдельные Entity для отколовшихся групп кубов
        for (int i = 1; i < groupCount; i++)
        {
            int groupIndex = groupIndices[i];
            int[] group = groups[groupIndex];

            if (group.Length == 0) continue;

            if (_cubeIdToIndex.TryGetValue(group[0], out int firstCubeIndex))
            {
                Cube firstCubeInGroup = _cubes[firstCubeIndex];
                if (firstCubeInGroup != null)
                {
                    // Создаем Entity через фабрику
                    Entity newEntity = EntityFactory.CreateEntity(
                        firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation,
                        transform.localScale,
                        isKinematic: true,
                        entityName: "Entity"
                    );

                    // Перемещаем кубы в новое Entity
                    for (int j = 0; j < group.Length; j++)
                    {
                        if (_cubeIdToIndex.TryGetValue(group[j], out int cubeIndex))
                        {
                            Cube cubeToMove = _cubes[cubeIndex];
                            if (cubeToMove != null)
                            {
                                cubeToMove.transform.SetParent(newEntity.transform, true);
                            }
                        }
                    }

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

        if (_pendingDetouchBuffer == null)
        {
            _pendingDetouchBuffer = new Cube[16];
            _pendingDetouchCount = 0;
        }

        if (_pendingDetouchCount == _pendingDetouchBuffer.Length)
        {
            int newCapacity = _pendingDetouchBuffer.Length << 1;
            var newArr = new Cube[newCapacity];
            System.Array.Copy(_pendingDetouchBuffer, 0, newArr, 0, _pendingDetouchCount);
            _pendingDetouchBuffer = newArr;
        }

        _pendingDetouchBuffer[_pendingDetouchCount++] = cube;

        if (!_detouchBatchPending)
        {
            StartCoroutine(ProcessDetouchBatch());
        }

        _structureDirty = true;
    }

    private IEnumerator ProcessDetouchBatch()
    {
        _detouchBatchPending = true;

        // Собираем несколько удалений за кадр - экономит вызовы дорогих операций
        yield return null;

        if (_pendingDetouchCount == 0)
        {
            _detouchBatchPending = false;
            yield break;
        }

        // Копируем в переиспользуемый буфер, чтобы не аллоцировать каждый раз
        if (_detouchWorkBuffer == null || _detouchWorkBuffer.Length < _pendingDetouchCount)
        {
            int newCapacity = _pendingDetouchCount < 16 ? 16 : _pendingDetouchCount;
            _detouchWorkBuffer = new Cube[newCapacity];
        }

        int workCount = _pendingDetouchCount;
        System.Array.Copy(_pendingDetouchBuffer, 0, _detouchWorkBuffer, 0, workCount);
        _pendingDetouchCount = 0;

        // Разделяем меши перед удалением кубов
        _meshCombiner.ShowCubes();

        // Оптимизация: обрабатываем кубы батчами для предотвращения WaitForJobGroupID
        // Создание множества Rigidbody одновременно перегружает Unity Physics Job System
        int processed = 0;
        while (processed < workCount)
        {
            int batchSize = Mathf.Min(MAX_CUBES_PER_FRAME_DETOUCH, workCount - processed);

            // Обрабатываем батч кубов
            for (int i = 0; i < batchSize; i++)
            {
                Cube cube = _detouchWorkBuffer[processed + i];
                if (cube == null) continue;

                Vector3 localPos = cube.transform.localPosition;
                int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
                int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
                int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

                if (x >= 0 && y >= 0 && z >= 0 &&
                    x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
                {
                    _cubesInfo[x, y, z] = 0;
                }

                // Копим соседние клетки как область локальной проверки
                if (_affectedCells == null) _affectedCells = new List<Vector3Int>(32);
                for (int n = 0; n < NeighborOffsets.Length; n++)
                {
                    Vector3Int p = new Vector3Int(x + NeighborOffsets[n].x, y + NeighborOffsets[n].y,
                        z + NeighborOffsets[n].z);
                    if (p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < _cubesInfoSizeX && p.y < _cubesInfoSizeY &&
                        p.z < _cubesInfoSizeZ)
                        _affectedCells.Add(p);
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

                // Создаем Rigidbody для куба (батчированное создание предотвращает WaitForJobGroupID)
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

            processed += batchSize;

            // Ждем FixedUpdate перед обработкой следующего батча
            // Это дает Unity Physics время обработать созданные Rigidbody
            if (processed < workCount)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        _cacheValid = false;

        // Локальная рекалькуляция только вокруг затронутых кубов
        RecalculateAroundAffected();
        RequestDelayedCombine();

        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        _detouchBatchPending = false;
    }

    public void FlushDetouchBatch()
    {
        if (_pendingDetouchBuffer != null && _pendingDetouchCount > 0)
        {
            StartCoroutine(ProcessDetouchBatchImmediate());
        }
    }

    private IEnumerator ProcessDetouchBatchImmediate()
    {
        if (_pendingDetouchCount == 0) yield break;

        // Копируем в переиспользуемый буфер, чтобы не аллоцировать каждый раз
        if (_detouchWorkBuffer == null || _detouchWorkBuffer.Length < _pendingDetouchCount)
        {
            int newCapacity = _pendingDetouchCount < 16 ? 16 : _pendingDetouchCount;
            _detouchWorkBuffer = new Cube[newCapacity];
        }

        int workCount = _pendingDetouchCount;
        System.Array.Copy(_pendingDetouchBuffer, 0, _detouchWorkBuffer, 0, workCount);
        _pendingDetouchCount = 0;

        // Оптимизация: обрабатываем кубы батчами для предотвращения WaitForJobGroupID
        int processed = 0;
        while (processed < workCount)
        {
            int batchSize = Mathf.Min(MAX_CUBES_PER_FRAME_DETOUCH, workCount - processed);

            // Обрабатываем батч кубов
            for (int i = 0; i < batchSize; i++)
            {
                Cube cube = _detouchWorkBuffer[processed + i];
                if (cube == null) continue;

                Vector3 localPos = cube.transform.localPosition;
                int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
                int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
                int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

                if (x >= 0 && y >= 0 && z >= 0 &&
                    x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
                {
                    _cubesInfo[x, y, z] = 0;
                }

                // Копим соседние клетки как область локальной проверки
                if (_affectedCells == null) _affectedCells = new List<Vector3Int>(32);
                for (int n = 0; n < NeighborOffsets.Length; n++)
                {
                    Vector3Int p = new Vector3Int(x + NeighborOffsets[n].x, y + NeighborOffsets[n].y,
                        z + NeighborOffsets[n].z);
                    if (p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < _cubesInfoSizeX && p.y < _cubesInfoSizeY &&
                        p.z < _cubesInfoSizeZ)
                        _affectedCells.Add(p);
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

                // Создаем Rigidbody для куба (батчированное создание предотвращает WaitForJobGroupID)
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

            processed += batchSize;

            // Ждем FixedUpdate перед обработкой следующего батча
            if (processed < workCount)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        _cacheValid = false;
        RecalculateAroundAffected();
        RequestDelayedCombine();

        if (_rb != null)
        {
            _rb.isKinematic = false;
        }
    }

    private float _lastCombineTime = -1f;
    private const float MIN_COMBINE_INTERVAL = 0.3f; // Минимальный интервал между объединениями мешей

    private void RequestDelayedCombine()
    {
        // Дебаунсинг: не запускаем новую корутину если уже идет объединение или недавно было объединение
        float timeSinceLastCombine = Time.time - _lastCombineTime;
        if (_recombineCoroutine != null || timeSinceLastCombine < MIN_COMBINE_INTERVAL)
        {
            // Останавливаем старую корутину если она еще идет (перезапускаем таймер)
            if (_recombineCoroutine != null)
            {
                StopCoroutine(_recombineCoroutine);
                _recombineCoroutine = null;
            }

            // Не запускаем новую корутину сразу - ждем минимум интервал
            if (timeSinceLastCombine < MIN_COMBINE_INTERVAL)
            {
                return;
            }
        }

        _recombineCoroutine = StartCoroutine(DelayedCombineMeshes());
    }

    private IEnumerator DelayedCombineMeshes()
    {
        // Минимальная задержка перед проверкой движения объекта
        int cubeCount = transform.childCount;
        float initialDelay = cubeCount > 30 ? 0.2f : 0.1f; // Небольшая задержка для стабилизации
        yield return new WaitForSeconds(initialDelay);

        // Проверяем, не был ли Entity уничтожен во время ожидания
        if (this == null || gameObject == null)
        {
            _recombineCoroutine = null;
            yield break;
        }

        // Проверяем, не объединены ли меши уже (если другой процесс уже это сделал)
        if (_meshCombiner != null && _meshCombiner.IsCombined)
        {
            _recombineCoroutine = null;
            yield break;
        }

        // Ждем, пока объект не перестанет двигаться
        float maxWaitTime = cubeCount > 30 ? 2.5f : 1.5f; // Ожидание остановки объекта
        float waitStartTime = Time.time;
        float velocityThreshold = 0.01f;
        float angularVelocityThreshold = 0.01f;

        // Проверяем движение каждый кадр, пока объект не остановится
        while (Time.time - waitStartTime < maxWaitTime)
        {
            if (this == null || gameObject == null)
            {
                _recombineCoroutine = null;
                yield break;
            }

            if (_rb != null && !_rb.isKinematic)
            {
                float velocityMagnitude = _rb.velocity.magnitude;
                float angularVelocityMagnitude = _rb.angularVelocity.magnitude;

                if (velocityMagnitude <= velocityThreshold &&
                    angularVelocityMagnitude <= angularVelocityThreshold)
                {
                    break;
                }
            }
            else if (_rb == null || _rb.isKinematic)
            {
                break;
            }

            yield return null;
        }

        // Финальная проверка перед объединением
        if (this == null || gameObject == null || _meshCombiner == null)
        {
            _recombineCoroutine = null;
            yield break;
        }

        // Объединяем меши и записываем время для дебаунсинга
        _meshCombiner.CombineMeshes();
        _lastCombineTime = Time.time;
        _recombineCoroutine = null;
    }

    private IEnumerator ScaleDownAndDestroyOptimized(Transform target, float duration)
    {
        if (target == null) yield break;

        Vector3 initialScale = target.localScale;
        float timer = 0f;
        float invDuration = 1f / duration;

        while (timer < duration && target != null)
        {
            float progress = timer * invDuration;
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

    private Vector3Int GridPosition(Vector3 localPosition)
    {
        float x = Mathf.RoundToInt(localPosition.x - _cubesInfoStartPosition.x);
        float y = Mathf.RoundToInt(localPosition.y - _cubesInfoStartPosition.y);
        float z = Mathf.RoundToInt(localPosition.z - _cubesInfoStartPosition.z);
        return new Vector3Int((int)x, (int)y, (int)z);
    }

    // Локальная проверка связности вокруг удалённых кубов
    private void RecalculateAroundAffected()
    {
        if (_affectedCells == null || _affectedCells.Count == 0)
        {
            RecalculateCubes();
            return;
        }

        // Собираем кандидатов: кубы-соседи вокруг удалённых
        var candidateIds = new HashSet<int>();
        for (int i = 0; i < _affectedCells.Count; i++)
        {
            Vector3Int c = _affectedCells[i];
            int idHere = _cubesInfo[c.x, c.y, c.z];
            if (idHere > 0) candidateIds.Add(idHere);
            for (int n = 0; n < NeighborOffsets.Length; n++)
            {
                int nx = c.x + NeighborOffsets[n].x;
                int ny = c.y + NeighborOffsets[n].y;
                int nz = c.z + NeighborOffsets[n].z;
                if (nx >= 0 && ny >= 0 && nz >= 0 && nx < _cubesInfoSizeX && ny < _cubesInfoSizeY &&
                    nz < _cubesInfoSizeZ)
                {
                    int nid = _cubesInfo[nx, ny, nz];
                    if (nid > 0) candidateIds.Add(nid);
                }
            }
        }

        _affectedCells.Clear();

        if (candidateIds.Count == 0)
            return;

        // BFS по кандидатам; если компонент касается кубов вне кандидатов — считаем его прикреплённым
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        var groupIds = new List<int>(32);

        foreach (int startId in candidateIds)
        {
            if (visited.Contains(startId)) continue;

            bool touchesOutside = false;
            groupIds.Clear();
            queue.Clear();
            queue.Enqueue(startId);
            visited.Add(startId);

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                groupIds.Add(id);

                if (!_cubeIdToIndex.TryGetValue(id, out int cubeIndex))
                    continue;
                Cube cube = (cubeIndex >= 0 && cubeIndex < _cubes.Length) ? _cubes[cubeIndex] : null;
                if (cube == null) continue;

                Vector3Int gp = GridPosition(cube.transform.localPosition);
                for (int n = 0; n < NeighborOffsets.Length; n++)
                {
                    int nx = gp.x + NeighborOffsets[n].x;
                    int ny = gp.y + NeighborOffsets[n].y;
                    int nz = gp.z + NeighborOffsets[n].z;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= _cubesInfoSizeX || ny >= _cubesInfoSizeY ||
                        nz >= _cubesInfoSizeZ)
                        continue;

                    int nid = _cubesInfo[nx, ny, nz];
                    if (nid <= 0) continue;

                    if (candidateIds.Contains(nid))
                    {
                        if (!visited.Contains(nid))
                        {
                            visited.Add(nid);
                            queue.Enqueue(nid);
                        }
                    }
                    else
                    {
                        // Нашли связь с внешними кубами — компонент должен остаться
                        touchesOutside = true;
                    }
                }
            }

            if (!touchesOutside && groupIds.Count > 0)
            {
                // Отделяем изолированную группу
                int firstId = groupIds[0];
                if (_cubeIdToIndex.TryGetValue(firstId, out int firstIndex))
                {
                    Cube firstCube = (firstIndex >= 0 && firstIndex < _cubes.Length) ? _cubes[firstIndex] : null;
                    if (firstCube != null)
                    {
                        Entity newEntity = EntityFactory.CreateEntity(
                            firstCube.transform.position,
                            firstCube.transform.rotation,
                            transform.localScale,
                            isKinematic: false,
                            entityName: "Entity"
                        );

                        for (int j = 0; j < groupIds.Count; j++)
                        {
                            int gid = groupIds[j];
                            if (_cubeIdToIndex.TryGetValue(gid, out int gi))
                            {
                                Cube cc = (gi >= 0 && gi < _cubes.Length) ? _cubes[gi] : null;
                                if (cc != null)
                                {
                                    cc.transform.SetParent(newEntity.transform, true);
                                }
                            }
                        }

                        newEntity.StartSetup();
                        if (_vehicleConnector)
                            _vehicleConnector.OnEntityRecalculated();
                    }
                }
            }
        }

        // Обновляем данные текущего Entity
        CollectCubes();
    }

    // Статические смещения для проверки соседей (6 направлений)
    private static readonly Vector3Int[] NeighborOffsets = new Vector3Int[]
    {
        new Vector3Int(0, 1, 0), // up
        new Vector3Int(0, -1, 0), // down
        new Vector3Int(-1, 0, 0), // left
        new Vector3Int(1, 0, 0), // right
        new Vector3Int(0, 0, 1), // forward
        new Vector3Int(0, 0, -1) // back
    };

    /// <summary>
    /// Заполняет временные массивы активными кубами
    /// </summary>
    private int FillActiveCubesArrays()
    {
        int activeCubeCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                activeCubeCount++;
        }

        if (activeCubeCount == 0) return 0;

        // Подготовка массивов
        if (_tempActiveCubes == null || _tempActiveCubes.Length < activeCubeCount)
        {
            _tempActiveCubes = new Cube[activeCubeCount];
            _tempCubeIds = new int[activeCubeCount];
            _tempVisited = new bool[activeCubeCount];
            _tempQueue = new int[activeCubeCount];
            _tempCurrentGroup = new int[activeCubeCount];
            _tempGridPositions = new Vector3Int[activeCubeCount];
        }

        // Подготовка маппинга id -> индекс активного куба
        int idMapSize = _cubes.Length + 1; // id назначаются как i+1
        if (_idToActiveIndex == null || _idToActiveIndex.Length < idMapSize)
        {
            _idToActiveIndex = new int[idMapSize];
        }

        for (int i = 0; i < _idToActiveIndex.Length; i++) _idToActiveIndex[i] = -1;

        // Заполняем массивы активными кубами и маппинг (кэшируем позиции сразу)
        int index = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
            {
                int id = _cubes[i].Id;
                _tempCubeIds[index] = id;
                _tempActiveCubes[index] = _cubes[i];
                // Кэшируем позицию в сетке (убирает вызовы GridPosition в BFS)
                _tempGridPositions[index] = GridPosition(_cubes[i].transform.localPosition);
                if (id >= 0 && id < _idToActiveIndex.Length)
                    _idToActiveIndex[id] = index;
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

        return activeCubeCount;
    }

    /// <summary>
    /// Проверяет соседа куба в указанном направлении (использует кэшированные позиции)
    /// </summary>
    private void CheckNeighborOptimized(int x, int y, int z, ref int queueEnd)
    {
        if (x < 0 || y < 0 || z < 0 ||
            x >= _cubesInfoSizeX || y >= _cubesInfoSizeY || z >= _cubesInfoSizeZ)
            return;

        int neighborId = _cubesInfo[x, y, z];
        if (neighborId <= 0) return;

        // O(1): берем индекс активного куба напрямую по id
        if (_idToActiveIndex != null && neighborId < _idToActiveIndex.Length)
        {
            int activeIdx = _idToActiveIndex[neighborId];
            if (activeIdx >= 0 && !_tempVisited[activeIdx])
            {
                _tempVisited[activeIdx] = true;
                _tempQueue[queueEnd++] = activeIdx;
            }
        }
    }

    /// <summary>
    /// Находит все связанные группы кубов используя BFS
    /// </summary>
    private int FindConnectedGroups(int activeCubeCount, out int[][] groups)
    {
        groups = null;
        if (activeCubeCount == 0) return 0;

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

            // BFS для поиска связанных кубов (используем кэшированные позиции)
            while (queueStart < queueEnd)
            {
                int currentIndex = _tempQueue[queueStart++];
                _tempCurrentGroup[currentGroupSize++] = _tempCubeIds[currentIndex];

                // Используем кэшированную позицию (убирает вызов GridPosition)
                Vector3Int gridPos = _tempGridPositions[currentIndex];

                // Проверяем всех соседей (прямой доступ к массиву вместо foreach)
                CheckNeighborOptimized(gridPos.x, gridPos.y + 1, gridPos.z, ref queueEnd); // up
                CheckNeighborOptimized(gridPos.x, gridPos.y - 1, gridPos.z, ref queueEnd); // down
                CheckNeighborOptimized(gridPos.x - 1, gridPos.y, gridPos.z, ref queueEnd); // left
                CheckNeighborOptimized(gridPos.x + 1, gridPos.y, gridPos.z, ref queueEnd); // right
                CheckNeighborOptimized(gridPos.x, gridPos.y, gridPos.z + 1, ref queueEnd); // forward
                CheckNeighborOptimized(gridPos.x, gridPos.y, gridPos.z - 1, ref queueEnd); // back
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
        }

        groups = _tempGroups;
        return groupCount;
    }

    /// <summary>
    /// Сортирует группы по размеру (от большей к меньшей)
    /// </summary>
    private void SortGroupsBySize(int groupCount, int[][] groups, out int[] groupIndices)
    {
        if (_tempGroupSizes == null || _tempGroupSizes.Length < groupCount)
        {
            _tempGroupSizes = new int[groupCount];
            _tempGroupIndices = new int[groupCount];
        }

        for (int i = 0; i < groupCount; i++)
        {
            _tempGroupSizes[i] = groups[i].Length;
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

        groupIndices = _tempGroupIndices;
    }


    public CubeData[] GetSaveData()
    {
        if (_cubes == null || _cubes.Length == 0)
            return new CubeData[0];

        // Считаем количество валидных кубов, затем заполняем массив точного размера
        int validCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null) validCount++;
        }

        if (validCount == 0) return new CubeData[0];

        var result = new CubeData[validCount];
        Vector3 entityWorldPos = transform.position;
        int write = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            var cube = _cubes[i];
            if (cube != null)
            {
                result[write++] = cube.GetSaveData(entityWorldPos, EntityId);
            }
        }

        return result;
    }

    public async System.Threading.Tasks.Task LoadFromDataAsync(CubeData[] cubes, CubeSpawner spawner,
        bool deferredSetup = true, Vector3? savedEntityPosition = null)
    {
        if (cubes == null || cubes.Length == 0)
            return;

        _isLoading = true;
        _StartCheck = true;
        // Используем сохранённую позицию entity, если указана, иначе текущую
        Vector3 entityPos = savedEntityPosition ?? transform.position;
        float entityScale = transform.localScale.x;

        foreach (var cubeData in cubes)
        {
            GameObject cubeObj = spawner.SpawnCube(cubeData, transform);
            if (cubeObj != null)
            {
                Vector3 localPos = (cubeData.Position - entityPos) / entityScale;
                cubeObj.transform.localPosition = localPos;
                cubeObj.transform.localRotation = cubeData.Rotation;

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
        _StartCheck = true;
        StartSetup();
        _isLoading = false;

        if (_rb)
            _rb.isKinematic = true;
    }

    public int CountConnectedGroups()
    {
        CollectCubes();

        if (_isLoading || _cubes == null)
        {
            return 0;
        }

        int activeCubeCount = FillActiveCubesArrays();
        if (activeCubeCount == 0)
        {
            return 0;
        }

        return FindConnectedGroups(activeCubeCount, out _);
    }

    public Entity[] SplitIntoSeparateEntities()
    {
        if (!_cacheValid)
        {
            CollectCubes();
        }

        if (_isLoading || _cubes == null)
        {
            return System.Array.Empty<Entity>();
        }

        int activeCubeCount = FillActiveCubesArrays();
        if (activeCubeCount == 0)
        {
            return System.Array.Empty<Entity>();
        }

        int groupCount = FindConnectedGroups(activeCubeCount, out int[][] groups);

        if (groupCount <= 1)
        {
            return System.Array.Empty<Entity>();
        }

        // Максимально возможное число новых Entity: groupCount - 1 (кроме самой большой группы)
        Entity[] newEntities = new Entity[groupCount - 1];
        int writeIndex = 0;

        SortGroupsBySize(groupCount, groups, out int[] groupIndices);

        // Создаем новые Entity для групп (кроме первой - самой большой)
        for (int i = 1; i < groupCount; i++)
        {
            int groupIndex = groupIndices[i];
            int[] group = groups[groupIndex];

            if (group.Length == 0) continue;

            // Используем кэш для быстрого поиска первого куба
            if (_cubeIdToIndex.TryGetValue(group[0], out int firstCubeIndex))
            {
                Cube firstCubeInGroup = _cubes[firstCubeIndex];
                if (firstCubeInGroup != null)
                {
                    // Создаем Entity через фабрику с isKinematic = false для разделенных Entity
                    Entity newEntity = EntityFactory.CreateEntity(
                        firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation,
                        transform.localScale,
                        isKinematic: false,
                        entityName: "Entity"
                    );

                    // Перемещаем кубы в новое entity с использованием кэша
                    for (int j = 0; j < group.Length; j++)
                    {
                        if (_cubeIdToIndex.TryGetValue(group[j], out int cubeIndex))
                        {
                            Cube cubeToMove = _cubes[cubeIndex];
                            if (cubeToMove != null)
                            {
                                cubeToMove.transform.SetParent(newEntity.transform, true);
                            }
                        }
                    }

                    newEntity.StartSetup();
                    newEntities[writeIndex++] = newEntity;

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

        if (writeIndex == newEntities.Length)
            return newEntities;

        var compact = new Entity[writeIndex];
        System.Array.Copy(newEntities, 0, compact, 0, writeIndex);
        return compact;
    }

    // Вызывается когда к entity прикрепляется хук - включаем физику
    public void EnablePhysics()
    {
        // Не включаем физику для ghost entity
        if (_isGhost)
            return;

        if (_rb != null)
        {
            _rb.isKinematic = false;
        }
    }

    // Отключаем физику для статичных объектов чтобы экономить производительность
    public void DisablePhysics()
    {
        if (_rb != null)
        {
            _rb.isKinematic = true;
        }
    }

    // Гарантирует валидность кэша кубов без лишней работы
    public void EnsureCacheValid()
    {
        if (!_cacheValid)
        {
            CollectCubes();
        }
    }

    // Пометить структуру изменённой
    public void MarkStructureDirty()
    {
        _structureDirty = true;
    }

    // Сбросить флаг грязности (после успешного Combine)
    public void ClearStructureDirty()
    {
        _structureDirty = false;
    }

    public bool IsStructureDirty => _structureDirty;

    // Быстрые локальные границы на основе карты кубов
    public bool TryGetLocalBounds(out Bounds bounds)
    {
        if (_cubesInfo == null || _cubesInfoSizeX == 0 || _cubesInfoSizeY == 0 || _cubesInfoSizeZ == 0)
        {
            bounds = default;
            return false;
        }

        Vector3 size = new Vector3(_cubesInfoSizeX, _cubesInfoSizeY, _cubesInfoSizeZ);
        Vector3 center = _cubesInfoStartPosition + (size - Vector3.one) * 0.5f;
        bounds = new Bounds(center, size);
        return true;
    }

    // Быстро получить куб по id
    public bool TryGetCubeById(int cubeId, out Cube cube)
    {
        cube = null;
        if (cubeId <= 0 || _cubeIdToIndex == null) return false;
        if (_cubeIdToIndex.TryGetValue(cubeId, out int idx))
        {
            if (idx >= 0 && _cubes != null && idx < _cubes.Length)
            {
                cube = _cubes[idx];
                return cube != null;
            }
        }

        return false;
    }

    // Удалить куб по id (точечно обновляя карты) и добавить его в батч отсоединения
    public bool RemoveCubeById(int cubeId)
    {
        if (!TryGetCubeById(cubeId, out Cube cube) || cube == null)
            return false;

        // Кладём куб в существующий батч-канал
        DetouchCube(cube);
        _structureDirty = true;
        return true;
    }

    // Получить id соседей куба по id (до 6 штук)
    public int GetNeighborIds(int cubeId, System.Collections.Generic.List<int> output)
    {
        output.Clear();
        if (!TryGetCubeById(cubeId, out Cube cube) || cube == null)
            return 0;

        Vector3Int gp = GridPosition(cube.transform.localPosition);
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            int nx = gp.x + NeighborOffsets[i].x;
            int ny = gp.y + NeighborOffsets[i].y;
            int nz = gp.z + NeighborOffsets[i].z;
            if (nx < 0 || ny < 0 || nz < 0 || nx >= _cubesInfoSizeX || ny >= _cubesInfoSizeY || nz >= _cubesInfoSizeZ)
                continue;
            int nid = _cubesInfo[nx, ny, nz];
            if (nid > 0) output.Add(nid);
        }

        return output.Count;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _cubesInfo == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
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
