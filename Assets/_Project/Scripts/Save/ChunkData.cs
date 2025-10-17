using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class ChunkData
{
    public Vector3Int chunkCoordinates;
    public List<CubeData> cubes = new List<CubeData>();

    public ChunkData(Vector3Int coordinates)
    {
        chunkCoordinates = coordinates;
    }

    public byte[] PackToBinary()
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write(chunkCoordinates.x);
            writer.Write(chunkCoordinates.y);
            writer.Write(chunkCoordinates.z);
            writer.Write(cubes.Count);

            foreach (var cube in cubes)
            {
                cube.WriteTo(writer);
            }

            return ms.ToArray();
        }
    }

    public static ChunkData UnpackFromBinary(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            Vector3Int coords = new Vector3Int(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );

            ChunkData chunk = new ChunkData(coords);
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                chunk.cubes.Add(CubeData.ReadFrom(reader));
            }

            return chunk;
        }
    }

    public string ToBase64()
    {
        return Convert.ToBase64String(PackToBinary());
    }

    public static ChunkData FromBase64(string base64)
    {
        byte[] data = Convert.FromBase64String(base64);
        return UnpackFromBinary(data);
    }
}

