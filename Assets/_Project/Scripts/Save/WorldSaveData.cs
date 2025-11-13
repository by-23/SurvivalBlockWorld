using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class WorldSaveData
{
    public string WorldName;
    public string ScreenshotPath;
    public Vector3Int WorldBoundsMin;
    public Vector3Int WorldBoundsMax;
    public long Timestamp;
    public Dictionary<Vector3Int, ChunkData> Chunks = new Dictionary<Vector3Int, ChunkData>();
    public int LikesCount;

    public WorldSaveData(string name, Vector3Int boundsMin, Vector3Int boundsMax)
    {
        WorldName = name;
        WorldBoundsMin = boundsMin;
        WorldBoundsMax = boundsMax;
        Timestamp = DateTime.UtcNow.Ticks;
        ScreenshotPath = string.Empty;
        LikesCount = 0;
    }

    public byte[] PackToBinary()
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write(WorldName ?? "");
            writer.Write(ScreenshotPath ?? "");
            writer.Write(WorldBoundsMin.x);
            writer.Write(WorldBoundsMin.y);
            writer.Write(WorldBoundsMin.z);
            writer.Write(WorldBoundsMax.x);
            writer.Write(WorldBoundsMax.y);
            writer.Write(WorldBoundsMax.z);
            writer.Write(Timestamp);
            writer.Write(Chunks.Count);

            foreach (var kvp in Chunks)
            {
                byte[] chunkData = kvp.Value.PackToBinary();
                writer.Write(chunkData.Length);
                writer.Write(chunkData);
            }

            writer.Write(LikesCount);

            return ms.ToArray();
        }
    }

    public static WorldSaveData UnpackFromBinary(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            string name = reader.ReadString();
            string screenshotPath = reader.ReadString();
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
                Timestamp = reader.ReadInt64(),
                ScreenshotPath = screenshotPath
            };

            int chunkCount = reader.ReadInt32();
            for (int i = 0; i < chunkCount; i++)
            {
                int chunkDataLength = reader.ReadInt32();
                byte[] chunkData = reader.ReadBytes(chunkDataLength);
                ChunkData chunk = ChunkData.UnpackFromBinary(chunkData);
                world.Chunks[chunk.chunkCoordinates] = chunk;
            }

            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                world.LikesCount = reader.ReadInt32();
            }

            return world;
        }
    }
}

