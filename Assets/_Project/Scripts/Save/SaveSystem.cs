using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SaveSystem : MonoBehaviour
{
    private static SaveSystem _instance;

    public static SaveSystem Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("SaveSystem");
                _instance = go.AddComponent<SaveSystem>();
                DontDestroyOnLoad(go);
            }

            return _instance;
        }
    }

    [Header("Configuration")] [SerializeField]
    private SaveConfig _config;

    [Header("Screenshot")] [SerializeField]
    private Camera _screenshotCamera;

    [SerializeField] private int _screenshotWidth = 1920;
    [SerializeField] private int _screenshotHeight = 1080;

    [Header("Spawning")] [SerializeField] private CubeSpawner _cubeSpawner;

    private ChunkManager _chunkManager;
    private FileManager _fileManager;
    private FirebaseAdapter _firebaseAdapter;


    private void Awake()
    {
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (_config == null)
        {
            Debug.LogError("SaveConfig not assigned to SaveSystem!");
            return;
        }

        _chunkManager = new ChunkManager(_config);
        _fileManager = new FileManager(_config);
        _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);

        if (_cubeSpawner == null)
        {
            _cubeSpawner = gameObject.AddComponent<CubeSpawner>();
        }

        // Проверяем, что CubeSpawner имеет доступные префабы
        if (!_cubeSpawner.HasAvailablePrefabs())
        {
            Debug.LogWarning("CubeSpawner не имеет доступных префабов! Попытка загрузки из Resources...");
        }
    }

    [ContextMenu("Save Current World")]
    public void SaveWorld(string worldName)
    {
        _ = SaveWorldAsync(worldName);
    }

    public async System.Threading.Tasks.Task<bool> SaveWorldAsync(string worldName,
        Action<float> progressCallback = null)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Save failed: World name cannot be empty.");
                return false;
            }

            progressCallback?.Invoke(0.1f);

            string screenshotPath = TakeScreenshot(worldName);

            progressCallback?.Invoke(0.2f);

            List<CubeData> allCubes = CollectAllCubesFromScene();
            Debug.Log($"Collected {allCubes.Count} cubes from scene");

            progressCallback?.Invoke(0.3f);

            Dictionary<Vector3Int, ChunkData> chunks = _chunkManager.OrganizeCubesIntoChunks(allCubes);
            Debug.Log($"Organized into {chunks.Count} chunks");

            progressCallback?.Invoke(0.5f);

            WorldSaveData worldData = new WorldSaveData(
                worldName,
                _config.worldBoundsMin,
                _config.worldBoundsMax
            )
            {
                Chunks = chunks,
                ScreenshotPath = screenshotPath
            };

            progressCallback?.Invoke(0.7f);

            if (_config.useLocalCache)
            {
                bool success =
                    await _fileManager.SaveWorldAsync(worldData, (p) => progressCallback?.Invoke(0.7f + p * 0.15f));
                if (!success) return false;
            }

            if (_config.useFirebase)
            {
                bool success = await _firebaseAdapter.SaveWorldToFirestore(worldData);
                if (!success) return false;
            }

            _chunkManager.ClearDirtyChunks();
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
    public void LoadWorld(string worldName)
    {
        _ = LoadWorldAsync(worldName);
    }

    public async System.Threading.Tasks.Task<bool> LoadWorldAsync(string worldName,
        Action<float> progressCallback = null)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Load failed: World name cannot be empty.");
                return false;
            }

            progressCallback?.Invoke(0.1f);

            WorldSaveData worldData = null;

            // TODO: Add logic to prefer local cache if available and newer
            if (_config.useFirebase)
            {
                worldData = await _firebaseAdapter.LoadWorldFromFirestore(worldName);
            }
            else if (_config.useLocalCache &&
                     _fileManager.SaveFileExists()) // This part might need adjustment for multiple saves
            {
                worldData = await _fileManager.LoadWorldAsync((p) => progressCallback?.Invoke(0.1f + p * 0.3f));
            }


            if (worldData == null)
            {
                Debug.LogWarning($"No save data found for world '{worldName}'");
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
            Debug.LogError($"LoadWorld failed for world '{worldName}': {e.Message}");
            return false;
        }
    }

    [ContextMenu("Clear Save Data")]
    public async void ClearSaveData()
    {
        try
        {
            bool success = true;

            // Очищаем локальный кэш
            if (_config.useLocalCache)
            {
                _fileManager.DeleteSaveFile();
                Debug.Log("Local save data cleared");
            }

            // Очищаем Firebase (удаляем все карты)
            if (_config.useFirebase && _firebaseAdapter != null)
            {
                var worldsMetadata = await _firebaseAdapter.GetAllWorldsMetadata();
                foreach (var world in worldsMetadata)
                {
                    bool deleteSuccess = await _firebaseAdapter.DeleteWorldFromFirestore(world.WorldName);
                    if (!deleteSuccess)
                    {
                        success = false;
                        Debug.LogError($"Failed to delete world '{world.WorldName}' from Firebase");
                    }
                }

                Debug.Log($"Firebase data cleared - {worldsMetadata.Count} worlds deleted");
            }

            if (success)
            {
                Debug.Log("All save data cleared successfully");
            }
            else
            {
                Debug.LogWarning("Some save data could not be cleared");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error clearing save data: {e.Message}");
        }
    }

    public async System.Threading.Tasks.Task<bool> DeleteWorldAsync(string worldName)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Delete failed: World name cannot be empty.");
                return false;
            }

            bool success = true;

            if (_config.useFirebase)
            {
                success = await _firebaseAdapter.DeleteWorldFromFirestore(worldName);
            }

            if (success)
            {
                Debug.Log($"World '{worldName}' deleted successfully.");
            }
            else
            {
                Debug.LogError($"Failed to delete world '{worldName}'.");
            }

            return success;
        }
        catch (Exception e)
        {
            Debug.LogError($"DeleteWorld failed for world '{worldName}': {e.Message}");
            return false;
        }
    }

    private string TakeScreenshot(string worldName)
    {
        if (_screenshotCamera == null)
        {
            Debug.LogError("Screenshot camera is not assigned in SaveSystem.");
            return string.Empty;
        }

        try
        {
            _screenshotCamera.gameObject.SetActive(true);
            RenderTexture rt = new RenderTexture(_screenshotWidth, _screenshotHeight, 24);
            _screenshotCamera.targetTexture = rt;
            Texture2D screenShot = new Texture2D(_screenshotWidth, _screenshotHeight, TextureFormat.RGB24, false);
            _screenshotCamera.Render();
            RenderTexture.active = rt;
            screenShot.ReadPixels(new Rect(0, 0, _screenshotWidth, _screenshotHeight), 0, 0);
            _screenshotCamera.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);
            byte[] bytes = screenShot.EncodeToPNG();
            string filename = System.IO.Path.Combine(Application.persistentDataPath, worldName + ".png");
            System.IO.File.WriteAllBytes(filename, bytes);
            _screenshotCamera.gameObject.SetActive(false);
            Debug.Log($"Screenshot saved to {filename}");
            return filename;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to take screenshot: {e.Message}");
            return string.Empty;
        }
    }

    private List<CubeData> CollectAllCubesFromScene()
    {
        List<CubeData> allCubes = new List<CubeData>();
        Entity[] entities = FindObjectsByType<Entity>(FindObjectsSortMode.None);

        foreach (var entity in entities)
        {
            CubeData[] entityCubes = entity.GetSaveData();
            allCubes.AddRange(entityCubes);
        }

        return allCubes;
    }

    private async System.Threading.Tasks.Task SpawnWorldFromData(WorldSaveData worldData,
        Action<float> progressCallback = null)
    {
        bool wasAutoSimulation = Physics.simulationMode == SimulationMode.Update;
        if (_config.disablePhysicsDuringLoad)
        {
            Physics.simulationMode = SimulationMode.Script;
        }

        int totalCubes = worldData.Chunks.Values.Sum(c => c.cubes.Count);
        int spawnedCubes = 0;

        Dictionary<Vector3Int, List<CubeData>> cubesByEntity = GroupCubesByEntity(worldData);
        List<Entity> allEntities = _config.useDeferredSetup ? new List<Entity>(cubesByEntity.Count) : null;

        int batchSize = Mathf.Max(_config.maxCubesPerFrame, _config.minBatchSize);
        int processedInBatch = 0;

        foreach (var kvp in cubesByEntity)
        {
            if (kvp.Value.Count == 0)
                continue;

            Vector3 minPos = kvp.Value[0].Position;
            foreach (var cube in kvp.Value)
            {
                if (cube.Position != null)
                {
                    if (cube.Position.x < minPos.x) minPos.x = cube.Position.x;
                    if (cube.Position.y < minPos.y) minPos.y = cube.Position.y;
                    if (cube.Position.z < minPos.z) minPos.z = cube.Position.z;
                }
            }

            GameObject entityObj = new GameObject("Entity");
            entityObj.transform.position = minPos;
            entityObj.transform.localScale = Vector3.one * _config.entityScale;

            Entity entity = entityObj.AddComponent<Entity>();
            await entity.LoadFromDataAsync(kvp.Value.ToArray(), _cubeSpawner, deferredSetup: _config.useDeferredSetup);

            if (_config.useDeferredSetup)
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

        if (_config.useDeferredSetup && allEntities != null)
        {
            progressCallback?.Invoke(0.85f);

            foreach (var entity in allEntities)
            {
                if (entity != null)
                {
                    entity.FinalizeLoad();
                }
            }

            progressCallback?.Invoke(0.95f);
            await System.Threading.Tasks.Task.Yield();
        }

        if (_config.disablePhysicsDuringLoad)
        {
            Physics.simulationMode = wasAutoSimulation ? SimulationMode.Update : SimulationMode.Script;
        }

        progressCallback?.Invoke(1f);

    }

    private Dictionary<Vector3Int, List<CubeData>> GroupCubesByEntity(WorldSaveData worldData)
    {
        List<CubeData> allCubes = new List<CubeData>();
        foreach (var chunk in worldData.Chunks.Values)
        {
            allCubes.AddRange(chunk.cubes);
        }

        if (allCubes.Count == 0)
            return new Dictionary<Vector3Int, List<CubeData>>();

        // Группируем кубы по их entityId
        Dictionary<int, List<CubeData>> cubesByEntityId = new Dictionary<int, List<CubeData>>();

        foreach (var cube in allCubes)
        {
            if (!cubesByEntityId.ContainsKey(cube.entityId))
            {
                cubesByEntityId[cube.entityId] = new List<CubeData>();
            }

            cubesByEntityId[cube.entityId].Add(cube);
        }


        // Конвертируем в формат, ожидаемый SpawnWorldFromData
        Dictionary<Vector3Int, List<CubeData>> entityGroups = new Dictionary<Vector3Int, List<CubeData>>();
        int groupIndex = 0;

        foreach (var kvp in cubesByEntityId)
        {
            Vector3Int groupKey = new Vector3Int(groupIndex, 0, 0);
            entityGroups[groupKey] = kvp.Value;
            groupIndex++;
        }

        return entityGroups;
    }

    private void ClearCurrentWorld()
    {
        Entity[] entities = FindObjectsByType<Entity>(FindObjectsSortMode.None);
        foreach (var entity in entities)
        {
            Destroy(entity.gameObject);
        }

    }

    public void MarkCubeDirty(Vector3 position)
    {
        _chunkManager.MarkChunkDirty(position);
    }

    public SaveConfig Config => _config;
    public ChunkManager ChunkManager => _chunkManager;
    public CubeSpawner CubeSpawner => _cubeSpawner;

    public async System.Threading.Tasks.Task<List<WorldMetadata>> GetAllWorldsMetadata()
    {
        if (_firebaseAdapter == null)
        {
            Debug.LogError("FirebaseAdapter not initialized!");
            return new List<WorldMetadata>();
        }

        return await _firebaseAdapter.GetAllWorldsMetadata();
    }
}


