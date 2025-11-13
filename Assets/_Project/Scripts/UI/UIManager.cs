using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using AYellowpaper.SerializedCollections;
using System.Threading.Tasks;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Управляет основным игровым UI: кнопками действия игрока и панелью сохранения
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // Общие события для полноэкранных UI элементов
        public static event Action OnFullscreenUIOpened;
        public static event Action OnFullscreenUIClosed;

        [Header("Gameplay UI")] [SerializeField]
        private Laser _laser;

        [SerializeField] private PlayerMovement _playerMovement;
        [SerializeField] private RopeGenerator _ropeGenerator;
        [SerializeField] private VehicleForce _vehicleForce;
        [SerializeField] private Bomb _raycastDetoucher;
        [SerializeField] private Button _bombButton;
        [SerializeField] private Button _jumpButton;

        [SerializeField] private Button _moverButton;


        [Header("Save UI")] [SerializeField] private Button _saveButton;
        [SerializeField] private GameObject _savePanel;
        [SerializeField] private TMP_InputField _saveNameInput;
        [SerializeField] private Button _confirmSaveButton;
        [SerializeField] private Button _cancelSaveButton;

        [Header("Menu Exit UI")] [SerializeField]
        private Button _menuButton;

        [SerializeField] private GameObject _exitMenuPanel;
        [SerializeField] private TMP_InputField _exitMenuNameInput;
        [SerializeField] private Button _exitAndSaveButton;
        [SerializeField] private Button _exitWithoutSaveButton;
        [SerializeField] private Button _cancelExitButton;

        [Header("Load UI")] [SerializeField] private Button _closeLoadPanelButton;
        [SerializeField] private MapListUI mapListUI;
        [SerializeField] private GameObject _loadingPanel;
        [SerializeField] private Image _loadingScreenshotImage;

        [Header("Save System")] [SerializeField]
        private SaveSystem _saveSystem;

        [Header("Tool Selection")] [SerializeField] [SerializedDictionary("Button", "Tool Object")]
        private SerializedDictionary<Button, GameObject> _tools;

        [Header("Visualizer")] [SerializeField]
        private EntityVisualizer _entityVisualizer;

        [SerializeField] private string _activeToolName = "";

        [Header("Build Mode UI")] [SerializeField]
        private GameObject _levitateButtonsPanel;


        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Скрываем экран загрузки после полной загрузки сцены
            HideLoadingPanel();
        }

        /// <summary>
        /// Показывает экран загрузки и вызывает событие для отключения управления
        /// </summary>
        public void ShowLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(true);
            }

            OnFullscreenUIOpened?.Invoke();
        }

        /// <summary>
        /// Скрывает экран загрузки и вызывает событие для включения управления
        /// </summary>
        public void HideLoadingPanel()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }

            OnFullscreenUIClosed?.Invoke();
        }

        /// <summary>
        /// Уведомляет об открытии полноэкранного UI элемента
        /// </summary>
        public static void NotifyFullscreenUIOpened()
        {
            OnFullscreenUIOpened?.Invoke();
        }

        /// <summary>
        /// Уведомляет о закрытии полноэкранного UI элемента
        /// </summary>
        public static void NotifyFullscreenUIClosed()
        {
            OnFullscreenUIClosed?.Invoke();
        }

        /// <summary>
        /// Вызывает событие начала загрузки и показывает экран загрузки
        /// </summary>
        public static void NotifyLoadingStarted()
        {
            // Показываем экран загрузки, если есть экземпляр UIManager
            UIManager instance = FindFirstObjectByType<UIManager>();
            if (instance != null)
            {
                instance.ShowLoadingPanel();
            }
            else
            {
                // Если экземпляр не найден, только вызываем событие
                NotifyFullscreenUIOpened();
            }
        }

        /// <summary>
        /// Вызывает событие окончания загрузки (для обратной совместимости)
        /// </summary>
        public static void NotifyLoadingFinished()
        {
            NotifyFullscreenUIClosed();
        }

        /// <summary>
        /// Загружает скриншот в изображение экрана загрузки
        /// </summary>
        public void LoadScreenshotToImage(string screenshotPath)
        {
            if (_loadingScreenshotImage == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(screenshotPath))
            {
                // Если путь пустой, устанавливаем черный фон
                _loadingScreenshotImage.color = Color.black;
                return;
            }

            try
            {
                if (File.Exists(screenshotPath))
                {
                    byte[] imageData = File.ReadAllBytes(screenshotPath);
                    Texture2D texture = new Texture2D(2, 2);

                    if (texture.LoadImage(imageData))
                    {
                        _loadingScreenshotImage.color = Color.white;
                        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f));
                        _loadingScreenshotImage.sprite = sprite;
                        _loadingScreenshotImage.enabled = true;
                        // Белый фон для видимости изображения
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to load image from: {screenshotPath}");
                        _loadingScreenshotImage.enabled = false;
                        // Черный фон при ошибке загрузки
                        _loadingScreenshotImage.color = Color.black;
                    }
                }
                else
                {
                    Debug.LogWarning($"Screenshot file not found: {screenshotPath}");
                    _loadingScreenshotImage.enabled = false;
                    // Черный фон если файл не найден
                    _loadingScreenshotImage.color = Color.black;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load screenshot from {screenshotPath}: {e.Message}");
                _loadingScreenshotImage.enabled = false;
                // Черный фон при исключении
                _loadingScreenshotImage.color = Color.black;
            }
        }

        /// <summary>
        /// Очищает изображение скриншота на экране загрузки
        /// </summary>
        public void ClearScreenshotImage()
        {
            if (_loadingScreenshotImage != null)
            {
                _loadingScreenshotImage.sprite = null;
                _loadingScreenshotImage.enabled = false;
                // Устанавливаем черный фон при очистке
                _loadingScreenshotImage.color = Color.black;
            }
        }

        private void Start()
        {
            SetupButtons();
            SetupPanels();
            SetupToolButtons();
            InitializeVisualizer();
            InitializeBuildModeUI();
        }

        private void SetupButtons()
        {
            if (_saveButton != null)
            {
                _saveButton.onClick.AddListener(OnSaveButtonPressed);
            }

            if (_menuButton != null)
            {
                _menuButton.onClick.AddListener(RequestExitToMenu);
            }

            if (_confirmSaveButton != null)
            {
                _confirmSaveButton.onClick.AddListener(OnConfirmSaveButtonPressed);
            }

            if (_cancelSaveButton != null)
            {
                _cancelSaveButton.onClick.AddListener(OnCancelSaveButtonPressed);
            }

            if (_exitAndSaveButton != null)
            {
                _exitAndSaveButton.onClick.AddListener(OnExitAndSavePressed);
            }

            if (_exitWithoutSaveButton != null)
            {
                _exitWithoutSaveButton.onClick.AddListener(OnExitWithoutSavePressed);
            }

            if (_cancelExitButton != null)
            {
                _cancelExitButton.onClick.AddListener(OnCancelExitPressed);
            }

            if (_closeLoadPanelButton != null)
            {
                _closeLoadPanelButton.onClick.AddListener(OnCloseLoadPanelButtonPressed);
            }

            if (_bombButton != null)
            {
                _bombButton.onClick.AddListener(OnBombButtonPressed);
            }

            if (_jumpButton != null)
            {
                _jumpButton.onClick.AddListener(OnJumpButtonPressed);
            }
        }


        // Удобные методы для EventTrigger (PointerDown/Up/Exit не передают bool)
        public void OnLevitateUpPointerDown()
        {
            if (GameManager.Instance != null) GameManager.Instance.LevitateUp(true);
        }

        public void OnLevitateUpPointerUp()
        {
            if (GameManager.Instance != null) GameManager.Instance.LevitateUp(false);
        }

        public void OnLevitateDownPointerDown()
        {
            if (GameManager.Instance != null) GameManager.Instance.LevitateDown(true);
        }

        public void OnLevitateDownPointerUp()
        {
            if (GameManager.Instance != null) GameManager.Instance.LevitateDown(false);
        }

        private void InitializeBuildModeUI()
        {
            bool active = GameManager.Instance != null && GameManager.Instance.BuildModeActive;
            UpdateBuildModeUI(active);
        }

        private void UpdateBuildModeUI(bool buildActive)
        {
            _levitateButtonsPanel.gameObject.SetActive(buildActive);

            // Скрываем кнопку прыжка в режиме строительства
            if (_jumpButton != null)
            {
                _jumpButton.gameObject.SetActive(!buildActive);
            }

            // Отключаем кнопку поднятия объектов в режиме строительства
            if (_moverButton != null)
            {
                _moverButton.interactable = !buildActive;
            }
        }

        private void SetupPanels()
        {
            if (_savePanel != null)
            {
                _savePanel.SetActive(false);
            }

            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }

            // Не вызываем HideLoadingPanel здесь, так как это только инициализация
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        private void SetupToolButtons()
        {
            if (_tools != null && _tools.Count > 0)
            {
                // Добавляем обработчики для каждой кнопки
                foreach (var kvp in _tools)
                {
                    if (kvp.Key != null)
                    {
                        var button = kvp.Key;
                        var toolObject = kvp.Value;
                        button.onClick.AddListener(() => OnToolButtonPressed(toolObject));
                    }
                }
            }
        }

        private void InitializeVisualizer()
        {
            // Initialize visualizer if not assigned
            if (_entityVisualizer == null)
            {
                _entityVisualizer = FindFirstObjectByType<EntityVisualizer>();

                // If still null, create a new one
                if (_entityVisualizer == null)
                {
                    GameObject visualizerObj = new GameObject("EntityVisualizer");
                    visualizerObj.transform.SetParent(transform);
                    _entityVisualizer = visualizerObj.AddComponent<EntityVisualizer>();

                    // Set camera reference - try to get from PlayerCamera component
                    if (Player.Instance != null && Player.Instance._playerCamera != null)
                    {
                        Camera playerCam = Player.Instance._playerCamera.GetComponent<Camera>();
                        if (playerCam != null)
                        {
                            _entityVisualizer.SetCamera(playerCam);
                        }
                        else
                        {
                            _entityVisualizer.SetCamera(Camera.main);
                        }
                    }
                    else
                    {
                        _entityVisualizer.SetCamera(Camera.main);
                    }
                }
            }
        }

        private void OnToolButtonPressed(GameObject selectedTool)
        {
            if (_tools == null)
                return;

            if (selectedTool == null)
            {
                Debug.LogWarning("Selected tool is null!");
                return;
            }

            // Отменяем ghost entity при переключении инструмента
            var entityManager = FindFirstObjectByType<EntityManager>();
            if (entityManager != null)
            {
                entityManager.CancelGhost();
            }

            // Скрываем ghost куба при переключении инструмента
            var cubeCreator = FindFirstObjectByType<CubeCreator>();
            if (cubeCreator != null)
            {
                cubeCreator.HideGhostCube();
            }

            // Отключаем визуализатор перед переключением инструментов
            if (_entityVisualizer != null)
            {
                _entityVisualizer.Deactivate();
            }

            // Отключаем все объекты
            foreach (var toolObject in _tools.Values)
            {
                if (toolObject != null)
                {
                    toolObject.SetActive(false);
                }
            }

            // Включаем выбранный объект
            if (selectedTool != null)
            {
                selectedTool.SetActive(true);
                _activeToolName = selectedTool.name;

                // Автоматически включаем building mode при выборе инструмента строительства кубов
                bool isCubeBuildingTool = selectedTool.name.Contains("CubeCreator");
                if (GameManager.Instance != null)
                {
                    if (isCubeBuildingTool)
                    {
                        if (!GameManager.Instance.BuildModeActive)
                        {
                            GameManager.Instance.SetBuildMode(true);
                            UpdateBuildModeUI(true);
                        }
                    }
                    else
                    {
                        // Выключаем building mode при выборе других инструментов
                        if (GameManager.Instance.BuildModeActive)
                        {
                            GameManager.Instance.SetBuildMode(false);
                            GameManager.Instance.LevitateUp(false);
                            GameManager.Instance.LevitateDown(false);
                            UpdateBuildModeUI(false);
                        }
                        else
                        {
                            // Включаем кнопку поднятия объектов при смене инструмента (если режим строительства не активен)
                            UpdateBuildModeUI(false);
                        }
                    }
                }

                // Активируем визуализатор если выбран инструмент "SaveSpawn" или "Move"
                if (_entityVisualizer != null &&
                    (selectedTool.name.Contains("SaveSpawn") || selectedTool.name.Contains("Move")))
                {
                    _entityVisualizer.ActivateForTool(_activeToolName);
                }
            }
        }

        // Button functions
        private void OnSaveButtonPressed()
        {
            if (_savePanel != null)
            {
                _savePanel.SetActive(true);

                // Отключаем управление при открытии полноэкранного UI
                UIManager.NotifyFullscreenUIOpened();

                if (_saveNameInput != null)
                {
                    _saveNameInput.text = "";
                    _saveNameInput.interactable = true;
                }
            }
        }

        private string GetWorldNameFromInput(TMP_InputField inputField)
        {
            if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
            {
                Debug.LogError("Map name cannot be empty.");
                return null;
            }

            return inputField.text.Trim();
        }

        private bool EnsureSaveSystemInstance()
        {
            if (_saveSystem == null)
                _saveSystem = FindAnyObjectByType<SaveSystem>();

            if (_saveSystem == null)
            {
                Debug.LogError("SaveSystem not found!");
                return false;
            }

            return true;
        }

        private string ResolveScreenshotPath(string worldName)
        {
            if (_saveSystem != null && _saveSystem.Config != null)
            {
                return _saveSystem.Config.GetWorldScreenshotPath(worldName);
            }

            string sanitized = SaveConfig.SanitizeFileName(worldName);
            return Path.Combine(Application.persistentDataPath, $"{sanitized}.png");
        }

        private async Task<bool> ExecuteSaveFlowAsync(string worldName, SaveSystem.SaveDestination destination,
            bool keepLoadingPanelActive = false)
        {
            if (!EnsureSaveSystemInstance())
            {
                return false;
            }

            ShowLoadingPanel();

            bool saveSucceeded = false;

            try
            {
                string existingScreenshotPath = ResolveScreenshotPath(worldName);
                if (File.Exists(existingScreenshotPath))
                {
                    LoadScreenshotToImage(existingScreenshotPath);
                }

                bool success = await _saveSystem.SaveWorldAsync(worldName, destination);

                if (success)
                {
                    saveSucceeded = true;

                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.CurrentWorldName = worldName;
                    }

                    string updatedScreenshotPath = ResolveScreenshotPath(worldName);
                    if (File.Exists(updatedScreenshotPath))
                    {
                        LoadScreenshotToImage(updatedScreenshotPath);
                    }
                }
                else
                {
                    Debug.LogError($"Failed to save world '{worldName}'. Check SaveSystem logs above for details.");

                    if (_saveSystem != null && _saveSystem.Config != null)
                    {
                        bool localEnabled = _saveSystem.Config.useLocalCache;
                        bool firebaseEnabled = _saveSystem.Config.useFirebase;
                        bool requestLocal = destination == SaveSystem.SaveDestination.Local ||
                                            destination == SaveSystem.SaveDestination.LocalAndOnline;
                        bool requestOnline = destination == SaveSystem.SaveDestination.Online ||
                                             destination == SaveSystem.SaveDestination.LocalAndOnline;

                        if (requestLocal && !localEnabled)
                        {
                            Debug.LogError("Local save requested but useLocalCache is disabled in SaveConfig.");
                        }

                        if (requestOnline && !firebaseEnabled)
                        {
                            Debug.LogError("Online save requested but useFirebase is disabled in SaveConfig.");
                        }

                        if (!localEnabled && !firebaseEnabled)
                        {
                            Debug.LogError(
                                "Both local and Firebase saves are disabled in SaveConfig. Enable at least one.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save world '{worldName}': {e.Message}");
            }
            finally
            {
                if (!keepLoadingPanelActive || !saveSucceeded)
                {
                    HideLoadingPanel();
                }
            }

            return saveSucceeded;
        }

        private async void OnConfirmSaveButtonPressed()
        {
            string worldName = GetWorldNameFromInput(_saveNameInput);
            if (string.IsNullOrEmpty(worldName))
            {
                return;
            }

            if (_savePanel != null)
            {
                _savePanel.SetActive(false);
            }

            await ExecuteSaveFlowAsync(worldName, SaveSystem.SaveDestination.Local);
        }

        private void OnCancelSaveButtonPressed()
        {
            if (_savePanel != null)
            {
                _savePanel.SetActive(false);
            }

            // Включаем управление при закрытии полноэкранного UI
            UIManager.NotifyFullscreenUIClosed();
        }

        public void RequestExitToMenu()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(true);

                // Отключаем управление при открытии полноэкранного UI
                UIManager.NotifyFullscreenUIOpened();

                // Настраиваем поле ввода в зависимости от наличия названия
                if (_exitMenuNameInput != null)
                {
                    if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.CurrentWorldName))
                    {
                        // Название есть - показываем его, поле неактивно
                        _exitMenuNameInput.text = GameManager.Instance.CurrentWorldName;
                        _exitMenuNameInput.interactable = false;
                    }
                    else
                    {
                        // Названия нет - поле активно для ввода
                        _exitMenuNameInput.text = "";
                        _exitMenuNameInput.interactable = true;
                        _exitMenuNameInput.Select();
                    }
                }
            }
        }

        private async void OnExitAndSavePressed()
        {
            string worldName = null;

            // Проверяем название из поля ввода в панели выхода
            if (_exitMenuNameInput != null && !string.IsNullOrWhiteSpace(_exitMenuNameInput.text))
            {
                worldName = _exitMenuNameInput.text.Trim();
            }
            else if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.CurrentWorldName))
            {
                // Если поле ввода пустое, но есть сохраненное название
                worldName = GameManager.Instance.CurrentWorldName;
            }

            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogError("Map name cannot be empty.");
                return;
            }

            // Закрываем панель выхода
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }

            bool saveSuccess = await ExecuteSaveFlowAsync(worldName, SaveSystem.SaveDestination.Local, true);
            if (!saveSuccess)
            {
                HideLoadingPanel();
                return;
            }

            // Асинхронно загружаем меню
            // Экран загрузки будет скрыт автоматически через OnSceneLoaded после полной загрузки
            await SceneLoadHelper.LoadSceneAsync(0);
        }


        private async void OnExitWithoutSavePressed()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }

            // Показываем экран загрузки
            ShowLoadingPanel();

            // Асинхронно загружаем меню
            // Экран загрузки будет скрыт автоматически через OnSceneLoaded после полной загрузки
            await SceneLoadHelper.LoadSceneAsync(0);
        }

        private void OnCancelExitPressed()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }

            // Включаем управление при закрытии полноэкранного UI
            UIManager.NotifyFullscreenUIClosed();
        }

        private void OnCloseLoadPanelButtonPressed()
        {
            if (mapListUI != null)
            {
                mapListUI.CloseMapList();
            }
        }

        public void OnLaserRemoveButtonPressed(bool isPressed)
        {
            if (_laser != null)
            {
                _laser.Press(isPressed);
            }
        }

        public void OnJumpButtonPressed()
        {
            // Не выполняем прыжок в режиме строительства
            if (GameManager.Instance != null && GameManager.Instance.BuildModeActive)
            {
                return;
            }

            if (_playerMovement != null)
            {
                _playerMovement.Jump();
            }
        }

        public void OnVehicleBrakeButtonPressed()
        {
            if (_vehicleForce != null && _vehicleForce._Active)
            {
                _vehicleForce.Break();
            }
        }

        public void OnHookButtonPressed()
        {
            if (_ropeGenerator != null)
            {
                _ropeGenerator.Hook();
            }
        }

        public void OnCarButtonPressed()
        {
            // This likely needs more logic to toggle entering/exiting
        }

        public void OnBombButtonPressed()
        {
            if (_raycastDetoucher != null)
            {
                _raycastDetoucher.Raycast();
            }
        }
    }
}


