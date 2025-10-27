using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Управляет данными списка карт: загружает из Firebase, создает элементы списка, вызывает события
    /// </summary>
    public class MapList : MonoBehaviour
    {
        [Header("UI References")] [SerializeField]
        private Transform _mapListContainer;

        [SerializeField] private GameObject _mapItemPrefab;
        [SerializeField] private SnappingScroll _snappingScroll;

        [Header("Configuration")] [SerializeField]
        private SaveSystem _saveSystem;

        private FirebaseFirestore _db;
        private List<MapItemView> _mapItems = new List<MapItemView>();
        private bool _isLoading;

        public event Action<string> OnMapLoadRequested;
        public event Action<string> OnMapDeleteRequested;
        public event Action OnLoadingStarted;
        public event Action OnLoadingCompleted;

        private void Awake()
        {
            _db = FirebaseFirestore.DefaultInstance;
        }

        public async void LoadSaveList()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            OnLoadingStarted?.Invoke();

            try
            {
                ClearMapList();
                if (_saveSystem == null)
                    _saveSystem = SaveSystem.Instance;
                if (_saveSystem == null)
                {
                    Debug.LogError("SaveSystem not assigned to SaveListManager!");
                    _isLoading = false;
                    OnLoadingCompleted?.Invoke();
                    return;
                }

                // Get worlds metadata from Firebase through SaveSystem
                var worldsMetadata = await _saveSystem.GetAllWorldsMetadata();

                if (worldsMetadata.Count == 0)
                {
                    _isLoading = false;
                    OnLoadingCompleted?.Invoke();
                    return;
                }

                foreach (var metadata in worldsMetadata)
                {
                    CreateMapItem(metadata.WorldName, metadata.ScreenshotPath);
                }

                OnLoadingCompleted?.Invoke();

                if (_snappingScroll)
                    _snappingScroll.UpdateChildren();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load save list: {e.Message}");
            }
            finally
            {
                _isLoading = false;
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

            // Подписываемся на события MapItemView и обрабатываем их локально
            mapItemView.OnLoadMapRequested += (name) => { OnMapLoadRequested?.Invoke(name); };
            mapItemView.OnDeleteMapRequested += (name) => { OnMapDeleteRequested?.Invoke(name); };

            _mapItems.Add(mapItemView);
        }

        private void ClearMapList()
        {
            foreach (var mapItem in _mapItems)
            {
                if (mapItem != null)
                {
                    // Отписываемся от всех обработчиков
                    mapItem.OnLoadMapRequested = null;
                    mapItem.OnDeleteMapRequested = null;
                    Destroy(mapItem.gameObject);
                }
            }

            _mapItems.Clear();
        }

        public void RefreshSaveList()
        {
            LoadSaveList();
        }

        public async void DeleteMap(string mapName)
        {
            OnLoadingStarted?.Invoke();

            try
            {
                if (_saveSystem == null)
                {
                    Debug.LogError("SaveSystem not assigned to SaveListManager!");
                    OnLoadingCompleted?.Invoke();
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
                    OnLoadingCompleted?.Invoke();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete map '{mapName}': {e.Message}");
                OnLoadingCompleted?.Invoke();
            }
        }

        private void OnDestroy()
        {
            ClearMapList();
        }
    }
}
