using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Threading.Tasks;
using TMPro;

namespace Assets._Project.Scripts.UI
{
    public class MapListUI : MonoBehaviour
    {
        [Header("UI References")] [SerializeField]
        private Transform _mapListContainer;

        [SerializeField] private GameObject _mapItemPrefab;
        [SerializeField] private SnappingScroll _snappingScroll;
        [SerializeField] private Button _startNewGameButton; // Кнопка «Новая игра»

        [Header("Configuration")] [SerializeField]
        private SaveSystem _saveSystem;

        [Header("Map Selection UI")] [SerializeField]
        private GameObject _mapsMenu;

        // [SerializeField] private GameObject _newGame;
        [SerializeField] private GameObject _loadingPanel;

        [Header("New Map UI")] [SerializeField]
        private GameObject _newMapPanel;

        [SerializeField] private TMP_InputField _newMapNameInput;
        [SerializeField] private Button _confirmNewMapButton;
        [SerializeField] private Button _cancelNewMapButton;

        [Header("Scene Settings")] [SerializeField]
        private int _mapSceneIndex = 1; // Индекс сцены карты в Build Settings

        private List<MapItemView> _mapItems = new List<MapItemView>();
        private bool _isLoading;
        private Image _loadingBackgroundImage;
        private float _originalTimeScale = 1f;

        public event Action<string> OnMapLoadRequested;
        public event Action<string> OnMapDeleteRequested;
        public event Action OnLoadingStarted;
        public event Action OnLoadingCompleted;

