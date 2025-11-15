using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;

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

            var worldMetadata = new Dictionary<string, object>
            {
                { "worldName", worldData.WorldName },
                { "screenshotPath", worldData.ScreenshotPath },
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
            Debug.Log($"World metadata for '{worldData.WorldName}' saved to Firestore.");

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