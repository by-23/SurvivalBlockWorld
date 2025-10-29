using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EntitySaveData
{
    public int entityId;
    public Vector3 position;
    public Vector3 scale;
    public Quaternion rotation;
    public CubeData[] cubesData;
}

/// <summary>
/// Manages saving and loading of Entity objects
/// </summary>
public class EntitySaveManager : MonoBehaviour
{
    [Header("UI Buttons")] [SerializeField]
    private UnityEngine.UI.Button _saveButton;

    [SerializeField] private UnityEngine.UI.Button _spawnButton;

    [Header("Spawn Settings")] [SerializeField]
    private float _spawnOffset = 5f;

    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private float _groundCheckDistance = 20f;

    [SerializeField] private EntitySelector _entitySelector;
    private List<EntitySaveData> _savedEntities;
    private int _currentSelectedSaveIndex;

    private void Awake()
    {
        _entitySelector = FindAnyObjectByType<EntitySelector>();
        _savedEntities = new List<EntitySaveData>();

        if (_saveButton != null)
        {
            _saveButton.onClick.AddListener(OnSaveButtonClicked);
        }

        if (_spawnButton != null)
        {
            _spawnButton.onClick.AddListener(OnSpawnButtonClicked);
        }
    }

    private void OnSaveButtonClicked()
    {
        if (_entitySelector == null)
        {
            Debug.LogError("EntitySelector not found!");
            return;
        }

        Entity hoveredEntity = _entitySelector.GetHoveredEntity();

        if (hoveredEntity == null)
        {
            Debug.LogWarning("No entity is being hovered over!");
            return;
        }

        SaveEntity(hoveredEntity);
    }

    private void OnSpawnButtonClicked()
    {
        if (_savedEntities.Count == 0)
        {
            Debug.LogWarning("No saved entities to spawn!");
            return;
        }

        SpawnSavedEntity();
    }

    /// <summary>
    /// Saves the specified entity to the saved entities list
    /// </summary>
    public void SaveEntity(Entity entity)
    {
        if (entity == null)
        {
            Debug.LogError("Cannot save null entity!");
            return;
        }

        EntitySaveData saveData = new EntitySaveData
        {
            entityId = entity.EntityId,
            position = entity.transform.position,
            scale = entity.transform.localScale,
            rotation = entity.transform.rotation,
            cubesData = entity.GetSaveData()
        };

        _savedEntities.Add(saveData);
        _currentSelectedSaveIndex = _savedEntities.Count - 1;

    }

    /// <summary>
    /// Spawns the last saved entity on the surface in front of the player
    /// </summary>
    public void SpawnSavedEntity()
    {
        if (_savedEntities.Count == 0)
        {
            Debug.LogWarning("No entities to spawn!");
            return;
        }

        EntitySaveData saveData = _savedEntities[_currentSelectedSaveIndex];
        Vector3 spawnPosition = GetSpawnPosition();

        // Create a new GameObject for the entity
        GameObject newEntityObj = new GameObject("Entity_" + DateTime.Now.Ticks);
        newEntityObj.transform.position = spawnPosition;
        newEntityObj.transform.rotation = saveData.rotation;
        newEntityObj.transform.localScale = saveData.scale;

        // Add required components
        Rigidbody rb = newEntityObj.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.drag = 0f;
        rb.angularDrag = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = true; // Will be disabled after loading

        Entity newEntity = newEntityObj.AddComponent<Entity>();
        newEntityObj.AddComponent<EntityMeshCombiner>();
        newEntityObj.AddComponent<EntityHookManager>();
        newEntityObj.AddComponent<EntityVehicleConnector>();

        StartCoroutine(LoadEntityAsync(newEntity, saveData));
    }

    /// <summary>
    /// Calculates spawn position on the surface in front of the player
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        Camera playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindAnyObjectByType<Camera>();
        }

        if (playerCamera != null)
        {
            Vector3 cameraPosition = playerCamera.transform.position;
            Vector3 cameraForward = playerCamera.transform.forward;

            Ray ray = new Ray(cameraPosition, cameraForward);

            // First try to find the ground
            if (Physics.Raycast(ray, out RaycastHit groundHit, _groundCheckDistance, _groundLayer))
            {
                return groundHit.point + Vector3.up * 0.5f;
            }

            // If no ground found, use a point in front of player
            return cameraPosition + cameraForward * _spawnOffset;
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Loads entity data asynchronously
    /// </summary>
    private System.Collections.IEnumerator LoadEntityAsync(Entity entity, EntitySaveData saveData)
    {
        if (saveData.cubesData == null || saveData.cubesData.Length == 0)
        {
            Debug.LogWarning("No cube data to load!");
            yield break;
        }

        CubeSpawner spawner = SaveSystem.Instance?.CubeSpawner;
        if (spawner == null)
        {
            spawner = FindAnyObjectByType<CubeSpawner>();
        }

        if (spawner == null)
        {
            Debug.LogError("CubeSpawner not found!");
            yield break;
        }

        // Convert cube positions from old entity position to new entity position
        Vector3 offset = entity.transform.position - saveData.position;
        CubeData[] adjustedCubeData = new CubeData[saveData.cubesData.Length];
        for (int i = 0; i < saveData.cubesData.Length; i++)
        {
            CubeData original = saveData.cubesData[i];
            adjustedCubeData[i] = new CubeData(
                original.Position + offset, // Shift to new entity position
                original.Color,
                original.blockTypeId,
                original.entityId,
                original.Rotation
            );
        }

        // Entity.LoadFromDataAsync expects world positions and converts them to local
        yield return entity.LoadFromDataAsync(adjustedCubeData, spawner, deferredSetup: true);
        entity.FinalizeLoad();

    }

    /// <summary>
    /// Gets the count of saved entities
    /// </summary>
    public int GetSavedEntityCount()
    {
        return _savedEntities.Count;
    }

    /// <summary>
    /// Clears all saved entities
    /// </summary>
    public void ClearSavedEntities()
    {
        _savedEntities.Clear();
        _currentSelectedSaveIndex = 0;
        Debug.Log("Saved entities cleared!");
    }

    /// <summary>
    /// Removes the specified entity from saved list
    /// </summary>
    public void RemoveSavedEntity(int index)
    {
        if (index >= 0 && index < _savedEntities.Count)
        {
            _savedEntities.RemoveAt(index);
            Debug.Log($"Entity removed at index {index}");
        }
    }
}
