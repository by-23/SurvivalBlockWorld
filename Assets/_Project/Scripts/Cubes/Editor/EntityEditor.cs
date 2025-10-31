using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Entity))]
public class EntityEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw all default fields (backing id is hidden by HideInInspector)
        DrawDefaultInspector();

        // Show read-only EntityId
        var entity = (Entity)target;
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField("Entity Id", entity.EntityId);
        EditorGUI.EndDisabledGroup();
    }
}


