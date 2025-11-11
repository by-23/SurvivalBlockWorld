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
    public Cube[] Cubes => _cubes;

    private int _cubesInfoSizeX;
    private int _cubesInfoSizeY;
    private int _cubesInfoSizeZ;

    private Dictionary<int, int> _cubeIdToIndex;
    private bool _cacheValid = false;
    private int _lastChildCount = -1;
    private Vector3[] _cachedPositionsBuffer;
    private Cube[] _tempActiveCubes;
    private int[] _tempCubeIds;
    private bool[] _tempVisited;
    private int[] _tempQueue;
    private int[] _tempCurrentGroup;
    private int[][] _tempGroups;
    private int[] _tempGroupSizes;

    private int[] _tempGroupIndices;
    private int[] _idToActiveIndex;
    private Vector3Int[] _tempGridPositions;
    private Cube[] _pendingDetouchBuffer;
    private int _pendingDetouchCount;

    private bool _detouchBatchPending = false;
    private Cube[] _detouchWorkBuffer;
    private const int MAX_CUBES_PER_FRAME_DETOUCH = 15;

    private EntityVehicleConnector _vehicleConnector;
    private EntityHookManager _hookManager;
    private EntityMeshCombiner _meshCombiner;

    private Coroutine _recombineCoroutine;

    [SerializeField] private float _maxWaitTimeForStop = 120f;
    private bool _structureDirty;
    private List<Vector3Int> _affectedCells;

    public bool IsKinematic
    {
        get
        {
            if (!EnsureRigidbodyReference())
                return false;
            return _rb.isKinematic;
        }
    }

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
        _structureDirty = true;
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
        int currentChildCount = transform.childCount;
        if (_cacheValid && currentChildCount == _lastChildCount && _cubes != null && _cubes.Length == currentChildCount)
        {
            bool allValid = true;
            for (int i = 0; i < Mathf.Min(3, currentChildCount); i++)
            {
                if (_cubes[i] == null)
                {
                    allValid = false;
                    break;
                }
            }

            if (allValid)
                return;
        }

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

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        if (_cubeIdToIndex == null)
            _cubeIdToIndex = new Dictionary<int, int>(childCount);
        else
            _cubeIdToIndex.Clear();

        if (_cachedPositionsBuffer == null || _cachedPositionsBuffer.Length < childCount)
            _cachedPositionsBuffer = new Vector3[childCount];

        int validCount = 0;

        for (int i = 0; i < childCount; i++)
        {
            var cube = _cubes[i];
            if (cube == null) continue;

            var pos = cube.transform.localPosition;
            _cachedPositionsBuffer[validCount] = pos;

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

        Vector3Int delta = Vector3Int.RoundToInt(max - min);
        int newSizeX = delta.x + 1;
        int newSizeY = delta.y + 1;
        int newSizeZ = delta.z + 1;

        if (_cubesInfo == null || _cubesInfoSizeX != newSizeX || _cubesInfoSizeY != newSizeY ||
            _cubesInfoSizeZ != newSizeZ)
        {
            _cubesInfo = new int[newSizeX, newSizeY, newSizeZ];
        }
        else
        {
            System.Array.Clear(_cubesInfo, 0, _cubesInfo.Length);
        }

        _cubesInfoSizeX = newSizeX;
        _cubesInfoSizeY = newSizeY;
        _cubesInfoSizeZ = newSizeZ;
        _cubesInfoStartPosition = min;

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

        if (_meshCombiner)
            _meshCombiner.InvalidateCubeCache();
    }

    private void RecalculateCubes()
    {
        if (_isLoading || !_cacheValid)
            return;

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
                    Entity newEntity = EntityFactory.CreateEntity(
                        firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation,
                        transform.localScale,
                        isKinematic: true,
                        entityName: "Entity"
                    );

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
        SetKinematicState(false, true);
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
        yield return null;

        if (_pendingDetouchCount == 0)
        {
            _detouchBatchPending = false;
            yield break;
        }

        if (_detouchWorkBuffer == null || _detouchWorkBuffer.Length < _pendingDetouchCount)
        {
            int newCapacity = _pendingDetouchCount < 16 ? 16 : _pendingDetouchCount;
            _detouchWorkBuffer = new Cube[newCapacity];
        }

        int workCount = _pendingDetouchCount;
        System.Array.Copy(_pendingDetouchBuffer, 0, _detouchWorkBuffer, 0, workCount);
        _pendingDetouchCount = 0;

        _meshCombiner.ShowCubes();

        int processed = 0;
        while (processed < workCount)
        {
            int batchSize = Mathf.Min(MAX_CUBES_PER_FRAME_DETOUCH, workCount - processed);

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

            if (processed < workCount)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        _cacheValid = false;
        RecalculateAroundAffected();
        RequestDelayedCombine();

        SetKinematicState(false, true);

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

        if (_detouchWorkBuffer == null || _detouchWorkBuffer.Length < _pendingDetouchCount)
        {
            int newCapacity = _pendingDetouchCount < 16 ? 16 : _pendingDetouchCount;
            _detouchWorkBuffer = new Cube[newCapacity];
        }

        int workCount = _pendingDetouchCount;
        System.Array.Copy(_pendingDetouchBuffer, 0, _detouchWorkBuffer, 0, workCount);
        _pendingDetouchCount = 0;

        int processed = 0;
        while (processed < workCount)
        {
            int batchSize = Mathf.Min(MAX_CUBES_PER_FRAME_DETOUCH, workCount - processed);

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

            if (processed < workCount)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        _cacheValid = false;
        RecalculateAroundAffected();
        RequestDelayedCombine();

        SetKinematicState(false, true);
    }

    private float _lastCombineTime = -1f;
    private const float MIN_COMBINE_INTERVAL = 0.3f;

    private void RequestDelayedCombine()
    {
        float timeSinceLastCombine = Time.time - _lastCombineTime;
        if (_recombineCoroutine != null || timeSinceLastCombine < MIN_COMBINE_INTERVAL)
        {
            if (_recombineCoroutine != null)
            {
                StopCoroutine(_recombineCoroutine);
                _recombineCoroutine = null;
            }

            if (timeSinceLastCombine < MIN_COMBINE_INTERVAL)
            {
                return;
            }
        }

        _recombineCoroutine = StartCoroutine(DelayedCombineMeshes());
    }

    private IEnumerator DelayedCombineMeshes()
    {
        int cubeCount = transform.childCount;
        float initialDelay = cubeCount > 50 ? 0.2f : 0.1f;
        yield return new WaitForSeconds(initialDelay);

        if (this == null || gameObject == null)
        {
            _recombineCoroutine = null;
            yield break;
        }

        bool alreadyCombined = _meshCombiner != null && _meshCombiner.IsCombined;
        float waitStartTime = Time.time;

        while (true)
        {
            if (this == null || gameObject == null)
            {
                _recombineCoroutine = null;
                yield break;
            }

            if (_rb == null || _rb.isKinematic)
            {
                break;
            }

            if (IsEntityStopped())
            {
                break;
            }

            if (Time.time - waitStartTime >= _maxWaitTimeForStop)
            {
                _recombineCoroutine = null;
                yield break;
            }

            yield return null;
        }

        if (this == null || gameObject == null || _meshCombiner == null)
        {
            _recombineCoroutine = null;
            yield break;
        }

        if (alreadyCombined)
        {
            SetKinematicState(true, true);
        }
        else
        {
            _meshCombiner.CombineMeshes();

            if (_meshCombiner != null && _meshCombiner.IsCombined)
            {
                SetKinematicState(true, true);
            }
        }

        _lastCombineTime = Time.time;
        _recombineCoroutine = null;
    }

    private bool IsEntityStopped(float velocityThreshold = 0.01f, float angularVelocityThreshold = 0.01f)
    {
        if (_rb == null)
            return true;
        if (_rb.isKinematic)
            return true;

        float velocityMagnitude = _rb.velocity.magnitude;
        float angularVelocityMagnitude = _rb.angularVelocity.magnitude;
        return velocityMagnitude <= velocityThreshold && angularVelocityMagnitude <= angularVelocityThreshold;
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

    private void RecalculateAroundAffected()
    {
        if (_affectedCells == null || _affectedCells.Count == 0)
        {
            RecalculateCubes();
            return;
        }

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
                        touchesOutside = true;
                    }
                }
            }

            if (!touchesOutside && groupIds.Count > 0)
            {
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

        CollectCubes();
        SetKinematicState(false, true);
    }

    private static readonly Vector3Int[] NeighborOffsets = new Vector3Int[]
    {
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    private int FillActiveCubesArrays()
    {
        int activeCubeCount = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                activeCubeCount++;
        }

        if (activeCubeCount == 0) return 0;

        if (_tempActiveCubes == null || _tempActiveCubes.Length < activeCubeCount)
        {
            _tempActiveCubes = new Cube[activeCubeCount];
            _tempCubeIds = new int[activeCubeCount];
            _tempVisited = new bool[activeCubeCount];
            _tempQueue = new int[activeCubeCount];
            _tempCurrentGroup = new int[activeCubeCount];
            _tempGridPositions = new Vector3Int[activeCubeCount];
        }

        int idMapSize = _cubes.Length + 1;
        if (_idToActiveIndex == null || _idToActiveIndex.Length < idMapSize)
        {
            _idToActiveIndex = new int[idMapSize];
        }

        for (int i = 0; i < _idToActiveIndex.Length; i++) _idToActiveIndex[i] = -1;

        int index = 0;
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
            {
                int id = _cubes[i].Id;
                _tempCubeIds[index] = id;
                _tempActiveCubes[index] = _cubes[i];
                _tempGridPositions[index] = GridPosition(_cubes[i].transform.localPosition);
                if (id >= 0 && id < _idToActiveIndex.Length)
                    _idToActiveIndex[id] = index;
                index++;
            }
        }

        for (int i = 0; i < activeCubeCount; i++)
        {
            _tempVisited[i] = false;
        }

        if (_tempGroups == null || _tempGroups.Length < activeCubeCount)
        {
            _tempGroups = new int[activeCubeCount][];
        }

        return activeCubeCount;
    }

    private void CheckNeighborOptimized(int x, int y, int z, ref int queueEnd)
    {
        if (x < 0 || y < 0 || z < 0 ||
            x >= _cubesInfoSizeX || y >= _cubesInfoSizeY || z >= _cubesInfoSizeZ)
            return;

        int neighborId = _cubesInfo[x, y, z];
        if (neighborId <= 0) return;

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

    private int FindConnectedGroups(int activeCubeCount, out int[][] groups)
    {
        groups = null;
        if (activeCubeCount == 0) return 0;

        int groupCount = 0;

        for (int i = 0; i < activeCubeCount; i++)
        {
            if (_tempVisited[i])
                continue;

            int currentGroupSize = 0;
            int queueStart = 0;
            int queueEnd = 0;

            _tempQueue[queueEnd++] = i;
            _tempVisited[i] = true;

            while (queueStart < queueEnd)
            {
                int currentIndex = _tempQueue[queueStart++];
                _tempCurrentGroup[currentGroupSize++] = _tempCubeIds[currentIndex];

                Vector3Int gridPos = _tempGridPositions[currentIndex];

                CheckNeighborOptimized(gridPos.x, gridPos.y + 1, gridPos.z, ref queueEnd);
                CheckNeighborOptimized(gridPos.x, gridPos.y - 1, gridPos.z, ref queueEnd);
                CheckNeighborOptimized(gridPos.x - 1, gridPos.y, gridPos.z, ref queueEnd);
                CheckNeighborOptimized(gridPos.x + 1, gridPos.y, gridPos.z, ref queueEnd);
                CheckNeighborOptimized(gridPos.x, gridPos.y, gridPos.z + 1, ref queueEnd);
                CheckNeighborOptimized(gridPos.x, gridPos.y, gridPos.z - 1, ref queueEnd);
            }

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
                SetKinematicState(true, true);
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
            SetKinematicState(true, true);
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

        Entity[] newEntities = new Entity[groupCount - 1];
        int writeIndex = 0;

        SortGroupsBySize(groupCount, groups, out int[] groupIndices);

        for (int i = 1; i < groupCount; i++)
        {
            int groupIndex = groupIndices[i];
            int[] group = groups[groupIndex];

            if (group.Length == 0) continue;

            if (group.Length <= 3)
            {
                for (int j = 0; j < group.Length; j++)
                {
                    if (_cubeIdToIndex.TryGetValue(group[j], out int cubeIndex))
                    {
                        Cube cubeToDestroy = _cubes[cubeIndex];
                        if (cubeToDestroy != null)
                        {
                            Vector3 localPos = cubeToDestroy.transform.localPosition;
                            int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
                            int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
                            int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

                            if (x >= 0 && y >= 0 && z >= 0 &&
                                x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
                            {
                                _cubesInfo[x, y, z] = 0;
                            }

                            int cubeArrayIndex = cubeToDestroy.Id - 1;
                            if (cubeArrayIndex >= 0 && cubeArrayIndex < _cubes.Length)
                            {
                                _cubes[cubeArrayIndex] = null;
                            }

                            if (_cubeIdToIndex != null)
                            {
                                _cubeIdToIndex.Remove(cubeToDestroy.Id);
                            }

                            cubeToDestroy.transform.parent = null;

                            if (_hookManager)
                                _hookManager.DetachHookFromCube(cubeToDestroy);

                            var rb = cubeToDestroy.gameObject.GetComponent<Rigidbody>();
                            if (rb == null)
                            {
                                rb = cubeToDestroy.gameObject.AddComponent<Rigidbody>();
                                rb.mass = 1f;
                                rb.drag = 0.5f;
                                rb.angularDrag = 0.5f;
                            }

                            StartCoroutine(ScaleDownAndDestroyOptimized(cubeToDestroy.transform, 2f));
                        }
                    }
                }

                continue;
            }

            if (_cubeIdToIndex.TryGetValue(group[0], out int firstCubeIndex))
            {
                Cube firstCubeInGroup = _cubes[firstCubeIndex];
                if (firstCubeInGroup != null)
                {
                    Entity newEntity = EntityFactory.CreateEntity(
                        firstCubeInGroup.transform.position,
                        firstCubeInGroup.transform.rotation,
                        transform.localScale,
                        isKinematic: false,
                        entityName: "Entity"
                    );

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

        int mainGroupIndex = groupIndices[0];
        int[] mainGroup = groups[mainGroupIndex];

        if (mainGroup.Length <= 3)
        {
            for (int j = 0; j < mainGroup.Length; j++)
            {
                if (_cubeIdToIndex.TryGetValue(mainGroup[j], out int cubeIndex))
                {
                    Cube cubeToDestroy = _cubes[cubeIndex];
                    if (cubeToDestroy != null)
                    {
                        Vector3 localPos = cubeToDestroy.transform.localPosition;
                        int x = Mathf.RoundToInt(localPos.x - _cubesInfoStartPosition.x);
                        int y = Mathf.RoundToInt(localPos.y - _cubesInfoStartPosition.y);
                        int z = Mathf.RoundToInt(localPos.z - _cubesInfoStartPosition.z);

                        if (x >= 0 && y >= 0 && z >= 0 &&
                            x < _cubesInfoSizeX && y < _cubesInfoSizeY && z < _cubesInfoSizeZ)
                        {
                            _cubesInfo[x, y, z] = 0;
                        }

                        int cubeArrayIndex = cubeToDestroy.Id - 1;
                        if (cubeArrayIndex >= 0 && cubeArrayIndex < _cubes.Length)
                        {
                            _cubes[cubeArrayIndex] = null;
                        }

                        if (_cubeIdToIndex != null)
                        {
                            _cubeIdToIndex.Remove(cubeToDestroy.Id);
                        }

                        cubeToDestroy.transform.parent = null;

                        if (_hookManager)
                            _hookManager.DetachHookFromCube(cubeToDestroy);

                        var rb = cubeToDestroy.gameObject.GetComponent<Rigidbody>();
                        if (rb == null)
                        {
                            rb = cubeToDestroy.gameObject.AddComponent<Rigidbody>();
                            rb.mass = 1f;
                            rb.drag = 0.5f;
                            rb.angularDrag = 0.5f;
                        }

                        StartCoroutine(ScaleDownAndDestroyOptimized(cubeToDestroy.transform, 2f));
                    }
                }
            }

            Destroy(gameObject);

            if (writeIndex == 0)
                return System.Array.Empty<Entity>();

            var result = new Entity[writeIndex];
            System.Array.Copy(newEntities, 0, result, 0, writeIndex);
            return result;
        }

        CollectCubes();
        ClearStructureDirty();
        SetKinematicState(false, true);
        RequestDelayedCombine();

        if (writeIndex == newEntities.Length)
            return newEntities;

        var compact = new Entity[writeIndex];
        System.Array.Copy(newEntities, 0, compact, 0, writeIndex);
        return compact;
    }

    public void EnablePhysics()
    {
        if (_isGhost)
            return;

        SetKinematicState(false, true);
    }

    public void DisablePhysics()
    {
        SetKinematicState(true, true);
    }

    public bool SetKinematicState(bool isKinematic, bool ignoreGhost = false)
    {
        if (!EnsureRigidbodyReference())
            return false;

        if (!ignoreGhost && _isGhost && !isKinematic)
            return false;

        if (_rb.isKinematic == isKinematic)
            return true;

        _rb.isKinematic = isKinematic;

        if (!isKinematic)
        {
            RequestDelayedCombine();
        }

        return true;
    }

    private bool EnsureRigidbodyReference()
    {
        if (_rb != null)
            return true;

        _rb = GetComponent<Rigidbody>();
        return _rb != null;
    }

    public void EnsureCacheValid()
    {
        if (!_cacheValid)
        {
            CollectCubes();
        }
    }

    public void MarkStructureDirty()
    {
        _structureDirty = true;
    }

    public void ClearStructureDirty()
    {
        _structureDirty = false;
    }

    public bool IsStructureDirty => _structureDirty;

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

    public bool RemoveCubeById(int cubeId)
    {
        if (!TryGetCubeById(cubeId, out Cube cube) || cube == null)
            return false;

        DetouchCube(cube);
        _structureDirty = true;
        return true;
    }

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