        private void Start()
        {
            SubscribeToSaveListEvents();
            // Привязываем кнопку «Новая игра» к методу запуска
            if (_startNewGameButton != null)
            {
                _startNewGameButton.onClick.RemoveAllListeners();
                _startNewGameButton.onClick.AddListener(PromptNewMapName);
            }

            if (_confirmNewMapButton != null)
            {
                _confirmNewMapButton.onClick.RemoveAllListeners();
                _confirmNewMapButton.onClick.AddListener(OnConfirmNewMapName);
            }

            if (_cancelNewMapButton != null)
            {
                _cancelNewMapButton.onClick.RemoveAllListeners();
                _cancelNewMapButton.onClick.AddListener(OnCancelNewMapName);
            }

            if (_newMapPanel != null)
            {
                _newMapPanel.SetActive(false);
            }

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
                    _saveSystem = FindAnyObjectByType<SaveSystem>();
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

            ShowLoadingPanel();

            // Pause the game
            _originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            bool isInMenu = SceneManager.GetActiveScene().buildIndex == 0;

            if (isInMenu)
            {
                await LoadMapSceneAsync();

                if (_saveSystem == null)
                    _saveSystem = FindAnyObjectByType<SaveSystem>();
                if (_saveSystem != null)
                {
                    bool loadSuccess = await _saveSystem.LoadWorldAsync(mapName, OnWorldLoadProgress);

                    if (loadSuccess)
                    {
                        if (GameManager.Instance != null)
                        {
                            GameManager.Instance.CurrentWorldName = mapName;
                        }

                        if (Player.Instance != null)
                        {
                            Player.Instance.ForcePlayerControlMode();
                        }

                        HideLoadingPanel();

                        if (this != null)
                        {
                            CloseMapList();
                        }

                        // Resume the game
                        Time.timeScale = _originalTimeScale;
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();

                        if (this != null && gameObject != null)
                        {
                            gameObject.SetActive(true);
                        }

                        // Resume the game even on error
                        Time.timeScale = _originalTimeScale;
                    }
                }
            }
            else
            {
                if (_saveSystem == null)
                    _saveSystem = FindAnyObjectByType<SaveSystem>();
                if (_saveSystem != null)
                {
                    bool loadSuccess = await _saveSystem.LoadWorldAsync(mapName, false, OnWorldLoadProgress);

                    if (loadSuccess)
                    {
                        if (GameManager.Instance != null)
                        {
                            GameManager.Instance.CurrentWorldName = mapName;
                        }

                        if (Player.Instance != null)
                        {
                            Player.Instance.ForcePlayerControlMode();
                        }

                        HideLoadingPanel();

                        if (this != null)
                        {
                            CloseMapList();
                        }

                        // Resume the game
                        Time.timeScale = _originalTimeScale;
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();

                        if (this != null && gameObject != null)
                        {
                            gameObject.SetActive(true);
                        }

                        // Resume the game even on error
                        Time.timeScale = _originalTimeScale;
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

            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        public async void StartNewGame(string mapName = null)
        {
            gameObject.SetActive(false);

            ShowLoadingPanel();

            _originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            bool isInMenu = SceneManager.GetActiveScene().buildIndex == 0;

            if (isInMenu)
            {
                await LoadMapSceneAsync();
            }

            // Если указано имя карты, загружаем её
            if (!string.IsNullOrEmpty(mapName))
            {
                if (_saveSystem == null)
                    _saveSystem = FindAnyObjectByType<SaveSystem>();
                if (_saveSystem != null)
            {
                    bool loadSuccess = await _saveSystem.LoadWorldAsync(mapName, false, OnWorldLoadProgress);

                if (!loadSuccess)
                {
                    Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                    HideLoadingPanel();

                    if (this != null && gameObject != null)
                    {
                        gameObject.SetActive(true);
                    }

                    Time.timeScale = _originalTimeScale;
                    return;
                }

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.CurrentWorldName = mapName;
                    }
                }
            }

            // Очищаем все Entity из сцены после загрузки карты
            ClearAllEntities();

            if (Player.Instance != null)
            {
                Player.Instance.ForcePlayerControlMode();
            }

            HideLoadingPanel();

            if (this != null)
            {
                CloseMapList();
            }

            Time.timeScale = _originalTimeScale;
        }

        /// <summary>
        /// Очищает все Entity из текущей сцены
        /// </summary>
        private void ClearAllEntities()
        {
            Entity[] entities = FindObjectsByType<Entity>(FindObjectsSortMode.None);
            foreach (var entity in entities)
            {
                if (entity != null && entity.gameObject != null)
                {
                    Destroy(entity.gameObject);
                }
            }

            Debug.Log($"Удалено {entities.Length} Entity из сцены");
        }

        public void ShowLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(true);

                // Setup dark background
                SetupLoadingBackground();
            }
        }

        public void HideLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        private void SetupLoadingBackground()
        {
            if (_loadingPanel == null) return;

            try
            {
                // Find or create the background image component for darkening
                if (_loadingBackgroundImage == null)
                {
                    // Get the LoadingScreen GameObject
                    GameObject loadingScreenObj = _loadingPanel;

                    // Check if there's already a background image
                    Image existingImage = loadingScreenObj.GetComponent<Image>();
                    if (existingImage == null)
                    {
                        // Add Image component to LoadingScreen for dark background
                        _loadingBackgroundImage = loadingScreenObj.AddComponent<Image>();

                        // Set it to fill the entire screen
                        RectTransform rectTransform = loadingScreenObj.GetComponent<RectTransform>();
                        if (rectTransform != null)
                        {
                            rectTransform.anchorMin = new Vector2(0, 0);
                            rectTransform.anchorMax = new Vector2(1, 1);
                            rectTransform.offsetMin = Vector2.zero;
                            rectTransform.offsetMax = Vector2.zero;
                        }

                        // Set dark color (black with some transparency for darker effect)
                        _loadingBackgroundImage.color = new Color(0f, 0f, 0f, 0.8f);
                    }
                    else
                    {
                        _loadingBackgroundImage = existingImage;
                        _loadingBackgroundImage.color = new Color(0f, 0f, 0f, 0.8f);
                    }
                }
                else
                {
                    _loadingBackgroundImage.color = new Color(0f, 0f, 0f, 0.8f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to setup loading background: {e.Message}");
            }
        }

        public void PromptNewMapName()
        {
            if (_newMapPanel != null)
            {
                _newMapPanel.SetActive(true);
                if (_newMapNameInput != null)
                {
                    _newMapNameInput.text = "";
                    _newMapNameInput.Select();
                }
            }
        }

        private async void OnConfirmNewMapName()
        {
            if (_newMapNameInput == null || string.IsNullOrWhiteSpace(_newMapNameInput.text))
            {
                Debug.LogError("Map name cannot be empty.");
                return;
            }

            string worldName = _newMapNameInput.text.Trim();

            if (GameManager.Instance != null)
            {
                GameManager.Instance.CurrentWorldName = worldName;
            }

            if (_newMapPanel != null)
            {
                _newMapPanel.SetActive(false);
            }

            gameObject.SetActive(false);
            ShowLoadingPanel();

            _originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            bool isInMenu = SceneManager.GetActiveScene().buildIndex == 0;

            if (isInMenu)
            {
                await LoadMapSceneAsync();
            }

            ClearAllEntities();

            if (Player.Instance != null)
            {
                Player.Instance.ForcePlayerControlMode();
            }

            HideLoadingPanel();

            if (this != null)
            {
                CloseMapList();
            }

            Time.timeScale = _originalTimeScale;
        }

        private void OnCancelNewMapName()
        {
            if (_newMapPanel != null)
            {
                _newMapPanel.SetActive(false);
            }
        }
    }
}
