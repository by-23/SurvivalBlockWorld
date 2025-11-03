using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EntityManager))]
public class EntityManagerEditor : Editor
{
    private bool _showSaved;
    private Vector2 _scroll;

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
                EditorGUILayout.LabelField("Path:", e.path);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spawn"))
                {
                    mgr.LoadSavedEntityFromPath(e.path);
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


