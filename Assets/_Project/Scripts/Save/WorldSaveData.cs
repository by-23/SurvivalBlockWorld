using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class WorldSaveData
{
    public string worldName;
    public Vector3Int worldBoundsMin;
    public Vector3Int worldBoundsMax;
    public long timestamp;
    public Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();

    public WorldSaveData(string name, Vector3Int boundsMin, Vector3Int boundsMax)
    {
        worldName = name;
        worldBoundsMin = boundsMin;
        worldBoundsMax = boundsMax;
        timestamp = DateTime.UtcNow.Ticks;
    }

    public byte[] PackToBinary()
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write(worldName ?? "");
            writer.Write(worldBoundsMin.x);
            writer.Write(worldBoundsMin.y);
            writer.Write(worldBoundsMin.z);
            writer.Write(worldBoundsMax.x);
            writer.Write(worldBoundsMax.y);
            writer.Write(worldBoundsMax.z);
            writer.Write(timestamp);
            writer.Write(chunks.Count);

            foreach (var kvp in chunks)
            {
                byte[] chunkData = kvp.Value.PackToBinary();
                writer.Write(chunkData.Length);
                writer.Write(chunkData);
            }

            return ms.ToArray();
        }
    }

    public static WorldSaveData UnpackFromBinary(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            string name = reader.ReadString();
            Vector3Int boundsMin = new Vector3Int(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );
            Vector3Int boundsMax = new Vector3Int(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );

            WorldSaveData world = new WorldSaveData(name, boundsMin, boundsMax)
            {
                timestamp = reader.ReadInt64()
            };

            int chunkCount = reader.ReadInt32();
            for (int i = 0; i < chunkCount; i++)
            {
                int chunkDataLength = reader.ReadInt32();
                byte[] chunkData = reader.ReadBytes(chunkDataLength);
                ChunkData chunk = ChunkData.UnpackFromBinary(chunkData);
                world.chunks[chunk.chunkCoordinates] = chunk;
            }

            return world;
        }
    }
}

