using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

namespace Assets._Project.Scripts.UI
{
    public class MapListUI : MonoBehaviour
    {
        [Header("UI References")] [SerializeField]
        private Transform _mapListContainer;

        [SerializeField] private GameObject _mapItemPrefab;
        [SerializeField] private SnappingScroll _snappingScroll;

        [Header("Configuration")] [SerializeField]
        private SaveSystem _saveSystem;

        [Header("Map Selection UI")] [SerializeField]
        private GameObject _mapsMenu;

        [SerializeField] private GameObject _newGame;
        [SerializeField] private GameObject _loadingPanel;

        [Header("Scene Settings")] [SerializeField]
        private int _mapSceneIndex = 0;

        private List<MapItemView> _mapItems = new List<MapItemView>();
        private bool _isLoading;

        public event Action<string> OnMapLoadRequested;
        public event Action<string> OnMapDeleteRequested;
        public event Action OnLoadingStarted;
        public event Action OnLoadingCompleted;

        private void Start()
        {
            SubscribeToSaveListEvents();
            if (SceneManager.GetActiveScene().buildIndex == 0)
                LoadSaveList();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSaveListEvents();
            ClearMapList();
        }

        private void SubscribeToSaveListEvents()
        {
            OnMapLoadRequested += OnMapLoadRequestedHandler;
            OnMapDeleteRequested += OnMapDeleteRequestedHandler;
            OnLoadingStarted += OnLoadingStartedHandler;
            OnLoadingCompleted += OnLoadingCompletedHandler;
        }

        private void UnsubscribeFromSaveListEvents()
        {
            OnMapLoadRequested -= OnMapLoadRequestedHandler;
            OnMapDeleteRequested -= OnMapDeleteRequestedHandler;
            OnLoadingStarted -= OnLoadingStartedHandler;
            OnLoadingCompleted -= OnLoadingCompletedHandler;
        }

        private void OnSettingsButtonPressed()
        {
            Debug.Log("Настройки - функционал пока не реализован");
            // TODO: Implement settings functionality
        }

        public void OpenMapList()
        {
            if (_mapsMenu != null)
            {
                _mapsMenu.SetActive(true);
            }

            gameObject.SetActive(true);


            if (_newGame != null)
            {
                _newGame.SetActive(true);
            }

            LoadSaveList();
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
                    Debug.LogError("SaveSystem not assigned to MapListUI!");
                    _isLoading = false;
                    OnLoadingCompleted?.Invoke();
                    return;
                }

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

            // Замыкание нужно для сохранения актуального имени карты в событии
            string capturedMapName = mapName;
            mapItemView.OnLoadMapRequested += (_) => { OnMapLoadRequested?.Invoke(capturedMapName); };
            mapItemView.OnDeleteMapRequested += (_) => { OnMapDeleteRequested?.Invoke(capturedMapName); };

            _mapItems.Add(mapItemView);
        }

        private void ClearMapList()
        {
            foreach (var mapItem in _mapItems)
            {
                if (mapItem != null)
                {
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
                    Debug.LogError("SaveSystem not assigned to MapListUI!");
                    OnLoadingCompleted?.Invoke();
                    return;
                }

                bool success = await _saveSystem.DeleteWorldAsync(mapName);

                if (success)
                {
                    Debug.Log($"Map '{mapName}' deleted successfully.");
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

        private void OnMapLoadRequestedHandler(string mapName)
        {
            OnMapLoadRequestedAsync(mapName);
        }

        private async void OnMapLoadRequestedAsync(string mapName)
        {
            gameObject.SetActive(false);

            if (_newGame != null)
            {
                _newGame.SetActive(false);
            }

            ShowLoadingPanel();

            bool isInMenu = SceneManager.GetActiveScene().buildIndex == 0;

            if (isInMenu)
            {
                await LoadMapSceneAsync();

                if (SaveSystem.Instance != null)
                {
                    bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, OnWorldLoadProgress);

                    if (loadSuccess)
                    {
                        InputManager.ForceActivateInputManager();
                        if (Player.Instance != null)
                        {
                            Player.Instance.ForcePlayerControlMode();
                        }

                        HideLoadingPanel();

                        if (this != null)
                        {
                            CloseMapList();
                        }
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();

                        if (this != null && gameObject != null)
                        {
                            gameObject.SetActive(true);

                            if (_newGame != null)
                            {
                                _newGame.SetActive(true);
                            }
                        }
                    }
                }
            }
            else
            {
                if (SaveSystem.Instance != null)
                {
                    bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, false, OnWorldLoadProgress);

                    if (loadSuccess)
                    {
                        InputManager.ForceActivateInputManager();
                        if (Player.Instance != null)
                        {
                            Player.Instance.ForcePlayerControlMode();
                        }

                        HideLoadingPanel();

                        if (this != null)
                        {
                            CloseMapList();
                        }
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();

                        if (this != null && gameObject != null)
                        {
                            gameObject.SetActive(true);

                            if (_newGame != null)
                            {
                                _newGame.SetActive(true);
                            }
                        }
                    }
                }
            }
        }

        private void OnMapDeleteRequestedHandler(string mapName)
        {
            Debug.Log($"Удаление карты: {mapName}");
            DeleteMap(mapName);
        }

        private void OnLoadingStartedHandler()
        {
            ShowLoadingPanel();
        }

        private void OnLoadingCompletedHandler()
        {
            HideLoadingPanel();
        }

        private void OnWorldLoadProgress(float progress)
        {
        }

        private async Task LoadMapSceneAsync()
        {
            if (_mapSceneIndex < 0 || _mapSceneIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError(
                    $"Неверный индекс сцены: {_mapSceneIndex}. Доступно сцен: {SceneManager.sceneCountInBuildSettings}");
                return;
            }

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(_mapSceneIndex);

            if (asyncLoad == null)
            {
                Debug.LogError($"Не удалось начать загрузку сцены с индексом {_mapSceneIndex}");
                return;
            }

            while (!asyncLoad.isDone)
            {
                if (this != null)
                {
                    CloseMapList();
                }

                await Task.Yield();
            }
        }


        public void CloseMapList()
        {
            if (this == null || gameObject == null)
            {
                return;
            }

            gameObject.SetActive(false);

            if (_newGame != null)
            {
                _newGame.SetActive(false);
            }

            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        public void ShowLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(true);
            }
        }

        public void HideLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }
    }
}
