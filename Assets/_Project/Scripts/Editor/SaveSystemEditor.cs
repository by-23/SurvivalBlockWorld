using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Threading.Tasks;

[CustomEditor(typeof(SaveSystem))]
public class SaveSystemEditor : Editor
{
    private string _worldNameToSave = "DeveloperMap";
    private List<WorldMetadata> _developerWorlds;
    private bool _listFetched = false;
    private Vector2 _scrollPosition;

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
            EditorApplication.delayCall += async () => await FetchDeveloperWorlds(saveSystem);
        }

        if (_developerWorlds != null)
        {
            EditorGUILayout.LabelField("Developer Maps on Firebase", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));

            if (_developerWorlds.Count == 0)
            {
                EditorGUILayout.LabelField("No developer maps found.");
            }
            else
            {
                foreach (var world in _developerWorlds)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(world.WorldName);
                    if (GUILayout.Button("Delete", GUILayout.Width(60)))
                    {
                        if (EditorUtility.DisplayDialog("Confirm Delete", $"Are you sure you want to delete '{world.WorldName}'?", "Yes", "No"))
                        {
                            // Удаляем карту и обновляем список
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
        Repaint(); // Перерисовать инспектор
    }
}
