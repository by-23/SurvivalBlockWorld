using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SaveProps : MonoBehaviour
{

    Entity _entity;

    private void Start()
    {
        _entity = GetComponent<Entity>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Z))
            Save(gameObject);

    }

    public void SavePrefab()
    {
        Save(gameObject);
    }

    public void Save(GameObject _saveObj)
    {
        /*string _localPath = "Assets/_Project/Resources/SavePrefabs/" + _saveObj.name + ".prefab";
        _localPath = AssetDatabase.GenerateUniqueAssetPath(_localPath);

        PrefabUtility.SaveAsPrefabAssetAndConnect(_saveObj, _localPath, InteractionMode.AutomatedAction);
        
        Debug.Log("Save: " + _saveObj.name);*/
    }
}
