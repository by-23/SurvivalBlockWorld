using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

public class FirebaseAdapter
{
    private SaveConfig config;
    private ChunkManager chunkManager;
    private FirebaseFirestore db;

    public FirebaseAdapter(SaveConfig config, ChunkManager chunkManager)
    {
        this.config = config;
        this.chunkManager = chunkManager;
        db = FirebaseFirestore.DefaultInstance;
    }

    public async Task<bool> SaveChunkToFirestore(ChunkData chunk)
    {
        try
        {
            // Используем Base64 для компактного хранения данных чанка
            string base64Data = chunk.ToBase64();
            string chunkId = $"chunk_{chunk.chunkCoordinates.x}_{chunk.chunkCoordinates.y}_{chunk.chunkCoordinates.z}";

            DocumentReference chunkRef = db.Collection("worlds").Document("MainWorld")
                .Collection("chunks").Document(chunkId);

            var data = new Dictionary<string, object>
            {
                { "data", base64Data },
                { "timestamp", Timestamp.GetCurrentTimestamp() }
            };

            await chunkRef.SetAsync(data);
            Debug.Log($"Chunk {chunkId} saved to Firestore.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving chunk {chunk.chunkCoordinates} to Firestore: {e.Message}");
            return false;
        }
    }

    public async Task<ChunkData> LoadChunkFromFirestore(Vector3Int chunkCoord)
    {
        try
        {
            string chunkId = $"chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}";
            DocumentReference chunkRef = db.Collection("worlds").Document("MainWorld")
                .Collection("chunks").Document(chunkId);

            DocumentSnapshot snapshot = await chunkRef.GetSnapshotAsync();

            if (snapshot.Exists)
            {
                string base64Data = snapshot.GetValue<string>("data");
                Debug.Log($"Chunk {chunkId} loaded from Firestore.");
                return ChunkData.FromBase64(base64Data);
            }

            Debug.LogWarning($"Chunk {chunkId} not found in Firestore.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading chunk {chunkCoord} from Firestore: {e.Message}");
            return null;
        }
    }

    public async Task<WorldSaveData> LoadWorldFromFirestore(string worldId)
    {
        try
        {
            QuerySnapshot snapshot = await db.Collection("worlds").Document(worldId)
                .Collection("chunks").GetSnapshotAsync();

            if (snapshot.Count > 0)
            {
                WorldSaveData worldData = new WorldSaveData(worldId, config.worldBoundsMin, config.worldBoundsMax);

                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    string base64Data = document.GetValue<string>("data");
                    ChunkData chunk = ChunkData.FromBase64(base64Data);
                    worldData.chunks[chunk.chunkCoordinates] = chunk;
                }

                Debug.Log($"World '{worldId}' with {worldData.chunks.Count} chunks loaded from Firestore.");
                return worldData;
            }

            Debug.LogWarning($"World '{worldId}' not found or has no chunks in Firestore.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load world '{worldId}' from Firestore: {e.Message}");
            return null;
        }
    }

    // Метод SyncDelta оставлен для будущей реализации, если потребуется синхронизация только изменений
    public async Task<bool> SyncDelta(CubeChange[] changes)
    {
        Debug.LogWarning("Delta sync not implemented.");
        await Task.Delay(100);
        return false;
    }
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

    public ChangeType type;
    public Vector3 position;
    public CubeData data;
    public long timestamp;

    public CubeChange(ChangeType changeType, CubeData cubeData)
    {
        type = changeType;
        position = cubeData.Position;
        data = cubeData;
        timestamp = DateTime.UtcNow.Ticks;
    }
}