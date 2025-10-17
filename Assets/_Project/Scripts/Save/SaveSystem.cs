using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SaveSystem : MonoBehaviour
{
    private static SaveSystem instance;
    public static SaveSystem Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("SaveSystem");
                instance = go.AddComponent<SaveSystem>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    [Header("Configuration")]
    [SerializeField] private SaveConfig config;

    [Header("Spawning")]
    [SerializeField] private CubeSpawner cubeSpawner;

    private ChunkManager chunkManager;
    private FileManager fileManager;
    private FirebaseAdapter firebaseAdapter;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (config == null)
        {
            Debug.LogError("SaveConfig not assigned to SaveSystem!");
            return;
        }

        chunkManager = new ChunkManager(config);
        fileManager = new FileManager(config);
        firebaseAdapter = new FirebaseAdapter(config, chunkManager);

        if (cubeSpawner == null)
        {
            cubeSpawner = gameObject.AddComponent<CubeSpawner>();
        }
    }

    [ContextMenu("Save Current World")]
    public async void SaveWorld()
    {
        await SaveWorldAsync();
    }

    public async System.Threading.Tasks.Task<bool> SaveWorldAsync(Action<float> progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke(0.1f);

            List<CubeData> allCubes = CollectAllCubesFromScene();
            Debug.Log($"Collected {allCubes.Count} cubes from scene");

            progressCallback?.Invoke(0.3f);

            Dictionary<Vector3Int, ChunkData> chunks = chunkManager.OrganizeCubesIntoChunks(allCubes);
            Debug.Log($"Organized into {chunks.Count} chunks");

            progressCallback?.Invoke(0.5f);

            WorldSaveData worldData = new WorldSaveData(
                "MainWorld",
                config.worldBoundsMin,
                config.worldBoundsMax
            );
            worldData.chunks = chunks;

            progressCallback?.Invoke(0.7f);

            if (config.useLocalCache)
            {
                bool success = await fileManager.SaveWorldAsync(worldData, (p) => progressCallback?.Invoke(0.7f + p * 0.3f));
                if (!success) return false;
            }

            if (config.useFirebase)
            {
                foreach (var chunk in chunks.Values)
                {
                    await firebaseAdapter.SaveChunkToFirestore(chunk);
                }
            }

            chunkManager.ClearDirtyChunks();
            progressCallback?.Invoke(1f);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveWorld failed: {e.Message}");
            return false;
        }
    }

    [ContextMenu("Load World")]
    public async void LoadWorld()
    {
        await LoadWorldAsync();
    }

    public async System.Threading.Tasks.Task<bool> LoadWorldAsync(Action<float> progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke(0.1f);

            WorldSaveData worldData = null;

            if (config.useLocalCache && fileManager.SaveFileExists())
            {
                worldData = await fileManager.LoadWorldAsync((p) => progressCallback?.Invoke(0.1f + p * 0.3f));
            }
            else if (config.useFirebase)
            {
                worldData = await firebaseAdapter.LoadWorldFromFirestore("MainWorld");
            }

            if (worldData == null)
            {
                Debug.LogWarning("No save data found");
                return false;
            }

            progressCallback?.Invoke(0.4f);

            ClearCurrentWorld();

            progressCallback?.Invoke(0.5f);

            await SpawnWorldFromData(worldData, (p) => progressCallback?.Invoke(0.5f + p * 0.5f));

            progressCallback?.Invoke(1f);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"LoadWorld failed: {e.Message}");
            return false;
        }
    }

    [ContextMenu("Clear Save Data")]
    public void ClearSaveData()
    {
        if (config.useLocalCache)
        {
            fileManager.DeleteSaveFile();
        }

        Debug.Log("Save data cleared");
    }

    private List<CubeData> CollectAllCubesFromScene()
    {
        List<CubeData> allCubes = new List<CubeData>();
        Entity[] entities = FindObjectsOfType<Entity>();

        foreach (var entity in entities)
        {
            CubeData[] entityCubes = entity.GetSaveData();
            allCubes.AddRange(entityCubes);
        }

        return allCubes;
    }

    private async System.Threading.Tasks.Task SpawnWorldFromData(WorldSaveData worldData, Action<float> progressCallback = null)
    {
        bool wasAutoSimulation = Physics.autoSimulation;
        if (config.disablePhysicsDuringLoad)
        {
            Physics.autoSimulation = false;
        }

        int totalCubes = worldData.chunks.Values.Sum(c => c.cubes.Count);
        int spawnedCubes = 0;

        Dictionary<Vector3Int, List<CubeData>> cubesByEntity = GroupCubesByEntity(worldData);
        List<Entity> allEntities = config.useDeferredSetup ? new List<Entity>(cubesByEntity.Count) : null;

        int batchSize = Mathf.Max(config.maxCubesPerFrame, config.minBatchSize);
        int processedInBatch = 0;

        foreach (var kvp in cubesByEntity)
        {
            if (kvp.Value.Count == 0)
                continue;

            Vector3 minPos = kvp.Value[0].Position;
            foreach (var cube in kvp.Value)
            {
                if (cube.Position.x < minPos.x) minPos.x = cube.Position.x;
                if (cube.Position.y < minPos.y) minPos.y = cube.Position.y;
                if (cube.Position.z < minPos.z) minPos.z = cube.Position.z;
            }

            GameObject entityObj = new GameObject("Entity");
            entityObj.transform.position = minPos;

            Entity entity = entityObj.AddComponent<Entity>();
            await entity.LoadFromDataAsync(kvp.Value.ToArray(), cubeSpawner, deferredSetup: config.useDeferredSetup);

            if (config.useDeferredSetup)
            {
                allEntities.Add(entity);
            }

            spawnedCubes += kvp.Value.Count;
            processedInBatch += kvp.Value.Count;
            progressCallback?.Invoke((float)spawnedCubes / totalCubes * 0.8f);

            if (processedInBatch >= batchSize)
            {
                await System.Threading.Tasks.Task.Yield();
                processedInBatch = 0;
            }
        }

        if (config.useDeferredSetup && allEntities != null)
        {
            progressCallback?.Invoke(0.85f);

            foreach (var entity in allEntities)
            {
                entity.FinalizeLoad();
            }

            progressCallback?.Invoke(0.95f);
            await System.Threading.Tasks.Task.Yield();
        }

        if (config.disablePhysicsDuringLoad)
        {
            Physics.autoSimulation = wasAutoSimulation;
        }

        progressCallback?.Invoke(1f);

        Debug.Log($"Spawned {spawnedCubes} cubes in {cubesByEntity.Count} entities");
    }

    private Dictionary<Vector3Int, List<CubeData>> GroupCubesByEntity(WorldSaveData worldData)
    {
        List<CubeData> allCubes = new List<CubeData>();
        foreach (var chunk in worldData.chunks.Values)
        {
            allCubes.AddRange(chunk.cubes);
        }

        if (allCubes.Count == 0)
            return new Dictionary<Vector3Int, List<CubeData>>();

        Dictionary<Vector3, CubeData> cubePositions = new Dictionary<Vector3, CubeData>();
        foreach (var cube in allCubes)
        {
            cubePositions[cube.Position] = cube;
        }

        HashSet<Vector3> processedCubes = new HashSet<Vector3>();
        Dictionary<Vector3Int, List<CubeData>> entityGroups = new Dictionary<Vector3Int, List<CubeData>>();
        int groupIndex = 0;

        foreach (var cube in allCubes)
        {
            if (processedCubes.Contains(cube.Position))
                continue;

            List<CubeData> group = new List<CubeData>();
            Queue<Vector3> toProcess = new Queue<Vector3>();
            toProcess.Enqueue(cube.Position);
            processedCubes.Add(cube.Position);

            while (toProcess.Count > 0)
            {
                Vector3 currentPos = toProcess.Dequeue();
                group.Add(cubePositions[currentPos]);

                Vector3[] neighbors = new Vector3[]
                {
                    currentPos + Vector3.up,
                    currentPos + Vector3.down,
                    currentPos + Vector3.left,
                    currentPos + Vector3.right,
                    currentPos + Vector3.forward,
                    currentPos + Vector3.back
                };

                foreach (var neighborPos in neighbors)
                {
                    if (cubePositions.ContainsKey(neighborPos) && !processedCubes.Contains(neighborPos))
                    {
                        processedCubes.Add(neighborPos);
                        toProcess.Enqueue(neighborPos);
                    }
                }
            }

            Vector3Int groupKey = new Vector3Int(groupIndex, 0, 0);
            entityGroups[groupKey] = group;
            groupIndex++;
        }

        return entityGroups;
    }

    private void ClearCurrentWorld()
    {
        Entity[] entities = FindObjectsOfType<Entity>();
        foreach (var entity in entities)
        {
            Destroy(entity.gameObject);
        }

        Debug.Log($"Cleared {entities.Length} entities from scene");
    }

    public void MarkCubeDirty(Vector3 position)
    {
        chunkManager.MarkChunkDirty(position);
    }

    public SaveConfig Config => config;
    public ChunkManager ChunkManager => chunkManager;
    public CubeSpawner CubeSpawner => cubeSpawner;
}


