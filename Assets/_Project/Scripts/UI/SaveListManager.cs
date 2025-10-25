using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;
using TMPro;

public class SaveListManager : MonoBehaviour
{
    [Header("UI References")] [SerializeField]
    private Transform _mapListContainer;

    [SerializeField] private GameObject _mapItemPrefab;
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private TextMeshProUGUI _loadingText;
    [SerializeField] private SnappingScroll _snappingScroll;

    [Header("Configuration")] [SerializeField]
    private SaveSystem _saveSystem;

    private FirebaseFirestore _db;
    private List<MapItemView> _mapItems = new List<MapItemView>();

    public System.Action<string> OnMapLoadRequested;
    public System.Action<string> OnMapDeleteRequested;
    public System.Action OnLoadingStarted;
    public System.Action OnLoadingCompleted;

    private void Awake()
    {
        _db = FirebaseFirestore.DefaultInstance;
    }

    public async void LoadSaveList()
    {
        ShowLoading(true, "Загрузка списка карт...");
        OnLoadingStarted?.Invoke();

        try
        {
            ClearMapList();
            if (_saveSystem == null)
             _saveSystem = SaveSystem.Instance;
            if (_saveSystem == null)
            {
                Debug.LogError("SaveSystem not assigned to SaveListManager!");
                ShowLoading(false);
                OnLoadingCompleted?.Invoke();
                return;
            }

            // Get worlds metadata from Firebase through SaveSystem
            var worldsMetadata = await _saveSystem.GetAllWorldsMetadata();

            if (worldsMetadata.Count == 0)
            {
                ShowLoading(false);
                OnLoadingCompleted?.Invoke();
                Debug.Log("No saved worlds found");
                return;
            }

            foreach (var metadata in worldsMetadata)
            {
                CreateMapItem(metadata.WorldName, metadata.ScreenshotPath);
            }

            ShowLoading(false);
            OnLoadingCompleted?.Invoke();

            if (_snappingScroll)
                _snappingScroll.UpdateChildren();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load save list: {e.Message}");
            ShowLoading(false);
            OnLoadingCompleted?.Invoke();
        }
    }

    private void CreateMapItem(string mapName, string screenshotPath)
    {
        if (_mapItemPrefab == null || _mapListContainer == null)
        {
            Debug.LogError("Map item prefab or container not assigned!");
            return;
        }

        GameObject mapItemObj = Instantiate(_mapItemPrefab, _mapListContainer);
        MapItemView mapItemView = mapItemObj.GetComponent<MapItemView>();

        if (mapItemView == null)
        {
            Debug.LogError("MapItemView component not found on prefab!");
            Destroy(mapItemObj);
            return;
        }

        mapItemView.SetMapData(mapName, screenshotPath);
        mapItemView.OnLoadMapRequested += OnMapLoadRequested;
        mapItemView.OnDeleteMapRequested += OnMapDeleteRequested;

        _mapItems.Add(mapItemView);
    }

    private void ClearMapList()
    {
        foreach (var mapItem in _mapItems)
        {
            if (mapItem != null)
            {
                mapItem.OnLoadMapRequested -= OnMapLoadRequested;
                mapItem.OnDeleteMapRequested -= OnMapDeleteRequested;
                Destroy(mapItem.gameObject);
            }
        }

        _mapItems.Clear();
    }

    private void ShowLoading(bool show, string message = "")
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(show);
        }

        if (_loadingText != null)
        {
            _loadingText.text = message;
        }
    }

    public void RefreshSaveList()
    {
        LoadSaveList();
    }

    public async void DeleteMap(string mapName)
    {
        ShowLoading(true, "Удаление карты...");

        try
        {
            if (_saveSystem == null)
            {
                Debug.LogError("SaveSystem not assigned to SaveListManager!");
                ShowLoading(false);
                return;
            }

            bool success = await _saveSystem.DeleteWorldAsync(mapName);

            if (success)
            {
                Debug.Log($"Map '{mapName}' deleted successfully.");
                // Обновляем список после успешного удаления
                LoadSaveList();
            }
            else
            {
                Debug.LogError($"Failed to delete map '{mapName}'.");
                ShowLoading(false);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to delete map '{mapName}': {e.Message}");
            ShowLoading(false);
        }
    }

    private void OnDestroy()
    {
        ClearMapList();
    }
}
