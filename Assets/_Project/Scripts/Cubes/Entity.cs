using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(Rigidbody)), RequireComponent(typeof(EntityMeshCombiner))]
public class Entity : MonoBehaviour
{
    public bool _StartCheck;

    private static int _nextEntityId = 1;
    public int EntityId { get; private set; }
    private bool _isLoading = false;

    private Rigidbody _rb;
    private int[,,] _cubesInfo;
    private Vector3 _cubesInfoStartPosition;
    private Cube[] _cubes;

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

    private void UpdateMassAndCubes()
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

        for (int i = 0; i < childCount; i++)
        {
            Transform child = _cubes[i].transform;
            min = Vector3.Min(min, child.localPosition);
            max = Vector3.Max(max, child.localPosition);
        }

        Vector3Int delta = Vector3Int.RoundToInt(max - min);
        _cubesInfo = new int[delta.x + 1, delta.y + 1, delta.z + 1];
        _cubesInfoStartPosition = min;
        
        for (int i = 0; i < childCount; i++)
        {
            Vector3Int grid = GridPosition(_cubes[i].transform.localPosition);
            _cubesInfo[grid.x, grid.y, grid.z] = i + 1;
            if (_cubes[i] != null)
            {
                _cubes[i].Id = i + 1;
                _cubes[i].SetEntity(this);
            }
        }

        if (_hookManager)
            _hookManager.DetachAllHooks(_cubes);
    }

    private void RecalculateCubes()
    {
        if (_isLoading)
        {
            return;
        }

        var allCubes = new Dictionary<int, Cube>();
        foreach (var cube in _cubes)
        {
            if (cube != null)
            {
                allCubes.Add(cube.Id, cube);
            }
        }

        if (allCubes.Count == 0)
        {
            Destroy(gameObject);
            return;
        }

        var groups = new List<List<int>>();
        var visitedCubeIds = new HashSet<int>();

        foreach (var cubeId in allCubes.Keys)
        {
            if (visitedCubeIds.Contains(cubeId))
            {
                continue;
            }

            var newGroup = new List<int>();
            var queue = new Queue<int>();

            queue.Enqueue(cubeId);
            visitedCubeIds.Add(cubeId);

            while (queue.Count > 0)
            {
                int currentCubeId = queue.Dequeue();
                newGroup.Add(currentCubeId);

                if (!allCubes.TryGetValue(currentCubeId, out var currentCube)) continue;

                Vector3Int gridPosition = GridPosition(currentCube.transform.localPosition);

                CheckNeighbor(gridPosition, Vector3Int.up);
                CheckNeighbor(gridPosition, Vector3Int.down);
                CheckNeighbor(gridPosition, Vector3Int.left);
                CheckNeighbor(gridPosition, Vector3Int.right);
                CheckNeighbor(gridPosition, Vector3Int.forward);
                CheckNeighbor(gridPosition, Vector3Int.back);
            }

            groups.Add(newGroup);

            void CheckNeighbor(Vector3Int currentPos, Vector3Int direction)
            {
                int neighborId = GetNeighbor(currentPos, direction);
                if (neighborId > 0 && allCubes.ContainsKey(neighborId) && !visitedCubeIds.Contains(neighborId))
                {
                    visitedCubeIds.Add(neighborId);
                    queue.Enqueue(neighborId);
                }
            }
        }

        if (groups.Count < 2)
        {
            return;
        }

        groups = groups.OrderByDescending(g => g.Count).ToList();

        for (int i = 1; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Count == 0) continue;

            GameObject newEntityObject = new GameObject("Entity");

            Cube firstCubeInGroup = allCubes[group[0]];
            newEntityObject.transform.SetPositionAndRotation(firstCubeInGroup.transform.position, firstCubeInGroup.transform.rotation);
            newEntityObject.transform.localScale = transform.localScale;

            foreach (int cubeId in group)
            {
                if (allCubes.TryGetValue(cubeId, out var cubeToMove))
                {
                    cubeToMove.transform.parent = newEntityObject.transform;
                }
            }

            Entity newEntity = newEntityObject.AddComponent<Entity>();
            newEntity.StartSetup();

            if (_vehicleConnector)
            {
                _vehicleConnector.OnEntityRecalculated();
            }
        }

        CollectCubes();
    }


    public void DetouchCube(Cube cube)
    {
        _meshCombiner.ShowCubes();
        
        Vector3Int grid = GridPosition(cube.transform.localPosition);
        _cubesInfo[grid.x, grid.y, grid.z] = 0;
        _cubes[cube.Id - 1] = null;

        cube.transform.parent = null;
        var rb = cube.gameObject.AddComponent<Rigidbody>();

        if (_hookManager)
            _hookManager.DetachHookFromCube(cube);

        StartCoroutine(ScaleDownAndDestroy(cube.transform, 2f));

        RecalculateCubes();
        RequestDelayedCombine();
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
        return Vector3Int.RoundToInt(localPosition - _cubesInfoStartPosition);
    }

    private int GetNeighbor(Vector3Int position, Vector3Int direction)
    {
        Vector3Int gridPosition = position + direction;
        if (gridPosition.x < 0 || gridPosition.y < 0 || gridPosition.z < 0)
            return 0;

        if (gridPosition.x >= _cubesInfo.GetLength(0) || gridPosition.y >= _cubesInfo.GetLength(1) ||
            gridPosition.z >= _cubesInfo.GetLength(2))
            return 0;

        return _cubesInfo[gridPosition.x, gridPosition.y, gridPosition.z];
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
        StartSetup();
        _isLoading = false;

        if (_rb)
            _rb.isKinematic = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _cubesInfo == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
        int xLength = _cubesInfo.GetLength(0);
        int yLength = _cubesInfo.GetLength(1);
        int zLength = _cubesInfo.GetLength(2);

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
