using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System;

[CustomEditor(typeof(SaveSystem))]
public class SaveSystemEditor : Editor
{
    private string _worldNameToSave = "DeveloperMap";
    private List<WorldMetadata> _developerWorlds;
    private bool _listFetched = false;
    private Vector2 _scrollPosition;
    private Dictionary<string, Texture2D> _screenshotTextures = new Dictionary<string, Texture2D>();
    private HashSet<string> _loadingScreenshots = new HashSet<string>();

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        SaveSystem saveSystem = (SaveSystem)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Developer Maps Management", EditorStyles.boldLabel);

        _worldNameToSave = EditorGUILayout.TextField("World Name to Save", _worldNameToSave);

        if (GUILayout.Button("Save Current Scene as Developer Map"))
        {
            if (!string.IsNullOrEmpty(_worldNameToSave))
            {
                // Сохраняем карту и обновляем список
                EditorApplication.delayCall += async () =>
                {
                    bool success = await saveSystem.SaveWorldAsDeveloperAsync(_worldNameToSave);
                    if (success)
                    {
                        await FetchDeveloperWorlds(saveSystem);
                    }
                };
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "World name cannot be empty.", "OK");
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Refresh List"))
        {
            _screenshotTextures.Clear();
            _loadingScreenshots.Clear();
            EditorApplication.delayCall += async () => await FetchDeveloperWorlds(saveSystem);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Screenshots Folder"))
        {
            string screenshotsPath = Application.persistentDataPath;
            if (Directory.Exists(screenshotsPath))
            {
                EditorUtility.RevealInFinder(screenshotsPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Screenshots folder not found.", "OK");
            }
        }

        if (_developerWorlds != null)
        {
            EditorGUILayout.LabelField("Developer Maps on Firebase", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(400));

            if (_developerWorlds.Count == 0)
            {
                EditorGUILayout.LabelField("No developer maps found.");
            }
            else
            {
                foreach (var world in _developerWorlds)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.LabelField("World Name:", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(world.WorldName);

                    if (!string.IsNullOrEmpty(world.ScreenshotPath))
                    {
                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField("Screenshot:", EditorStyles.boldLabel);

                        if (IsUrl(world.ScreenshotPath))
                        {
                            EditorGUILayout.LabelField("URL:", world.ScreenshotPath, EditorStyles.wordWrappedLabel);

                            bool isLoaded =
                                _screenshotTextures.TryGetValue(world.ScreenshotPath, out Texture2D urlTexture) &&
                                urlTexture != null;
                            bool isLoading = _loadingScreenshots.Contains(world.ScreenshotPath);

                            if (!isLoaded && !isLoading)
                            {
                                _loadingScreenshots.Add(world.ScreenshotPath);
                                EditorApplication.delayCall += async () =>
                                {
                                    await LoadScreenshotFromUrlAsync(world.ScreenshotPath);
                                    _loadingScreenshots.Remove(world.ScreenshotPath);
                                };
                            }

                            if (isLoading)
                            {
                                EditorGUILayout.HelpBox("Loading screenshot...", MessageType.Info);
                            }
                            else if (isLoaded)
                            {
                                float previewHeight = Mathf.Min(200, urlTexture.height);
                                float previewWidth = (urlTexture.width / (float)urlTexture.height) * previewHeight;
                                Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                                EditorGUI.DrawPreviewTexture(rect, urlTexture);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("Failed to load screenshot", MessageType.Warning);
                            }

                            if (GUILayout.Button("Open Screenshot URL", GUILayout.Height(30)))
                            {
                                Application.OpenURL(world.ScreenshotPath);
                            }
                        }
                        else
                        {
                            // Проверяем, является ли путь временным файлом
                            bool isTempFile = world.ScreenshotPath.Contains(Application.temporaryCachePath);

                            if (isTempFile)
                            {
                                EditorGUILayout.HelpBox(
                                    "Temporary screenshot path (file was deleted after Firebase upload). Screenshot should be available via Firebase URL.",
                                    MessageType.Info);
                            }
                            else
                            {
                                EditorGUILayout.LabelField("Path:", world.ScreenshotPath,
                                    EditorStyles.wordWrappedLabel);

                                if (File.Exists(world.ScreenshotPath))
                                {
                                    Texture2D screenshot = LoadScreenshotTexture(world.ScreenshotPath);
                                    if (screenshot != null)
                                    {
                                        float previewHeight = Mathf.Min(200, screenshot.height);
                                        float previewWidth = (screenshot.width / (float)screenshot.height) *
                                                             previewHeight;
                                        Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                                        EditorGUI.DrawPreviewTexture(rect, screenshot);

                                        if (GUILayout.Button("Open Screenshot File", GUILayout.Height(25)))
                                        {
                                            EditorUtility.RevealInFinder(world.ScreenshotPath);
                                        }
                                    }
                                }
                                else
                                {
                                    EditorGUILayout.HelpBox("Screenshot file not found", MessageType.Warning);
                                }
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("No screenshot available", MessageType.Info);
                    }

                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Delete", GUILayout.Height(25)))
                    {
                        if (EditorUtility.DisplayDialog("Confirm Delete",
                                $"Are you sure you want to delete '{world.WorldName}'?", "Yes", "No"))
                        {
                            EditorApplication.delayCall += async () =>
                            {
                                bool success = await saveSystem.DeleteDeveloperWorldAsync(world.WorldName);
                                if (success)
                                {
                                    await FetchDeveloperWorlds(saveSystem);
                                }
                            };
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(5);
                }
            }

            EditorGUILayout.EndScrollView();
        }
        else if (!_listFetched)
        {
            // Загружаем список при первой отрисовке
            EditorApplication.delayCall += async () => await FetchDeveloperWorlds(saveSystem);
        }
    }

    private async Task FetchDeveloperWorlds(SaveSystem saveSystem)
    {
        _developerWorlds = await saveSystem.GetDeveloperWorldsMetadataAsync();
        _listFetched = true;

        // Автоматически загружаем скриншоты из Firebase для всех карт
        if (_developerWorlds != null)
        {
            foreach (var world in _developerWorlds)
            {
                if (!string.IsNullOrEmpty(world.ScreenshotPath) && IsUrl(world.ScreenshotPath))
                {
                    if (!_screenshotTextures.ContainsKey(world.ScreenshotPath) &&
                        !_loadingScreenshots.Contains(world.ScreenshotPath))
                    {
                        _loadingScreenshots.Add(world.ScreenshotPath);
                        _ = LoadScreenshotFromUrlAsync(world.ScreenshotPath);
                    }
                }
            }
        }

        Repaint();
    }

    private bool IsUrl(string path)
    {
        return !string.IsNullOrEmpty(path) && (path.StartsWith("http://") || path.StartsWith("https://"));
    }

    private Texture2D LoadScreenshotTexture(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            byte[] imageData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(imageData))
            {
                return texture;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load screenshot texture: {e.Message}");
        }

        return null;
    }

    private async Task LoadScreenshotFromUrlAsync(string url)
    {
        try
        {
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    byte[] imageData = request.downloadHandler.data;
                    if (imageData != null && imageData.Length > 0)
                    {
                        Texture2D texture = new Texture2D(2, 2);
                        if (texture.LoadImage(imageData))
                        {
                            _screenshotTextures[url] = texture;
                            _loadingScreenshots.Remove(url);
                            EditorApplication.delayCall += () => Repaint();
                        }
                        else
                        {
                            Debug.LogWarning($"Failed to load image data from URL: {url}");
                            _loadingScreenshots.Remove(url);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Empty image data from URL: {url}");
                        _loadingScreenshots.Remove(url);
                    }
                }
                else
                {
                    Debug.LogError($"Failed to download screenshot from URL: {url}, Error: {request.error}");
                    _loadingScreenshots.Remove(url);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception while downloading screenshot from URL {url}: {e.Message}");
            _loadingScreenshots.Remove(url);
        }
    }
}
