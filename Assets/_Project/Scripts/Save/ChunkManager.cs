using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private SaveConfig config;
    private HashSet<Vector3Int> dirtyChunks = new HashSet<Vector3Int>();

    public ChunkManager(SaveConfig config)
    {
        this.config = config;
    }

    public Vector3Int GetChunkCoordinates(Vector3 worldPosition)
    {
        int chunkSize = config.chunkSize;
        return new Vector3Int(
            Mathf.FloorToInt(worldPosition.x / chunkSize),
            Mathf.FloorToInt(worldPosition.y / chunkSize),
            Mathf.FloorToInt(worldPosition.z / chunkSize)
        );
    }

    public Vector3 GetChunkWorldPosition(Vector3Int chunkCoordinates)
    {
        int chunkSize = config.chunkSize;
        return new Vector3(
            chunkCoordinates.x * chunkSize,
            chunkCoordinates.y * chunkSize,
            chunkCoordinates.z * chunkSize
        );
    }

    public void MarkChunkDirty(Vector3Int chunkCoord)
    {
        dirtyChunks.Add(chunkCoord);
    }

    public void MarkChunkDirty(Vector3 worldPosition)
    {
        MarkChunkDirty(GetChunkCoordinates(worldPosition));
    }

    public HashSet<Vector3Int> GetDirtyChunks()
    {
        return new HashSet<Vector3Int>(dirtyChunks);
    }

    public void ClearDirtyChunks()
    {
        dirtyChunks.Clear();
    }

    public bool IsChunkDirty(Vector3Int chunkCoord)
    {
        return dirtyChunks.Contains(chunkCoord);
    }

    public Dictionary<Vector3Int, ChunkData> OrganizeCubesIntoChunks(List<CubeData> allCubes)
    {
        Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();

        foreach (var cubeData in allCubes)
        {
            Vector3Int chunkCoord = GetChunkCoordinates(cubeData.Position);

            if (!chunks.ContainsKey(chunkCoord))
            {
                chunks[chunkCoord] = new ChunkData(chunkCoord);
            }

            chunks[chunkCoord].cubes.Add(cubeData);
        }

        return chunks;
    }
}

