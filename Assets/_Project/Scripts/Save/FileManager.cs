using System;
using System.Collections.Generic;
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

            byte[] data;
            try
            {
                data = worldData.PackToBinary();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to pack world data for '{worldData.WorldName}': {e.Message}");
                return false;
            }

            progressCallback?.Invoke(0.5f);

            string path = config.GetWorldSavePath(worldData.WorldName);

            try
            {
                await File.WriteAllBytesAsync(path, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write world file to '{path}': {e.Message}");
                return false;
            }

            progressCallback?.Invoke(1f);

            Debug.Log($"World saved successfully to: {path} ({data.Length / 1024f:F2} KB)");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"Failed to save world '{worldData?.WorldName ?? "unknown"}': {e.Message}\nStackTrace: {e.StackTrace}");
            return false;
        }
    }

    public async Task<WorldSaveData> LoadWorldAsync(string worldName, Action<float> progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke(0.1f);

            string path = config.GetWorldSavePath(worldName);

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

    public async Task<List<WorldMetadata>> LoadLocalWorldsMetadataAsync()
    {
        List<WorldMetadata> metadata = new List<WorldMetadata>();

        try
        {
            string directory = config.GetLocalSavesDirectory();

            if (!Directory.Exists(directory))
            {
                return metadata;
            }

            string[] files = Directory.GetFiles(directory, "*.dat", SearchOption.TopDirectoryOnly);

            foreach (string file in files)
            {
                try
                {
                    byte[] data = await File.ReadAllBytesAsync(file);
                    WorldSaveData world = WorldSaveData.UnpackFromBinary(data);
                    metadata.Add(new WorldMetadata
                    {
                        WorldName = world.WorldName,
                        ScreenshotPath = world.ScreenshotPath,
                        Timestamp = world.Timestamp,
                        Likes = world.LikesCount
                    });
                }
                catch (Exception readException)
                {
                    Debug.LogWarning($"Failed to read metadata from '{file}': {readException.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to enumerate local worlds: {e.Message}");
        }

        return metadata;
    }

    public bool SaveFileExists(string worldName)
    {
        string path = config.GetWorldSavePath(worldName);
        return File.Exists(path);
    }

    public bool AnySaveFilesExist()
    {
        string directory = config.GetLocalSavesDirectory();
        if (!Directory.Exists(directory))
        {
            return false;
        }

        string[] files = Directory.GetFiles(directory, "*.dat", SearchOption.TopDirectoryOnly);
        return files.Length > 0;
    }

    public bool DeleteSaveFile(string worldName)
    {
        string path = config.GetWorldSavePath(worldName);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"Save file deleted: {path}");
            return true;
        }

        return false;
    }
}

