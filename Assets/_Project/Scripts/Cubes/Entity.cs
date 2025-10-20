using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
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
        _meshCombiner.ShowCubes();
        UpdateMassAndCubes();
        RequestDelayedCombine();
    }

    private void UpdateMassAndCubes()
    {
        if (_rb)
            _rb.mass = transform.childCount / 10;

        CollectCubes();
        RecalculateCubes();
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

        HashSet<int> freeCubeIds = new HashSet<int>();
        for (int i = 0; i < _cubes.Length; i++)
        {
            if (_cubes[i] != null)
                freeCubeIds.Add(_cubes[i].Id);
        }

        if (freeCubeIds.Count == 0)
        {
            Destroy(gameObject);
            return;
        }

        List<CubeGroup> groups = new List<CubeGroup>();
        int currentGroup = 0;

        while (freeCubeIds.Count > 0)
        {
            groups.Add(new CubeGroup());
            int id = 0;
            foreach (int cubeId in freeCubeIds)
            {
                id = cubeId;
                break;
            }

            groups[currentGroup].Cubes.Add(id);
            freeCubeIds.Remove(id);
            checkCube(id);
            currentGroup++;

            void checkCube(int id)
            {
                Vector3Int gridPosition = GridPosition(_cubes[id - 1].transform.localPosition);

                checkNeighbor(Vector3Int.up);
                checkNeighbor(Vector3Int.right);
                checkNeighbor(Vector3Int.down);
                checkNeighbor(Vector3Int.left);
                checkNeighbor(Vector3Int.forward);
                checkNeighbor(Vector3Int.back);

                void checkNeighbor(Vector3Int direction)
                {
                    int id = GetNeighbor(gridPosition, direction);
                    if (freeCubeIds.Remove(id))
                    {
                        groups[currentGroup].Cubes.Add(id);
                        checkCube(id);
                    }
                }
            }
        }

        if (groups.Count < 2)
            return;

        for (int i = 1; i < groups.Count; i++)
        {
            GameObject entity = new GameObject("Entity");
            var firstCube = _cubes[groups[i].Cubes[0] - 1].transform;
            entity.transform.SetPositionAndRotation(firstCube.position, firstCube.rotation);
            entity.transform.localScale = transform.localScale;

            foreach (int id in groups[i].Cubes)
            {
                _cubes[id - 1].transform.parent = entity.transform;
            }

            Entity addEntity = entity.AddComponent<Entity>();
            addEntity.StartSetup();

            if (_vehicleConnector)
                _vehicleConnector.OnEntityRecalculated();
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

        cube.transform.DOScale(0.0f, 2).OnComplete(() => Destroy(cube.gameObject));

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

public class CubeGroup
{
    public List<int> Cubes = new List<int>();
}