using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EntityManager))]
public class EntityManagerEditor : Editor
{
    private bool _showSaved;
    private Vector2 _scroll;

    [System.Serializable]
    private class ScreenshotIndexDTO
    {
        public List<ScreenshotEntryDTO> Entries;
    }

    [System.Serializable]
    private class ScreenshotEntryDTO
    {
        public string Id;
        public string Path;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var mgr = (EntityManager)target;
        EditorGUILayout.Space();
        _showSaved = EditorGUILayout.Foldout(_showSaved, "Saved Entities");
        if (_showSaved)
        {
            if (GUILayout.Button("Refresh"))
            {
                mgr.RefreshSavedList();
            }

            List<EntityManager.SavedEntry> entries = mgr.GetSavedEntries();
            if (entries == null || entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Нет сохранённых объектов.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(300));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Name:", string.IsNullOrEmpty(e.name) ? "Entity" : e.name);
                EditorGUILayout.LabelField("Entity Path:", e.path);

                // Показываем путь к скриншоту: либо URL (если он уже в Firebase),
                // либо локальный путь, восстановленный по id из индекса скриншотов.
                string screenshotPathDisplay = e.screenshotId;
                if (!string.IsNullOrEmpty(e.screenshotId) &&
                    !(e.screenshotId.StartsWith("http://") || e.screenshotId.StartsWith("https://")))
                {
                    // Пытаемся найти локальный путь к скриншоту по id (как в EntityManager.DeleteSavedEntity)
                    try
                    {
                        string indexPath = Path.Combine(Application.persistentDataPath, "screenshots.json");
                        if (File.Exists(indexPath))
                        {
                            string json = File.ReadAllText(indexPath);
                            if (!string.IsNullOrEmpty(json))
                            {
                                var dto = JsonUtility.FromJson<ScreenshotIndexDTO>(json);
                                if (dto != null && dto.Entries != null)
                                {
                                    for (int j = dto.Entries.Count - 1; j >= 0; j--)
                                    {
                                        if (dto.Entries[j] != null && dto.Entries[j].Id == e.screenshotId)
                                        {
                                            screenshotPathDisplay = dto.Entries[j].Path;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // В редакторе не падаем, если индекс прочитать не удалось
                    }
                }

                EditorGUILayout.LabelField("Screenshot Path:", string.IsNullOrEmpty(screenshotPathDisplay)
                    ? "(none)"
                    : screenshotPathDisplay);

                // Показываем ссылку/идентификатор entity в Firebase, если она уже сохранена
                string firebaseEntityDisplay = "(not saved)";
                if (mgr.TryGetFirebaseEntityId(e.path, out var firebaseId) && !string.IsNullOrEmpty(firebaseId))
                {
                    firebaseEntityDisplay = $"sharedEntities/{firebaseId}";
                }

                EditorGUILayout.LabelField("Firebase Entity:", firebaseEntityDisplay);

                bool isInFirebase = mgr.IsEntityInFirebase(e.path);
                if (isInFirebase)
                {
                    EditorGUILayout.HelpBox("✓ Сохранено в Firebase", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Не сохранено в Firebase. Нажмите 'Save' для сохранения.",
                        MessageType.Warning);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save"))
                {
                    Debug.Log(
                        $"[EntityManagerEditor] Save button pressed. Path='{e.path}', ScreenshotId='{e.screenshotId}', Name='{e.name}'");
                    mgr.SaveEntityToFirebaseFromEditor(e.path, e.screenshotId, e.name);
                }

                if (GUILayout.Button("Delete from Firebase"))
                {
                    mgr.DeleteFromFirebaseOnly(e.path);
                }

                if (GUILayout.Button("Delete"))
                {
                    mgr.DeleteSavedEntity(e.path, e.screenshotId);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}


