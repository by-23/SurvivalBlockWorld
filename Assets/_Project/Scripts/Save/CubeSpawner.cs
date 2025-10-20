using System.Collections.Generic;
using UnityEngine;

public class CubeSpawner : MonoBehaviour
{
    [Header("Cube Prefabs")] [SerializeField]
    private GameObject defaultCubePrefab;

    [SerializeField] private GameObject[] cubePrefabsByType;

    private Dictionary<byte, GameObject> prefabDictionary = new Dictionary<byte, GameObject>();
    private Dictionary<byte, PrefabComponents> componentCache = new Dictionary<byte, PrefabComponents>();

    private struct PrefabComponents
    {
        public bool hasCube;
        public bool hasColorCube;
    }

    private void Awake()
    {
        if (cubePrefabsByType != null)
        {
            for (byte i = 0; i < cubePrefabsByType.Length; i++)
            {
                if (cubePrefabsByType[i] != null)
                {
                    prefabDictionary[i] = cubePrefabsByType[i];
                    CachePrefabComponents(i, cubePrefabsByType[i]);
                }
            }
        }

        if (defaultCubePrefab != null)
        {
            CachePrefabComponents(255, defaultCubePrefab);
        }
    }

    private void CachePrefabComponents(byte typeId, GameObject prefab)
    {
        componentCache[typeId] = new PrefabComponents
        {
            hasCube = prefab.GetComponent<Cube>() != null,
            hasColorCube = prefab.GetComponent<ColorCube>() != null
        };
    }

    public GameObject SpawnCube(CubeData data, Transform parent = null)
    {
        GameObject prefab = GetPrefabForType(data.blockTypeId);
        byte cacheKey = data.blockTypeId;

        if (prefab == null)
        {
            prefab = defaultCubePrefab;
            cacheKey = 255;
        }

        if (prefab == null)
        {
            Debug.LogError("No cube prefab available for spawning!");
            return null;
        }

        GameObject cube = Instantiate(prefab, data.Position, data.Rotation, parent);

        if (componentCache.TryGetValue(cacheKey, out PrefabComponents components))
        {
            if (components.hasCube)
            {
                Cube cubeComponent = cube.GetComponent<Cube>();
                cubeComponent.BlockTypeID = data.blockTypeId;
            }

            if (components.hasColorCube)
            {
                ColorCube colorCube = cube.GetComponent<ColorCube>();
                colorCube.Setup(data.Color);
            }
        }

        return cube;
    }

    public List<GameObject> SpawnCubeBatch(List<CubeData> cubes, Transform parent = null)
    {
        List<GameObject> spawnedCubes = new List<GameObject>(cubes.Count);

        foreach (var cubeData in cubes)
        {
            GameObject cube = SpawnCubeOptimized(cubeData, parent);
            if (cube != null)
            {
                spawnedCubes.Add(cube);
            }
        }

        return spawnedCubes;
    }

    private GameObject SpawnCubeOptimized(CubeData data, Transform parent)
    {
        GameObject prefab = GetPrefabForType(data.blockTypeId);
        byte cacheKey = data.blockTypeId;

        if (prefab == null)
        {
            prefab = defaultCubePrefab;
            cacheKey = 255;
        }

        if (prefab == null)
            return null;

        GameObject cube = Instantiate(prefab, parent);
        cube.transform.localPosition = data.Position;
        cube.transform.localRotation = data.Rotation;

        if (componentCache.TryGetValue(cacheKey, out PrefabComponents components))
        {
            if (components.hasCube)
            {
                cube.GetComponent<Cube>().BlockTypeID = data.blockTypeId;
            }

            if (components.hasColorCube)
            {
                cube.GetComponent<ColorCube>().Setup(data.Color);
            }
        }

        return cube;
    }

    private GameObject GetPrefabForType(byte typeId)
    {
        if (prefabDictionary.TryGetValue(typeId, out GameObject prefab))
        {
            return prefab;
        }

        return null;
    }

    public void RegisterPrefab(byte typeId, GameObject prefab)
    {
        prefabDictionary[typeId] = prefab;
    }
}


