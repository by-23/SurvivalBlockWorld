using System.IO;
using UnityEngine;

[CreateAssetMenu(fileName = "SaveConfig", menuName = "Save System/Save Config")]
public class SaveConfig : ScriptableObject
{
    [Header("Chunk Settings")] public int chunkSize = 32;

    [Header("World Bounds")] public Vector3Int worldBoundsMin = new Vector3Int(-500, -100, -500);
    public Vector3Int worldBoundsMax = new Vector3Int(500, 100, 500);

    [Header("Save Mode")] public bool useFirebase = true;
    public bool useLocalCache = true;

    [Header("Performance")] public bool enableCompression = false;
    public int maxCubesPerFrame = 100;

    [Tooltip("Disable physics during world load for faster spawning")]
    public bool disablePhysicsDuringLoad = true;

    [Tooltip("Defer entity setup until all cubes are spawned")]
    public bool useDeferredSetup = true;

    [Tooltip("Minimum batch size for yielding (higher = faster load, more frame stuttering)")]
    public int minBatchSize = 200;

    [Header("File Settings")] public string saveFileName = "world.dat";
    public string localSavesFolderName = "WorldSaves";

    [Header("Entity Settings")] [Tooltip("Scale factor for entities when loading from save data")]
    public float entityScale = 0.35f;

    public string GetSavePath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, saveFileName);
    }

    public string GetLocalSavesDirectory()
    {
        return Path.Combine(Application.persistentDataPath, localSavesFolderName);
    }

    public string GetWorldSavePath(string worldName)
    {
        string directory = GetLocalSavesDirectory();
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(worldName) ? "world" : worldName);
        return Path.Combine(directory, $"{safeName}.dat");
    }

    public string GetWorldScreenshotPath(string worldName)
    {
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(worldName) ? "world" : worldName);
        return Path.Combine(Application.persistentDataPath, $"{safeName}.png");
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "world";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] buffer = value.ToCharArray();
        bool hasValidSymbol = false;

        for (int i = 0; i < buffer.Length; i++)
        {
            char current = buffer[i];
            bool isInvalid = false;

            for (int j = 0; j < invalidChars.Length; j++)
            {
                if (current == invalidChars[j])
                {
                    isInvalid = true;
                    break;
                }
            }

            if (isInvalid || char.IsControl(current))
            {
                buffer[i] = '_';
            }
            else
            {
                hasValidSymbol = true;
            }
        }

        string sanitized = new string(buffer).Trim();
        return string.IsNullOrEmpty(sanitized) || !hasValidSymbol ? "world" : sanitized;
    }
}

