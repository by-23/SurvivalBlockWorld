using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class FileManager
{
    private SaveConfig config;

    public FileManager(SaveConfig config)
    {
        this.config = config;
    }

    public async Task<bool> SaveWorldAsync(WorldSaveData worldData, Action<float> progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke(0.1f);

            byte[] data = worldData.PackToBinary();

            progressCallback?.Invoke(0.5f);

            string path = config.GetSavePath();
            string directory = Path.GetDirectoryName(path);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, data);

            progressCallback?.Invoke(1f);

            Debug.Log($"World saved successfully to: {path} ({data.Length / 1024f:F2} KB)");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save world: {e.Message}");
            return false;
        }
    }

    public async Task<WorldSaveData> LoadWorldAsync(Action<float> progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke(0.1f);

            string path = config.GetSavePath();

            if (!File.Exists(path))
            {
                Debug.LogWarning($"Save file not found at: {path}");
                return null;
            }

            progressCallback?.Invoke(0.3f);

            byte[] data = await File.ReadAllBytesAsync(path);

            progressCallback?.Invoke(0.7f);

            WorldSaveData worldData = WorldSaveData.UnpackFromBinary(data);

            progressCallback?.Invoke(1f);

            Debug.Log($"World loaded successfully from: {path} ({data.Length / 1024f:F2} KB)");
            return worldData;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load world: {e.Message}");
            return null;
        }
    }

    public bool SaveFileExists()
    {
        return File.Exists(config.GetSavePath());
    }

    public void DeleteSaveFile()
    {
        string path = config.GetSavePath();
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"Save file deleted: {path}");
        }
    }
}

