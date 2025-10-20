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
    public int entityId;
    public float rotationX;
    public float rotationY;
    public float rotationZ;
    public float rotationW;

    private const float POSITION_SCALE = 100f;

    public CubeData(Vector3 position, Color32 color, byte typeId, int entityId = 0, Quaternion rotation = default)
    {
        x = (short)Mathf.RoundToInt(position.x * POSITION_SCALE);
        y = (short)Mathf.RoundToInt(position.y * POSITION_SCALE);
        z = (short)Mathf.RoundToInt(position.z * POSITION_SCALE);
        r = color.r;
        g = color.g;
        b = color.b;
        a = color.a;
        blockTypeId = typeId;
        this.entityId = entityId;
        rotationX = rotation.x;
        rotationY = rotation.y;
        rotationZ = rotation.z;
        rotationW = rotation.w;
    }

    public Vector3 Position => new Vector3(x / POSITION_SCALE, y / POSITION_SCALE, z / POSITION_SCALE);
    public Color32 Color => new Color32(r, g, b, a);
    public Quaternion Rotation => new Quaternion(rotationX, rotationY, rotationZ, rotationW);

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
        writer.Write(entityId);
        writer.Write(rotationX);
        writer.Write(rotationY);
        writer.Write(rotationZ);
        writer.Write(rotationW);
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
            blockTypeId = reader.ReadByte(),
            entityId = reader.ReadInt32(),
            rotationX = reader.ReadSingle(),
            rotationY = reader.ReadSingle(),
            rotationZ = reader.ReadSingle(),
            rotationW = reader.ReadSingle()
        };
    }

    public const int SIZE_BYTES = 31;
}

