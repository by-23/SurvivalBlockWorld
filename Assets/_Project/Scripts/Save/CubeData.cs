using System;
using System.IO;
using UnityEngine;

[Serializable]
public struct CubeData
{
    public short x;
    public short y;
    public short z;
    public byte r;
    public byte g;
    public byte b;
    public byte a;
    public byte blockTypeId;

    private const float POSITION_SCALE = 100f;

    public CubeData(Vector3 position, Color32 color, byte typeId)
    {
        x = (short)Mathf.RoundToInt(position.x * POSITION_SCALE);
        y = (short)Mathf.RoundToInt(position.y * POSITION_SCALE);
        z = (short)Mathf.RoundToInt(position.z * POSITION_SCALE);
        r = color.r;
        g = color.g;
        b = color.b;
        a = color.a;
        blockTypeId = typeId;
    }

    public Vector3 Position => new Vector3(x / POSITION_SCALE, y / POSITION_SCALE, z / POSITION_SCALE);
    public Color32 Color => new Color32(r, g, b, a);

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        writer.Write(r);
        writer.Write(g);
        writer.Write(b);
        writer.Write(a);
        writer.Write(blockTypeId);
    }

    public static CubeData ReadFrom(BinaryReader reader)
    {
        return new CubeData
        {
            x = reader.ReadInt16(),
            y = reader.ReadInt16(),
            z = reader.ReadInt16(),
            r = reader.ReadByte(),
            g = reader.ReadByte(),
            b = reader.ReadByte(),
            a = reader.ReadByte(),
            blockTypeId = reader.ReadByte()
        };
    }

    public const int SIZE_BYTES = 11;
}

