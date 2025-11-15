using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveSystem : MonoBehaviour
{
    [Header("Configuration")] [SerializeField]
    private SaveConfig _config;

    [Header("Screenshot")] public Camera _screenshotCamera;

    [SerializeField] private int _screenshotWidth = 640;
    [SerializeField] private int _screenshotHeight = 480;

    [Header("Spawning")] [SerializeField] private CubeSpawner _cubeSpawner;

    private ChunkManager _chunkManager;
    private FileManager _fileManager;
    private FirebaseAdapter _firebaseAdapter;

    private bool _isLoading = false;

    public enum SaveDestination
    {
        Local,
        Online,
        LocalAndOnline
    }

    public enum WorldStorageSource
    {
        Auto,
        LocalOnly,
        OnlineOnly,
        UserPublished,
        Community
    }


    private void Awake()
    {
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        if (Physics.simulationMode != SimulationMode.Update)
        {
            Physics.simulationMode = SimulationMode.Update;
        }

        FindScreenshotCamera();

        if (_config == null)
        {
            _config = Resources.Load<SaveConfig>("SaveConfig");

            if (_config == null)
            {
                Debug.LogError("SaveConfig not found in Resources! SaveSystem initialization failed.");
                return;
            }
        }

        _chunkManager = new ChunkManager(_config);
        _fileManager = new FileManager(_config);
        _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);

        if (_cubeSpawner == null)
            _cubeSpawner = FindAnyObjectByType<CubeSpawner>();


        if (!_cubeSpawner.HasAvailablePrefabs())
        {
            Debug.LogWarning("CubeSpawner не имеет доступных префабов! Попытка загрузки из Resources...");
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private async void Start()
    {
        if (_config != null && _config.useFirebase && _firebaseAdapter != null)
        {
            await UserManager.InitializeUserIdAsync(_firebaseAdapter);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FindScreenshotCamera();
        ValidateCubeSpawnerPrefabs();
        if (_cubeSpawner == null)
            _cubeSpawner = FindAnyObjectByType<CubeSpawner>();
    }

    private void FindScreenshotCamera()
    {
        if (_screenshotCamera == null)
        {
            GameObject screenshotCameraObj = GameObject.FindWithTag("ScreenshotCamera");
            if (screenshotCameraObj != null)
            {
                screenshotCameraObj.TryGetComponent(out _screenshotCamera);
                if (_screenshotCamera != null)
                {
                    _screenshotCamera.gameObject.SetActive(false);
                }
            }
            else
            {
                Debug.LogWarning("ScreenshotCamera object with tag 'ScreenshotCamera' not found in scene!");
            }
        }
    }

    private void ValidateCubeSpawnerPrefabs()
    {
        if (_cubeSpawner != null)
        {
            if (!_cubeSpawner.HasAvailablePrefabs())
            {
                Debug.LogWarning("CubeSpawner не имеет доступных префабов в текущей сцене!");
            }
        }
    }

    private bool IsValidGameScene()
    {
        int currentSceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
        int[] invalidSceneIds = { 0 };

        foreach (int invalidSceneId in invalidSceneIds)
        {
            if (currentSceneBuildIndex == invalidSceneId)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsInMenuScene()
    {
        int currentSceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
        int[] menuSceneIds = { 0 };

        foreach (int menuSceneId in menuSceneIds)
        {
            if (currentSceneBuildIndex == menuSceneId)
            {
                return true;
            }
        }

        return false;
    }

    [ContextMenu("Save Current World")]
    public void SaveWorld(string worldName)
    {
        _ = SaveWorldAsync(worldName);
    }

    public async System.Threading.Tasks.Task<bool> SaveWorldAsync(string worldName,
        Action<float> progressCallback = null)
    {
        return await SaveWorldAsync(worldName, SaveDestination.LocalAndOnline, progressCallback);
    }

    public async System.Threading.Tasks.Task<bool> SaveWorldAsync(string worldName, SaveDestination destination,
        Action<float> progressCallback = null)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Save failed: World name cannot be empty.");
                return false;
            }

            if (!IsValidGameScene())
            {
                Debug.LogError("Save failed: Not in a valid game scene for saving.");
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

            bool requestLocal = destination == SaveDestination.Local || destination == SaveDestination.LocalAndOnline;
            bool requestOnline = destination == SaveDestination.Online || destination == SaveDestination.LocalAndOnline;

            bool shouldSaveLocal = requestLocal && _config.useLocalCache;
            bool shouldSaveOnline = requestOnline && _config.useFirebase;

            if (!shouldSaveLocal && !shouldSaveOnline)
            {
                string errorMsg = $"Save skipped: no enabled destinations for the current configuration. " +
                                  $"Requested: Local={requestLocal}, Online={requestOnline}. " +
                                  $"Config: useLocalCache={_config.useLocalCache}, useFirebase={_config.useFirebase}.";
                Debug.LogError(errorMsg);
                return false;
            }

            if (shouldSaveLocal)
            {
                float localRange = shouldSaveOnline ? 0.15f : 0.3f;
                if (_fileManager == null)
                {
                    _fileManager = new FileManager(_config);
                }

                bool success = await _fileManager.SaveWorldAsync(worldData,
                    (p) => progressCallback?.Invoke(0.7f + p * localRange));
                if (!success)
                {
                    Debug.LogError(
                        $"Failed to save world '{worldName}' to local storage. Check FileManager logs above for details.");
                    return false;
                }

                progressCallback?.Invoke(0.7f + localRange);
            }

            if (shouldSaveOnline)
            {
                progressCallback?.Invoke(shouldSaveLocal ? 0.85f : 0.8f);
                if (_firebaseAdapter == null)
                {
                    _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
                }

                bool success = await _firebaseAdapter.SaveWorldToFirestore(worldData);
                if (!success)
                {
                    Debug.LogError(
                        $"Failed to save world '{worldName}' to Firebase. Check FirebaseAdapter logs above for details.");
                    return false;
                }
            }

            _chunkManager.ClearDirtyChunks();
            progressCallback?.Invoke(1f);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveWorld failed for '{worldName}': {e.Message}\nStackTrace: {e.StackTrace}");
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
        bool shouldLoadScene = IsInMenuScene();
        return await LoadWorldAsync(worldName, shouldLoadScene, progressCallback);
    }

    public async System.Threading.Tasks.Task<bool> LoadWorldAsync(string worldName, bool loadScene,
        Action<float> progressCallback = null, WorldStorageSource source = WorldStorageSource.Auto)
    {
        try
        {
            if (_isLoading)
            {
                Debug.LogWarning($"Загрузка уже выполняется, пропускаем повторный вызов для '{worldName}'");
                return true; // Возвращаем true, так как загрузка уже идет
            }

            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Load failed: World name cannot be empty.");
                return false;
            }

            _isLoading = true;

            if (loadScene && !IsInMenuScene())
            {
                Debug.LogError("Load failed: Scene loading requested but not in menu scene.");
                return false;
            }

            if (!loadScene && !IsValidGameScene())
            {
                Debug.LogError("Load failed: Not in a valid game scene for loading.");
                return false;
            }

            progressCallback?.Invoke(0.1f);

            WorldSaveData worldData = null;

            worldData = await LoadWorldDataAsync(worldName, source,
                (p) => progressCallback?.Invoke(0.1f + p * 0.3f));

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
        finally
        {
            _isLoading = false;
        }
    }

    [ContextMenu("Clear Save Data")]
    public async void ClearSaveData()
    {
        try
        {
            bool success = true;

            if (_config.useLocalCache)
            {
                string directory = _config.GetLocalSavesDirectory();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }

                Debug.Log("Local save data cleared");
            }

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
                if (_firebaseAdapter == null)
                {
                    if (_chunkManager == null)
                    {
                        _chunkManager = new ChunkManager(_config);
                    }

                    _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
                }

                if (!UserManager.IsInitialized)
                {
                    await UserManager.InitializeUserIdAsync(_firebaseAdapter);
                }

                string userId = UserManager.UserId;
                success = await _firebaseAdapter.DeleteWorldFromFirestore(worldName, userId);
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

    public async System.Threading.Tasks.Task<bool> UpdateWorldLikesAsync(string worldName, int likesCount)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Update likes failed: World name cannot be empty.");
                return false;
            }

            if (!_config.useFirebase)
            {
                Debug.LogWarning("Update likes skipped: Firebase disabled in configuration.");
                return false;
            }

            if (_firebaseAdapter == null)
            {
                if (_config == null)
                {
                    _config = Resources.Load<SaveConfig>("SaveConfig");
                    if (_config == null)
                    {
                        Debug.LogError("SaveConfig not found when updating likes.");
                        return false;
                    }
                }

                if (_chunkManager == null)
                {
                    _chunkManager = new ChunkManager(_config);
                }

                _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            }

            return await _firebaseAdapter.UpdateWorldLikes(worldName, likesCount);
        }
        catch (Exception e)
        {
            Debug.LogError($"UpdateWorldLikesAsync failed for world '{worldName}': {e.Message}");
            return false;
        }
    }

    public async System.Threading.Tasks.Task<bool> UpdateWorldLikesWithUserAsync(string worldName, bool isLiking)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Update likes failed: World name cannot be empty.");
                return false;
            }

            if (!_config.useFirebase)
            {
                Debug.LogWarning("Update likes skipped: Firebase disabled in configuration.");
                return false;
            }

            if (!UserManager.IsInitialized)
            {
                if (_firebaseAdapter == null)
                {
                    if (_config == null)
                    {
                        _config = Resources.Load<SaveConfig>("SaveConfig");
                    }

                    if (_chunkManager == null)
                    {
                        _chunkManager = new ChunkManager(_config);
                    }

                    _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
                }

                await UserManager.InitializeUserIdAsync(_firebaseAdapter);
            }

            string userId = UserManager.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                Debug.LogError("User ID not initialized. Cannot update likes.");
                return false;
            }

            if (_firebaseAdapter == null)
            {
                if (_config == null)
                {
                    _config = Resources.Load<SaveConfig>("SaveConfig");
                    if (_config == null)
                    {
                        Debug.LogError("SaveConfig not found when updating likes.");
                        return false;
                    }
                }

                if (_chunkManager == null)
                {
                    _chunkManager = new ChunkManager(_config);
                }

                _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            }

            return await _firebaseAdapter.UpdateWorldLikesWithUser(worldName, userId, isLiking);
        }
        catch (Exception e)
        {
            Debug.LogError($"UpdateWorldLikesWithUserAsync failed for world '{worldName}': {e.Message}");
            return false;
        }
    }

    public async System.Threading.Tasks.Task<HashSet<string>> GetUserLikedWorldsAsync()
    {
        try
        {
            if (!_config.useFirebase)
            {
                return new HashSet<string>();
            }

            if (!UserManager.IsInitialized)
            {
                if (_firebaseAdapter == null)
                {
                    if (_config == null)
                    {
                        _config = Resources.Load<SaveConfig>("SaveConfig");
                    }

                    if (_chunkManager == null)
                    {
                        _chunkManager = new ChunkManager(_config);
                    }

                    _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
                }

                await UserManager.InitializeUserIdAsync(_firebaseAdapter);
            }

            string userId = UserManager.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                return new HashSet<string>();
            }

            if (_firebaseAdapter == null)
            {
                if (_config == null)
                {
                    _config = Resources.Load<SaveConfig>("SaveConfig");
                }

                if (_chunkManager == null)
                {
                    _chunkManager = new ChunkManager(_config);
                }

                _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            }

            return await _firebaseAdapter.GetUserLikedWorldsAsync(userId);
        }
        catch (Exception e)
        {
            Debug.LogError($"GetUserLikedWorldsAsync failed: {e.Message}");
            return new HashSet<string>();
        }
    }

    private string TakeScreenshot(string worldName)
    {
        if (_screenshotCamera == null)
        {
            FindScreenshotCamera();
        }

        if (_screenshotCamera == null)
        {
            Debug.LogError("Screenshot camera is not assigned in SaveSystem and could not be found in scene.");
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
        SimulationMode previousSimulationMode = Physics.simulationMode;
        bool restoreSimulationMode = false;
        if (_config.disablePhysicsDuringLoad && previousSimulationMode != SimulationMode.Script)
        {
            Physics.simulationMode = SimulationMode.Script;
            restoreSimulationMode = true;
        }

        try
        {
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

                // Создаем Entity через фабрику
                Entity entity = EntityFactory.CreateEntity(
                    minPos,
                    Quaternion.identity,
                    Vector3.one * _config.entityScale,
                    isKinematic: true,
                    entityName: "Entity"
                );

                // minPos используется как сохранённая позиция entity для правильного вычисления локальных позиций кубов
                await entity.LoadFromDataAsync(kvp.Value.ToArray(), _cubeSpawner,
                    deferredSetup: _config.useDeferredSetup,
                    savedEntityPosition: minPos);

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

            progressCallback?.Invoke(1f);
        }
        finally
        {
            if (restoreSimulationMode)
            {
                // Возвращаем исходный режим симуляции физики
                Physics.simulationMode = previousSimulationMode;
            }
        }
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

        Dictionary<int, List<CubeData>> cubesByEntityId = new Dictionary<int, List<CubeData>>();

        foreach (var cube in allCubes)
        {
            if (!cubesByEntityId.ContainsKey(cube.entityId))
            {
                cubesByEntityId[cube.entityId] = new List<CubeData>();
            }

            cubesByEntityId[cube.entityId].Add(cube);
        }

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
            Debug.LogWarning("FirebaseAdapter not initialized, attempting to initialize...");

            if (_config == null)
            {
                Debug.LogWarning("SaveConfig not assigned to SaveSystem! Attempting to load from Resources...");
                _config = Resources.Load<SaveConfig>("SaveConfig");

                if (_config == null)
                {
                    Debug.LogError("SaveConfig not found in Resources! Cannot initialize FirebaseAdapter.");
                    return new List<WorldMetadata>();
                }

                Debug.Log("SaveConfig loaded from Resources successfully.");
            }

            if (_chunkManager == null)
            {
                _chunkManager = new ChunkManager(_config);
            }

            _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            Debug.Log("FirebaseAdapter initialized successfully.");
        }

        return await _firebaseAdapter.GetAllWorldsMetadata();
    }

    public async System.Threading.Tasks.Task<List<WorldMetadata>> GetAllLocalWorldsMetadata()
    {
        if (_fileManager == null)
        {
            _fileManager = new FileManager(_config);
        }

        return await _fileManager.LoadLocalWorldsMetadataAsync();
    }

    public async System.Threading.Tasks.Task<List<WorldMetadata>> GetUserWorldsMetadataAsync()
    {
        if (!_config.useFirebase)
        {
            return new List<WorldMetadata>();
        }

        if (_firebaseAdapter == null)
        {
            if (_config == null)
            {
                _config = Resources.Load<SaveConfig>("SaveConfig");
                if (_config == null)
                {
                    Debug.LogError("SaveConfig not found in Resources! Cannot initialize FirebaseAdapter.");
                    return new List<WorldMetadata>();
                }
            }

            if (_chunkManager == null)
            {
                _chunkManager = new ChunkManager(_config);
            }

            _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
        }

        if (!UserManager.IsInitialized)
        {
            await UserManager.InitializeUserIdAsync(_firebaseAdapter);
        }

        string userId = UserManager.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return new List<WorldMetadata>();
        }

        return await _firebaseAdapter.GetUserWorldsMetadataAsync(userId);
    }

    public bool DeleteLocalWorld(string worldName)
    {
        if (string.IsNullOrEmpty(worldName))
        {
            Debug.LogError("Delete failed: World name cannot be empty.");
            return false;
        }

        if (_fileManager == null)
        {
            _fileManager = new FileManager(_config);
        }

        bool deleted = _fileManager.DeleteSaveFile(worldName);

        if (deleted)
        {
            string screenshotPath = _config != null
                ? _config.GetWorldScreenshotPath(worldName)
                : Path.Combine(Application.persistentDataPath, $"{SaveConfig.SanitizeFileName(worldName)}.png");

            if (File.Exists(screenshotPath))
            {
                try
                {
                    File.Delete(screenshotPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to delete screenshot for '{worldName}': {e.Message}");
                }
            }
        }

        return deleted;
    }

    private async System.Threading.Tasks.Task<WorldSaveData> LoadWorldDataAsync(string worldName,
        WorldStorageSource source, Action<float> progressCallback)
    {
        switch (source)
        {
            case WorldStorageSource.LocalOnly:
                return await LoadLocalWorld(worldName, progressCallback);
            case WorldStorageSource.OnlineOnly:
                return await LoadOnlineWorld(worldName);
            default:
                WorldSaveData onlineWorld = null;

                if (_config.useFirebase)
                {
                    onlineWorld = await LoadOnlineWorld(worldName);
                    if (onlineWorld != null)
                    {
                        return onlineWorld;
                    }
                }

                if (_config.useLocalCache)
                {
                    return await LoadLocalWorld(worldName, progressCallback);
                }

                return onlineWorld;
        }
    }

    private async System.Threading.Tasks.Task<WorldSaveData> LoadLocalWorld(string worldName,
        Action<float> progressCallback)
    {
        if (_fileManager == null)
        {
            _fileManager = new FileManager(_config);
        }

        return await _fileManager.LoadWorldAsync(worldName, progressCallback);
    }

    private async System.Threading.Tasks.Task<WorldSaveData> LoadOnlineWorld(string worldName)
    {
        if (_firebaseAdapter == null)
        {
            if (_config == null)
            {
                _config = Resources.Load<SaveConfig>("SaveConfig");
                if (_config == null)
                {
                    Debug.LogError("SaveConfig not found when attempting to load online world.");
                    return null;
                }
            }

            if (_chunkManager == null)
            {
                _chunkManager = new ChunkManager(_config);
            }

            _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
        }

        return await _firebaseAdapter.LoadWorldFromFirestore(worldName);
    }

    public async System.Threading.Tasks.Task<bool> PublishLocalWorldToFirebaseAsync(string worldName)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Publish failed: World name cannot be empty.");
                return false;
            }

            if (!_config.useFirebase)
            {
                Debug.LogError("Publish failed: Firebase is disabled in configuration.");
                return false;
            }

            if (_fileManager == null)
            {
                _fileManager = new FileManager(_config);
            }

            WorldSaveData worldData = await _fileManager.LoadWorldAsync(worldName);
            if (worldData == null)
            {
                Debug.LogError($"Publish failed: Local world '{worldName}' not found.");
                return false;
            }

            if (_firebaseAdapter == null)
            {
                if (_chunkManager == null)
                {
                    _chunkManager = new ChunkManager(_config);
                }

                _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            }

            if (!UserManager.IsInitialized)
            {
                await UserManager.InitializeUserIdAsync(_firebaseAdapter);
            }

            string userId = UserManager.UserId;
            bool success = await _firebaseAdapter.SaveWorldToFirestore(worldData, userId);
            if (success)
            {
                Debug.Log($"World '{worldName}' published to Firebase successfully.");
            }
            else
            {
                Debug.LogError($"Failed to publish world '{worldName}' to Firebase.");
            }

            return success;
        }
        catch (Exception e)
        {
            Debug.LogError($"PublishLocalWorldToFirebaseAsync failed for '{worldName}': {e.Message}");
            return false;
        }
    }

    public async System.Threading.Tasks.Task<bool> IsWorldPublishedAsync(string worldName)
    {
        try
        {
            if (string.IsNullOrEmpty(worldName) || !_config.useFirebase)
            {
                return false;
            }

            if (_firebaseAdapter == null)
            {
                if (_config == null)
                {
                    _config = Resources.Load<SaveConfig>("SaveConfig");
                    if (_config == null)
                    {
                        return false;
                    }
                }

                if (_chunkManager == null)
                {
                    _chunkManager = new ChunkManager(_config);
                }

                _firebaseAdapter = new FirebaseAdapter(_config, _chunkManager);
            }

            WorldSaveData worldData = await _firebaseAdapter.LoadWorldFromFirestore(worldName);
            return worldData != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"IsWorldPublishedAsync failed for '{worldName}': {e.Message}");
            return false;
        }
    }
}


