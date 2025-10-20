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

    [Header("Entity Settings")] [Tooltip("Scale factor for entities when loading from save data")]
    public float entityScale = 0.35f;

    public string GetSavePath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, saveFileName);
    }
}

