using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainGenerator generator = (TerrainGenerator)target;

        // Показываем кнопку "Сгенерировать" только если авто-обновление отключено
        if (!generator.RealtimeUpdate)
        {
            if (GUILayout.Button("Сгенерировать"))
            {
                generator.Generate();
            }
        }

        if (GUILayout.Button("Очистить"))
        {
            generator.Clear();
        }
    }
}
