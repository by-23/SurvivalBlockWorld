using System;
using System.Collections.Generic;
using System.Linq;
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
        private Transform _localMapListContainer;

        [SerializeField] private Transform _publishedMapListContainer;

        [SerializeField] private GameObject _localMapListObject;
        [SerializeField] private GameObject _publishedMapListObject;

        [SerializeField] private GameObject _mapItemPrefab;
        [SerializeField] private Button _localListButton;
        [SerializeField] private Button _onlineListButton;
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

        private readonly List<MapItemEntry> _mapItems = new List<MapItemEntry>();
        private bool _isLoading;
        private Image _loadingBackgroundImage;
        private float _originalTimeScale = 1f;
        private bool _isLoadingScene = false;
        private bool _listsLoaded;
        private SaveSystem.WorldStorageSource? _activeListSource;

        public event Action<string, SaveSystem.WorldStorageSource> OnMapLoadRequested;
        public event Action<string, SaveSystem.WorldStorageSource> OnMapDeleteRequested;
        public event Action OnLoadingStarted;
        public event Action OnLoadingCompleted;

        private class MapItemEntry
        {
            public MapItemView View;
            public string MapName;
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeFromSaveListEvents();
            ClearMapList();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Скрываем экран загрузки после полной загрузки сцены, если он был показан
            if (_isLoadingScene && _loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
                _isLoadingScene = false;
            }
        }

        private void Start()
        {
            SubscribeToSaveListEvents();
            // Привязываем кнопку «Новая игра» к методу запуска
            if (_startNewGameButton != null)
            {
                _startNewGameButton.onClick.RemoveAllListeners();
                _startNewGameButton.onClick.AddListener(PromptNewMapName);
            }

            if (_localListButton != null)
            {
                _localListButton.onClick.RemoveAllListeners();
                _localListButton.onClick.AddListener(OnLocalListButtonPressed);
            }

            if (_onlineListButton != null)
            {
                _onlineListButton.onClick.RemoveAllListeners();
                _onlineListButton.onClick.AddListener(OnOnlineListButtonPressed);
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

            HideAllLists();

            if (SceneManager.GetActiveScene().buildIndex == 0)
            {
                ToggleList(SaveSystem.WorldStorageSource.LocalOnly);
            }
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

            ToggleList(SaveSystem.WorldStorageSource.LocalOnly);
        }

        public async void LoadSaveList()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            OnLoadingStarted?.Invoke();
            _listsLoaded = false;

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

                var localTask = _saveSystem.GetAllLocalWorldsMetadata();
                var publishedTask = _saveSystem.GetAllWorldsMetadata();

                await Task.WhenAll(localTask, publishedTask);

                var localWorlds = localTask.Result;
                var publishedWorlds = publishedTask.Result;

                if ((localWorlds == null || localWorlds.Count == 0) &&
                    (publishedWorlds == null || publishedWorlds.Count == 0))
                {
                    _listsLoaded = true;
                    _isLoading = false;
                    OnLoadingCompleted?.Invoke();
                    ApplyActiveListVisibility();
                    UpdateListButtonsState();
                    return;
                }

                if (localWorlds != null)
                {
                    foreach (var metadata in localWorlds)
                    {
                        CreateMapItem(metadata, GetContainerForSource(SaveSystem.WorldStorageSource.LocalOnly),
                            SaveSystem.WorldStorageSource.LocalOnly);
                    }
                }

                if (publishedWorlds != null)
                {
                    foreach (var metadata in publishedWorlds)
                    {
                        CreateMapItem(metadata, GetContainerForSource(SaveSystem.WorldStorageSource.OnlineOnly),
                            SaveSystem.WorldStorageSource.OnlineOnly);
                    }
                }

                OnLoadingCompleted?.Invoke();
                _listsLoaded = true;
                ApplyActiveListVisibility();
                UpdateListButtonsState();
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

        private Transform GetContainerForSource(SaveSystem.WorldStorageSource source)
        {
            return source == SaveSystem.WorldStorageSource.LocalOnly
                ? _localMapListContainer
                : _publishedMapListContainer;
        }

        private void CreateMapItem(WorldMetadata metadata, Transform container, SaveSystem.WorldStorageSource source)
        {
            if (_mapItemPrefab == null || container == null)
            {
                Debug.LogError("Map item prefab or container not assigned!");
                return;
            }

            GameObject mapItemObj = Instantiate(_mapItemPrefab, container);
            MapItemView mapItemView = mapItemObj.GetComponent<MapItemView>();

            if (mapItemView == null)
            {
                Debug.LogError("MapItemView component not found on prefab!");
                Destroy(mapItemObj);
                return;
            }

            mapItemView.SetMapData(metadata.WorldName, metadata.ScreenshotPath, metadata.Likes);

            string capturedMapName = metadata.WorldName;
            mapItemView.OnLoadMapRequested += (_) => { OnMapLoadRequested?.Invoke(capturedMapName, source); };
            mapItemView.OnDeleteMapRequested += (_) => { OnMapDeleteRequested?.Invoke(capturedMapName, source); };

            if (source == SaveSystem.WorldStorageSource.OnlineOnly)
            {
                mapItemView.OnLikeValueChanged += OnMapLikesChanged;
                mapItemView.SetPublishButtonEnabled(false);
            }
            else
            {
                mapItemView.SetLikesEnabled(false);
                mapItemView.OnPublishRequested += OnPublishRequested;
                CheckAndSetPublishedState(mapItemView, capturedMapName);
            }

            _mapItems.Add(new MapItemEntry { View = mapItemView, MapName = capturedMapName });
        }

        private void ClearMapList()
        {
            foreach (var mapItem in _mapItems)
            {
                if (mapItem?.View != null)
                {
                    mapItem.View.OnLoadMapRequested = null;
                    mapItem.View.OnDeleteMapRequested = null;
                    mapItem.View.OnLikeValueChanged = null;
                    mapItem.View.OnPublishRequested = null;
                    Destroy(mapItem.View.gameObject);
                }
            }

            _mapItems.Clear();
        }

        private void OnLocalListButtonPressed()
        {
            ToggleList(SaveSystem.WorldStorageSource.LocalOnly);
        }

        private void OnOnlineListButtonPressed()
        {
            ToggleList(SaveSystem.WorldStorageSource.OnlineOnly);
        }

        private void ToggleList(SaveSystem.WorldStorageSource targetSource)
        {
            if (_activeListSource.HasValue && _activeListSource.Value == targetSource)
            {
                _activeListSource = null;
                HideAllLists();
                UpdateListButtonsState();
                return;
            }

            _activeListSource = targetSource;

            if (targetSource == SaveSystem.WorldStorageSource.OnlineOnly)
            {
                LoadSaveList();
            }
            else if (!_listsLoaded)
            {
                LoadSaveList();
            }

            ApplyActiveListVisibility();
            UpdateListButtonsState();
        }

        private void ApplyActiveListVisibility()
        {
            if (!_activeListSource.HasValue)
            {
                HideAllLists();
                return;
            }

            bool showLocal = _activeListSource.Value == SaveSystem.WorldStorageSource.LocalOnly;
            bool showOnline = _activeListSource.Value == SaveSystem.WorldStorageSource.OnlineOnly;

            if (_localMapListObject != null)
            {
                _localMapListObject.SetActive(showLocal);
            }

            if (_publishedMapListObject != null)
            {
                _publishedMapListObject.SetActive(showOnline);
            }
        }

        private void HideAllLists()
        {
            if (_localMapListObject != null)
            {
                _localMapListObject.SetActive(false);
            }

            if (_publishedMapListObject != null)
            {
                _publishedMapListObject.SetActive(false);
            }
        }

        private void UpdateListButtonsState()
        {
            if (_localListButton != null)
            {
                _localListButton.interactable = !_activeListSource.HasValue ||
                                                _activeListSource.Value != SaveSystem.WorldStorageSource.LocalOnly;
            }

            if (_onlineListButton != null)
            {
                _onlineListButton.interactable = !_activeListSource.HasValue ||
                                                 _activeListSource.Value != SaveSystem.WorldStorageSource.OnlineOnly;
            }
        }

        public void RefreshSaveList()
        {
            LoadSaveList();
        }

        public async void DeleteMap(string mapName, SaveSystem.WorldStorageSource source)
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

                bool localDeleted = false;
                bool onlineDeleted = false;

                if (source == SaveSystem.WorldStorageSource.LocalOnly)
                {
                    localDeleted = _saveSystem.DeleteLocalWorld(mapName);
                    bool isPublished = await _saveSystem.IsWorldPublishedAsync(mapName);
                    if (isPublished)
                    {
                        onlineDeleted = await _saveSystem.DeleteWorldAsync(mapName);
                    }
                    else
                    {
                        onlineDeleted = true;
                    }
                }
                else
                {
                    onlineDeleted = await _saveSystem.DeleteWorldAsync(mapName);
                    var localWorlds = await _saveSystem.GetAllLocalWorldsMetadata();
                    bool localExists = localWorlds != null && localWorlds.Any(w => w.WorldName == mapName);
                    if (localExists)
                    {
                        localDeleted = _saveSystem.DeleteLocalWorld(mapName);
                    }
                    else
                    {
                        localDeleted = true;
                    }
                }

                if (localDeleted && onlineDeleted)
                {
                    Debug.Log($"Map '{mapName}' deleted successfully.");
                    LoadSaveList();
                }
                else
                {
                    Debug.LogError($"Failed to delete map '{mapName}'. Local: {localDeleted}, Online: {onlineDeleted}");
                    OnLoadingCompleted?.Invoke();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete map '{mapName}': {e.Message}");
                OnLoadingCompleted?.Invoke();
            }
        }

        private void OnMapLoadRequestedHandler(string mapName, SaveSystem.WorldStorageSource source)
        {
            OnMapLoadRequestedAsync(mapName, source);
        }

        private async void OnMapLikesChanged(string mapName, int likes)
        {
            // Сохраняем лайки в Firebase через SaveSystem
            if (string.IsNullOrEmpty(mapName))
                return;

            if (_saveSystem == null)
                _saveSystem = FindAnyObjectByType<SaveSystem>();

            if (_saveSystem == null)
            {
                Debug.LogError("SaveSystem not found when trying to update likes.");
                return;
            }

            bool success = await _saveSystem.UpdateWorldLikesAsync(mapName, likes);
            if (!success)
            {
                Debug.LogError($"Не удалось обновить количество лайков карты '{mapName}'");
                LoadSaveList();
            }
        }

        private async void OnPublishRequested(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return;

            if (_saveSystem == null)
                _saveSystem = FindAnyObjectByType<SaveSystem>();

            if (_saveSystem == null)
            {
                Debug.LogError("SaveSystem not found when trying to publish/unpublish.");
                return;
            }

            bool isPublished = await _saveSystem.IsWorldPublishedAsync(mapName);

            if (isPublished)
            {
                bool success = await _saveSystem.DeleteWorldAsync(mapName);
                if (success)
                {
                    Debug.Log($"Map '{mapName}' unpublished successfully.");
                    UpdatePublishedStateForMap(mapName, false);
                }
                else
                {
                    Debug.LogError($"Failed to unpublish map '{mapName}'.");
                }
            }
            else
            {
                bool success = await _saveSystem.PublishLocalWorldToFirebaseAsync(mapName);
                if (success)
                {
                    Debug.Log($"Map '{mapName}' published successfully.");
                    UpdatePublishedStateForMap(mapName, true);
                    LoadSaveList();
                }
                else
                {
                    Debug.LogError($"Failed to publish map '{mapName}'.");
                }
            }
        }

        private async void CheckAndSetPublishedState(MapItemView mapItemView, string mapName)
        {
            if (_saveSystem == null)
                _saveSystem = FindAnyObjectByType<SaveSystem>();

            if (_saveSystem != null)
            {
                bool isPublished = await _saveSystem.IsWorldPublishedAsync(mapName);
                mapItemView.SetPublishedState(isPublished);
            }
        }

        private void UpdatePublishedStateForMap(string mapName, bool isPublished)
        {
            foreach (var mapItem in _mapItems)
            {
                if (mapItem?.View != null && mapItem.MapName == mapName)
                {
                    mapItem.View.SetPublishedState(isPublished);
                    break;
                }
            }
        }

        private async void OnMapLoadRequestedAsync(string mapName, SaveSystem.WorldStorageSource source)
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
                    bool loadSuccess =
                        await _saveSystem.LoadWorldAsync(mapName, false, OnWorldLoadProgress, source);

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
                    bool loadSuccess =
                        await _saveSystem.LoadWorldAsync(mapName, false, OnWorldLoadProgress, source);

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

        private void OnMapDeleteRequestedHandler(string mapName, SaveSystem.WorldStorageSource source)
        {
            Debug.Log($"Удаление карты: {mapName}");
            DeleteMap(mapName, source);
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

            // Показываем экран загрузки перед загрузкой сцены
            ShowLoadingPanel();
            _isLoadingScene = true;
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(_mapSceneIndex);

            if (asyncLoad == null)
            {
                Debug.LogError($"Не удалось начать загрузку сцены с индексом {_mapSceneIndex}");
                _isLoadingScene = false;
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

            _activeListSource = null;
            HideAllLists();
            UpdateListButtonsState();

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

            // Вызываем событие для отключения управления
            UIManager.NotifyFullscreenUIOpened();
        }

        public void HideLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }

            _isLoadingScene = false;

            // Вызываем событие для включения управления
            UIManager.NotifyFullscreenUIClosed();
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
