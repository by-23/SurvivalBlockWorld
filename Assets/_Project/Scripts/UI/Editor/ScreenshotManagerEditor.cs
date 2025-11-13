using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Scripts.UI.Editor
{
#if UNITY_EDITOR
    public class ScreenshotManagerEditor : EditorWindow
    {
        private Vector2 _scroll;
        private readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();
        private ScreenshotManager _manager;

        [MenuItem("Tools/Screenshot Manager")]
        public static void ShowWindow()
        {
            GetWindow<ScreenshotManagerEditor>("Screenshot Manager");
        }

        private void OnGUI()
        {
            if (_manager == null)
            {
                // Ищем ScreenshotManager в сцене или создаем временный объект для редактора
                _manager = Object.FindFirstObjectByType<ScreenshotManager>();
                if (_manager == null)
                {
                    GameObject tempObj = new GameObject("TempScreenshotManager");
                    _manager = tempObj.AddComponent<ScreenshotManager>();
                    tempObj.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Saved Screenshots", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Index"))
            {
                RefreshIndexAsync(_manager);
            }

            if (GUILayout.Button("Open Folder"))
            {
                EditorUtility.RevealInFinder(Application.persistentDataPath);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
            foreach (var kv in _manager.GetAllScreenshots())
            {
                DrawEntry(_manager, kv.Key, kv.Value);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(ScreenshotManager mgr, string id, string path)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("ID:", id);
            EditorGUILayout.LabelField("Path:", path ?? "");

            Texture2D tex = GetOrLoadThumb(path);
            if (tex != null)
            {
                float size = 128f;
                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(path) && File.Exists(path);
            if (GUILayout.Button("Reveal"))
            {
                EditorUtility.RevealInFinder(path);
            }

            GUI.enabled = true;

            if (GUILayout.Button("Delete"))
            {
                if (EditorUtility.DisplayDialog("Delete Screenshot",
                        "Удалить скриншот и запись?", "Delete", "Cancel"))
                {
                    if (mgr.DeleteScreenshot(id))
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (_thumbCache.TryGetValue(path, out var cachedTex) && cachedTex != null)
                            {
                                DestroyImmediate(cachedTex);
                            }

                            _thumbCache.Remove(path);
                        }

                        Repaint();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private Texture2D GetOrLoadThumb(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_thumbCache.TryGetValue(path, out var cached))
                return cached;

            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0) return null;
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    _thumbCache[path] = tex;
                    return tex;
                }
            }
            catch
            {
            }

            return null;
        }

        private async void RefreshIndexAsync(ScreenshotManager mgr)
        {
            await mgr.RefreshIndexAsync(true);
            _thumbCache.Clear();
            EditorApplication.delayCall += Repaint;
        }
    }
#endif
}


