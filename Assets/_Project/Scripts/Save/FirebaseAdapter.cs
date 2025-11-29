using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Storage;

public class FirebaseAdapter
{
    private readonly SaveConfig _config;
    private readonly ChunkManager _chunkManager;
    private readonly FirebaseFirestore _db;

    public FirebaseAdapter(SaveConfig config, ChunkManager chunkManager)
    {
        _config = config;
        _chunkManager = chunkManager;
        _db = FirebaseFirestore.DefaultInstance;
    }

    public async Task<bool> SaveWorldToFirestore(WorldSaveData worldData, string userId = null)
    {
        try
        {
            DocumentReference worldRef = _db.Collection("worlds").Document(worldData.WorldName);

            DocumentSnapshot existingSnapshot = await worldRef.GetSnapshotAsync();

            int likesValue = 0;
            if (existingSnapshot.Exists && existingSnapshot.TryGetValue("likes", out long existingLikes))
            {
                likesValue = (int)Mathf.Max(0, existingLikes);
            }

            string existingUserId = string.Empty;
            if (existingSnapshot.Exists && existingSnapshot.TryGetValue("userId", out string existingUserIdValue))
            {
                existingUserId = existingUserIdValue;
            }

            string screenshotUrl = string.Empty;
            if (!string.IsNullOrEmpty(worldData.ScreenshotPath))
            {
                if (worldData.ScreenshotPath.StartsWith("http://") || worldData.ScreenshotPath.StartsWith("https://"))
                {
                    screenshotUrl = worldData.ScreenshotPath;
                }
                else if (File.Exists(worldData.ScreenshotPath))
                {
                    screenshotUrl =
                        await UploadScreenshotToStorageAsync(worldData.WorldName, worldData.ScreenshotPath,
                            "world_screenshots");
                    if (string.IsNullOrEmpty(screenshotUrl))
                    {
                        screenshotUrl = worldData.ScreenshotPath;
                    }
                }
            }

            var worldMetadata = new Dictionary<string, object>
            {
                { "worldName", worldData.WorldName },
                { "screenshotPath", screenshotUrl },
                { "worldBoundsMinX", worldData.WorldBoundsMin.x },
                { "worldBoundsMinY", worldData.WorldBoundsMin.y },
                { "worldBoundsMinZ", worldData.WorldBoundsMin.z },
                { "worldBoundsMaxX", worldData.WorldBoundsMax.x },
                { "worldBoundsMaxY", worldData.WorldBoundsMax.y },
                { "worldBoundsMaxZ", worldData.WorldBoundsMax.z },
                { "timestamp", worldData.Timestamp },
                { "likes", likesValue }
            };

            if (!string.IsNullOrEmpty(userId))
            {
                worldMetadata["userId"] = userId;
            }
            else if (!string.IsNullOrEmpty(existingUserId))
            {
                worldMetadata["userId"] = existingUserId;
            }

            await worldRef.SetAsync(worldMetadata, SetOptions.MergeAll);

            foreach (var chunk in worldData.Chunks.Values)
            {
                await SaveChunkToFirestore(worldData.WorldName, chunk);
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving world '{worldData.WorldName}' to Firestore: {e.Message}");
            return false;
        }
    }

    private async Task<bool> SaveChunkToFirestore(string worldId, ChunkData chunk)
    {
        try
        {
            // Используем Base64 для компактного хранения данных чанка
            string base64Data = chunk.ToBase64();
            string chunkId = $"chunk_{chunk.chunkCoordinates.x}_{chunk.chunkCoordinates.y}_{chunk.chunkCoordinates.z}";

            DocumentReference chunkRef = _db.Collection("worlds").Document(worldId)
                .Collection("chunks").Document(chunkId);

            var data = new Dictionary<string, object>
            {
                { "data", base64Data },
                { "timestamp", Timestamp.GetCurrentTimestamp() }
            };

            await chunkRef.SetAsync(data);
            Debug.Log($"Chunk {chunkId} for world '{worldId}' saved to Firestore.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"Error saving chunk {chunk.chunkCoordinates} for world '{worldId}' to Firestore: {e.Message}");
            return false;
        }
    }

    public async Task<ChunkData> LoadChunkFromFirestore(string worldId, Vector3Int chunkCoord)
    {
        try
        {
            string chunkId = $"chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}";
            DocumentReference chunkRef = _db.Collection("worlds").Document(worldId)
                .Collection("chunks").Document(chunkId);

            DocumentSnapshot snapshot = await chunkRef.GetSnapshotAsync();

            if (snapshot.Exists)
            {
                string base64Data = snapshot.GetValue<string>("data");
                Debug.Log($"Chunk {chunkId} loaded from Firestore.");
                return ChunkData.FromBase64(base64Data);
            }

            Debug.LogWarning($"Chunk {chunkId} not found in Firestore for world '{worldId}'.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading chunk {chunkCoord} from Firestore for world '{worldId}': {e.Message}");
            return null;
        }
    }

    public async Task<WorldSaveData> LoadWorldFromFirestore(string worldId)
    {
        try
        {
            DocumentReference worldRef = _db.Collection("worlds").Document(worldId);
            DocumentSnapshot worldSnapshot = await worldRef.GetSnapshotAsync();

            if (!worldSnapshot.Exists)
            {
                Debug.LogWarning($"World '{worldId}' not found in Firestore.");
                return null;
            }

            Vector3Int boundsMin = new Vector3Int(
                worldSnapshot.GetValue<int>("worldBoundsMinX"),
                worldSnapshot.GetValue<int>("worldBoundsMinY"),
                worldSnapshot.GetValue<int>("worldBoundsMinZ")
            );
            Vector3Int boundsMax = new Vector3Int(
                worldSnapshot.GetValue<int>("worldBoundsMaxX"),
                worldSnapshot.GetValue<int>("worldBoundsMaxY"),
                worldSnapshot.GetValue<int>("worldBoundsMaxZ")
            );

            string worldNameFromField = worldSnapshot.TryGetValue("worldName", out string worldNameValue)
                ? worldNameValue
                : null;

            string actualWorldName = !string.IsNullOrEmpty(worldNameFromField) ? worldNameFromField : worldId;

            WorldSaveData worldData = new WorldSaveData(actualWorldName, boundsMin, boundsMax)
            {
                ScreenshotPath = worldSnapshot.GetValue<string>("screenshotPath"),
                Timestamp = worldSnapshot.GetValue<long>("timestamp")
            };

            if (worldSnapshot.TryGetValue("likes", out long likes))
            {
                worldData.LikesCount = (int)Mathf.Max(0, likes);
            }

            if (worldSnapshot.TryGetValue("userId", out string userId))
            {
                worldData.CreatorId = userId;
            }

            QuerySnapshot snapshot = await _db.Collection("worlds").Document(worldId)
                .Collection("chunks").GetSnapshotAsync();

            if (snapshot.Count > 0)
            {
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    string base64Data = document.GetValue<string>("data");
                    ChunkData chunk = ChunkData.FromBase64(base64Data);
                    worldData.Chunks[chunk.chunkCoordinates] = chunk;
                }
            }

            return worldData;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load world '{worldId}' from Firestore: {e.Message}");
            return null;
        }
    }

    public async Task<List<WorldMetadata>> GetAllWorldsMetadata()
    {
        try
        {
            QuerySnapshot snapshot = await _db.Collection("worlds").GetSnapshotAsync();
            List<WorldMetadata> worlds = new List<WorldMetadata>();

            foreach (DocumentSnapshot document in snapshot.Documents)
            {
                try
                {
                    string documentId = document.Id;
                    string worldNameFromField = document.TryGetValue("worldName", out string worldNameValue)
                        ? worldNameValue
                        : null;

                    string actualWorldName =
                        !string.IsNullOrEmpty(worldNameFromField) ? worldNameFromField : documentId;

                    WorldMetadata metadata = new WorldMetadata
                    {
                        WorldName = actualWorldName,
                        ScreenshotPath = document.GetValue<string>("screenshotPath"),
                        Timestamp = document.GetValue<long>("timestamp"),
                        Likes = document.TryGetValue("likes", out long likes)
                            ? (int)Mathf.Max(0, likes)
                            : 0,
                        UserId = document.TryGetValue("userId", out string docUserId)
                            ? docUserId
                            : string.Empty
                    };
                    worlds.Add(metadata);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing world metadata for {document.Id}: {e.Message}");
                }
            }

            return worlds;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get worlds metadata: {e.Message}");
            return new List<WorldMetadata>();
        }
    }

    public async Task<bool> DeleteWorldFromFirestore(string worldName, string userId = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(userId))
            {
                string ownerId = await GetWorldOwnerIdAsync(worldName);
                if (string.IsNullOrEmpty(ownerId) || ownerId != userId)
                {
                    Debug.LogWarning($"User '{userId}' is not the owner of world '{worldName}'. Deletion denied.");
                    return false;
                }
            }

            DocumentReference worldRef = _db.Collection("worlds").Document(worldName);

            // Сначала удаляем все чанки
            QuerySnapshot chunksSnapshot = await worldRef.Collection("chunks").GetSnapshotAsync();
            foreach (DocumentSnapshot chunkDoc in chunksSnapshot.Documents)
            {
                await chunkDoc.Reference.DeleteAsync();
            }

            // Затем удаляем сам документ мира
            await worldRef.DeleteAsync();

            Debug.Log($"World '{worldName}' deleted from Firestore.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deleting world '{worldName}' from Firestore: {e.Message}");
            return false;
        }
    }

    // Метод SyncDelta оставлен для будущей реализации, если потребуется синхронизация только изменений
    public async Task<bool> SyncDelta(CubeChange[] changes)
    {
        Debug.LogWarning("Delta sync not implemented.");
        await Task.Delay(100);
        return false;
    }

    public async Task<bool> UpdateWorldLikes(string worldName, int likesCount)
    {
        try
        {
            DocumentReference worldRef = _db.Collection("worlds").Document(worldName);
            var payload = new Dictionary<string, object>
            {
                { "likes", Mathf.Max(0, likesCount) },
                { "likesUpdatedAt", Timestamp.GetCurrentTimestamp() }
            };

            await worldRef.SetAsync(payload, SetOptions.MergeAll);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating likes for world '{worldName}' in Firestore: {e.Message}");
            return false;
        }
    }

    public async Task<bool> RegisterUserIdAsync(string userId)
    {
        try
        {
            DocumentReference userRef = _db.Collection("users").Document(userId);
            var userData = new Dictionary<string, object>
            {
                { "registeredAt", Timestamp.GetCurrentTimestamp() }
            };

            await userRef.SetAsync(userData);
            Debug.Log($"User ID '{userId}' registered in Firestore.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error registering user ID '{userId}' in Firestore: {e.Message}");
            return false;
        }
    }

    public async Task<bool> IsUserIdAvailableAsync(string userId)
    {
        try
        {
            DocumentReference userRef = _db.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();
            return !snapshot.Exists;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking user ID availability '{userId}': {e.Message}");
            return false;
        }
    }

    public async Task<List<WorldMetadata>> GetUserWorldsMetadataAsync(string userId)
    {
        try
        {
            QuerySnapshot snapshot = await _db.Collection("worlds")
                .WhereEqualTo("userId", userId)
                .GetSnapshotAsync();

            List<WorldMetadata> worlds = new List<WorldMetadata>();

            foreach (DocumentSnapshot document in snapshot.Documents)
            {
                try
                {
                    string documentId = document.Id;
                    string worldNameFromField = document.TryGetValue("worldName", out string worldNameValue)
                        ? worldNameValue
                        : null;

                    string actualWorldName =
                        !string.IsNullOrEmpty(worldNameFromField) ? worldNameFromField : documentId;

                    WorldMetadata metadata = new WorldMetadata
                    {
                        WorldName = actualWorldName,
                        ScreenshotPath = document.GetValue<string>("screenshotPath"),
                        Timestamp = document.GetValue<long>("timestamp"),
                        Likes = document.TryGetValue("likes", out long likes)
                            ? (int)Mathf.Max(0, likes)
                            : 0,
                        UserId = document.TryGetValue("userId", out string docUserId)
                            ? docUserId
                            : string.Empty
                    };
                    worlds.Add(metadata);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing world metadata for {document.Id}: {e.Message}");
                }
            }

            return worlds;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get user worlds metadata: {e.Message}");
            return new List<WorldMetadata>();
        }
    }

    public async Task<string> GetWorldOwnerIdAsync(string worldName)
    {
        try
        {
            DocumentReference worldRef = _db.Collection("worlds").Document(worldName);
            DocumentSnapshot snapshot = await worldRef.GetSnapshotAsync();

            if (snapshot.Exists && snapshot.TryGetValue("userId", out string userId))
            {
                return userId;
            }

            return string.Empty;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting world owner ID for '{worldName}': {e.Message}");
            return string.Empty;
        }
    }

    public async Task<HashSet<string>> GetUserLikedWorldsAsync(string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
            {
                return new HashSet<string>();
            }

            DocumentReference userRef = _db.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();

            if (snapshot.Exists && snapshot.TryGetValue("likedWorlds", out List<string> likedWorlds))
            {
                return new HashSet<string>(likedWorlds ?? new List<string>());
            }

            return new HashSet<string>();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting liked worlds for user '{userId}': {e.Message}");
            return new HashSet<string>();
        }
    }

    public async Task<bool> AddLikedWorldAsync(string userId, string worldName)
    {
        try
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(worldName))
            {
                return false;
            }

            DocumentReference userRef = _db.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();

            HashSet<string> likedWorlds = new HashSet<string>();
            if (snapshot.Exists && snapshot.TryGetValue("likedWorlds", out List<string> existingLikedWorlds))
            {
                likedWorlds = new HashSet<string>(existingLikedWorlds ?? new List<string>());
            }

            if (likedWorlds.Contains(worldName))
            {
                return true;
            }

            likedWorlds.Add(worldName);

            var userData = new Dictionary<string, object>
            {
                { "likedWorlds", likedWorlds.ToList() },
                { "lastUpdated", Timestamp.GetCurrentTimestamp() }
            };

            await userRef.SetAsync(userData, SetOptions.MergeAll);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error adding liked world '{worldName}' for user '{userId}': {e.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveLikedWorldAsync(string userId, string worldName)
    {
        try
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(worldName))
            {
                return false;
            }

            DocumentReference userRef = _db.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();

            HashSet<string> likedWorlds = new HashSet<string>();
            if (snapshot.Exists && snapshot.TryGetValue("likedWorlds", out List<string> existingLikedWorlds))
            {
                likedWorlds = new HashSet<string>(existingLikedWorlds ?? new List<string>());
            }

            if (!likedWorlds.Contains(worldName))
            {
                return true;
            }

            likedWorlds.Remove(worldName);

            var userData = new Dictionary<string, object>
            {
                { "likedWorlds", likedWorlds.ToList() },
                { "lastUpdated", Timestamp.GetCurrentTimestamp() }
            };

            await userRef.SetAsync(userData, SetOptions.MergeAll);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error removing liked world '{worldName}' for user '{userId}': {e.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateWorldLikesWithUser(string worldName, string userId, bool isLiking)
    {
        try
        {
            DocumentReference worldRef = _db.Collection("worlds").Document(worldName);
            DocumentSnapshot worldSnapshot = await worldRef.GetSnapshotAsync();

            if (!worldSnapshot.Exists)
            {
                Debug.LogError($"World '{worldName}' not found.");
                return false;
            }

            int currentLikes = 0;
            if (worldSnapshot.TryGetValue("likes", out long likes))
            {
                currentLikes = (int)Mathf.Max(0, likes);
            }

            int newLikesCount = isLiking ? currentLikes + 1 : Mathf.Max(0, currentLikes - 1);

            var payload = new Dictionary<string, object>
            {
                { "likes", newLikesCount },
                { "likesUpdatedAt", Timestamp.GetCurrentTimestamp() }
            };

            await worldRef.SetAsync(payload, SetOptions.MergeAll);

            if (isLiking)
            {
                await AddLikedWorldAsync(userId, worldName);
            }
            else
            {
                await RemoveLikedWorldAsync(userId, worldName);
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating likes for world '{worldName}' with user '{userId}': {e.Message}");
            return false;
        }
    }

    public async Task<bool> SaveSharedEntityAsync(string entityId, CubeData[] cubes, string screenshotId)
    {
        try
        {
            if (cubes == null || cubes.Length == 0)
            {
                Debug.LogWarning($"[FirebaseAdapter] Cannot save entity '{entityId}': no cubes data");
                return false;
            }

            DocumentReference entityRef = _db.Collection("sharedEntities").Document(entityId);

            string finalScreenshotId = string.Empty;
            if (!string.IsNullOrEmpty(screenshotId))
            {
                // Если это уже URL, просто сохраняем его
                if (screenshotId.StartsWith("http://") || screenshotId.StartsWith("https://"))
                {
                    finalScreenshotId = screenshotId;
                }
                else
                {
                    // Пытаемся найти локальный путь через ScreenshotManager и загрузить в Firebase
                    var screenshotManager =
                        UnityEngine.Object.FindAnyObjectByType<Assets._Project.Scripts.UI.ScreenshotManager>();
                    if (screenshotManager != null &&
                        screenshotManager.TryGetPath(screenshotId, out string screenshotPath) &&
                        !string.IsNullOrEmpty(screenshotPath) &&
                        File.Exists(screenshotPath))
                    {
                        Debug.Log(
                            $"[FirebaseAdapter] Найден локальный путь к скриншоту для entity '{entityId}': {screenshotPath}");
                        string uploadedUrl =
                            await UploadScreenshotToStorageAsync(entityId, screenshotPath, "entity_screenshots");
                        if (string.IsNullOrEmpty(uploadedUrl))
                        {
                            Debug.LogWarning(
                                $"[FirebaseAdapter] Не удалось загрузить скриншот в Storage для entity '{entityId}', сохраняем исходный id");
                            finalScreenshotId = screenshotId;
                        }
                        else
                        {
                            Debug.Log(
                                $"[FirebaseAdapter] Скриншот загружен в Storage для entity '{entityId}'. Url='{uploadedUrl}'");
                            finalScreenshotId = uploadedUrl;
                        }
                    }
                    else
                    {
                        // Фолбэк — сохраняем как есть (на случай, если это уже какой-то внешний id)
                        Debug.LogWarning(
                            $"[FirebaseAdapter] Не удалось получить локальный путь скриншота по id '{screenshotId}' для entity '{entityId}', сохраняем id как есть");
                        finalScreenshotId = screenshotId;
                    }
                }
            }

            var entityData = new Dictionary<string, object>
            {
                { "screenshotId", finalScreenshotId ?? string.Empty },
                { "timestamp", Timestamp.GetCurrentTimestamp() }
            };

            await entityRef.SetAsync(entityData);

            // Упаковываем все кубы в один base64-поле, чтобы не плодить сотни документов
            string packedCubesBase64;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(cubes.Length);
                for (int i = 0; i < cubes.Length; i++)
                {
                    cubes[i].WriteTo(writer);
                }

                writer.Flush();
                packedCubesBase64 = Convert.ToBase64String(ms.ToArray());
            }

            DocumentReference packedRef = entityRef.Collection("data").Document("cubes");
            var packedDoc = new Dictionary<string, object>
            {
                { "data", packedCubesBase64 },
                { "timestamp", Timestamp.GetCurrentTimestamp() }
            };
            await packedRef.SetAsync(packedDoc);

            Debug.Log($"Shared entity '{entityId}' saved to Firestore (packed {cubes.Length} cubes).");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving shared entity '{entityId}' to Firestore: {e.Message}");
            return false;
        }
    }

    public async Task<SharedEntityData> LoadSharedEntityAsync(string entityId)
    {
        try
        {
            DocumentReference entityRef = _db.Collection("sharedEntities").Document(entityId);
            DocumentSnapshot entitySnapshot = await entityRef.GetSnapshotAsync();

            if (!entitySnapshot.Exists)
            {
                Debug.LogWarning($"Shared entity '{entityId}' not found in Firestore.");
                return null;
            }

            string screenshotId = entitySnapshot.TryGetValue("screenshotId", out string ssId) ? ssId : string.Empty;

            List<CubeData> cubes = new List<CubeData>();

            // 1) Пытаемся загрузить новый упакованный формат
            DocumentReference packedRef = entityRef.Collection("data").Document("cubes");
            DocumentSnapshot packedSnapshot = await packedRef.GetSnapshotAsync();
            if (packedSnapshot.Exists && packedSnapshot.TryGetValue("data", out string packedBase64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(packedBase64);
                    using (var ms = new MemoryStream(bytes))
                    using (var reader = new BinaryReader(ms))
                    {
                        int count = reader.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            cubes.Add(CubeData.ReadFrom(reader));
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse packed shared entity '{entityId}': {e.Message}");
                    cubes.Clear();
                }
            }

            // 2) Фолбэк на старый формат (подколлекция 'cubes'), чтобы не сломать уже сохранённые данные
            if (cubes.Count == 0)
            {
                QuerySnapshot cubesSnapshot = await entityRef.Collection("cubes").GetSnapshotAsync();
                foreach (DocumentSnapshot cubeDoc in cubesSnapshot.Documents)
                {
                    Vector3 cubePos = new Vector3(
                        cubeDoc.GetValue<float>("positionX"),
                        cubeDoc.GetValue<float>("positionY"),
                        cubeDoc.GetValue<float>("positionZ")
                    );

                    Color32 cubeColor = new Color32(
                        (byte)Mathf.Clamp(cubeDoc.GetValue<float>("colorR") * 255f, 0, 255),
                        (byte)Mathf.Clamp(cubeDoc.GetValue<float>("colorG") * 255f, 0, 255),
                        (byte)Mathf.Clamp(cubeDoc.GetValue<float>("colorB") * 255f, 0, 255),
                        (byte)Mathf.Clamp(cubeDoc.GetValue<float>("colorA") * 255f, 0, 255)
                    );

                    byte blockTypeId = (byte)cubeDoc.GetValue<int>("blockTypeId");
                    int cubeEntityId = cubeDoc.TryGetValue("entityId", out int eId) ? eId : 0;

                    Quaternion cubeRotation = Quaternion.identity;
                    if (cubeDoc.TryGetValue("rotationX", out float rx) &&
                        cubeDoc.TryGetValue("rotationY", out float ry) &&
                        cubeDoc.TryGetValue("rotationZ", out float rz) &&
                        cubeDoc.TryGetValue("rotationW", out float rw))
                    {
                        cubeRotation = new Quaternion(rx, ry, rz, rw);
                    }

                    cubes.Add(new CubeData(cubePos, cubeColor, blockTypeId, cubeEntityId, cubeRotation));
                }
            }

            return new SharedEntityData
            {
                EntityId = entityId,
                Cubes = cubes.ToArray(),
                ScreenshotId = screenshotId
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading shared entity '{entityId}' from Firestore: {e.Message}");
            return null;
        }
    }

    public async Task<List<SharedEntityMetadata>> GetAllSharedEntitiesMetadataAsync()
    {
        try
        {
            QuerySnapshot snapshot = await _db.Collection("sharedEntities").GetSnapshotAsync();
            List<SharedEntityMetadata> entities = new List<SharedEntityMetadata>();

            foreach (DocumentSnapshot document in snapshot.Documents)
            {
                try
                {
                    string entityId = document.Id;
                    string screenshotId = document.TryGetValue("screenshotId", out string ssId) ? ssId : string.Empty;
                    long timestamp = document.TryGetValue("timestamp", out Timestamp ts) ? ts.ToDateTime().Ticks : 0;

                    entities.Add(new SharedEntityMetadata
                    {
                        EntityId = entityId,
                        ScreenshotId = screenshotId,
                        Timestamp = timestamp
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing shared entity metadata for {document.Id}: {e.Message}");
                }
            }

            return entities;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get shared entities metadata: {e.Message}");
            return new List<SharedEntityMetadata>();
        }
    }

    public async Task<bool> DeleteSharedEntityAsync(string entityId)
    {
        try
        {
            DocumentReference entityRef = _db.Collection("sharedEntities").Document(entityId);

            // Удаляем новый упакованный документ (если есть)
            DocumentReference packedRef = entityRef.Collection("data").Document("cubes");
            await packedRef.DeleteAsync();

            // Для обратной совместимости пробуем удалить старый формат (подколлекция 'cubes'),
            // если он ещё существует. Это может занять время только для старых данных.
            QuerySnapshot cubesSnapshot = await entityRef.Collection("cubes").GetSnapshotAsync();
            foreach (DocumentSnapshot cubeDoc in cubesSnapshot.Documents)
            {
                await cubeDoc.Reference.DeleteAsync();
            }

            await entityRef.DeleteAsync();

            Debug.Log($"Shared entity '{entityId}' deleted from Firestore.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deleting shared entity '{entityId}' from Firestore: {e.Message}");
            return false;
        }
    }

    private async Task<string> UploadScreenshotToStorageAsync(string objectId, string localFilePath, string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(localFilePath))
            {
                return string.Empty;
            }

            int maxRetries = 10;
            int retryDelay = 100;
            for (int i = 0; i < maxRetries; i++)
            {
                if (File.Exists(localFilePath))
                {
                    try
                    {
                        using (var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (fs.Length > 0)
                            {
                                break;
                            }
                        }
                    }
                    catch (IOException)
                    {
                        if (i < maxRetries - 1)
                        {
                            await Task.Delay(retryDelay);
                            continue;
                        }
                    }
                }

                if (i < maxRetries - 1)
                {
                    await Task.Delay(retryDelay);
                }
            }

            if (!File.Exists(localFilePath))
            {
                return string.Empty;
            }

            byte[] fileBytes;
            try
            {
                fileBytes = await File.ReadAllBytesAsync(localFilePath);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                return string.Empty;
            }

            var firebaseApp = Firebase.FirebaseApp.DefaultInstance;
            if (firebaseApp == null)
            {
                return string.Empty;
            }

            FirebaseStorage storage = FirebaseStorage.DefaultInstance;
            if (storage == null)
            {
                return string.Empty;
            }

            string sanitizedId = SaveConfig.SanitizeFileName(objectId);
            string storagePath = $"{folder}/{sanitizedId}.png";

            string expectedBucket = firebaseApp.Options?.StorageBucket;
            if (string.IsNullOrEmpty(expectedBucket))
            {
                return string.Empty;
            }

            StorageReference screenshotRef;
            try
            {
                screenshotRef = storage.GetReference(storagePath);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            try
            {
                MetadataChange metadata = new MetadataChange();
                metadata.ContentType = "image/png";

                StorageMetadata result = await screenshotRef.PutBytesAsync(fileBytes, metadata);
                if (result == null)
                {
                    return string.Empty;
                }

                await Task.Delay(500);

                Uri downloadUri = null;
                int urlRetries = 5;
                for (int i = 0; i < urlRetries; i++)
                {
                    try
                    {
                        downloadUri = await screenshotRef.GetDownloadUrlAsync();
                        if (downloadUri != null)
                        {
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        if (i < urlRetries - 1)
                        {
                            await Task.Delay(1000 * (i + 1));
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                string downloadUrl = downloadUri?.ToString() ?? string.Empty;
                return downloadUrl;
            }
            catch (StorageException)
            {
                return string.Empty;
            }
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

[Serializable]
public class WorldMetadata
{
    public string WorldName;
    public string ScreenshotPath;
    public long Timestamp;
    public int Likes;
    public string UserId;
}

[Serializable]
public class SharedEntityData
{
    public string EntityId;
    public CubeData[] Cubes;
    public string ScreenshotId;
}

[Serializable]
public class SharedEntityMetadata
{
    public string EntityId;
    public string ScreenshotId;
    public long Timestamp;
}

[Serializable]
public struct CubeChange
{
    public enum ChangeType
    {
        Add,
        Remove,
        Update
    }

    public ChangeType Type;
    public Vector3 Position;
    public CubeData Data;
    public long Timestamp;

    public CubeChange(ChangeType changeType, CubeData cubeData)
    {
        Type = changeType;
        Position = cubeData.Position;
        Data = cubeData;
        Timestamp = DateTime.UtcNow.Ticks;
    }
}