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
        [Header("Gameplay UI")] [SerializeField]
        private Laser _laser;

        [SerializeField] private PlayerMovement _playerMovement;
        [SerializeField] private RopeGenerator _ropeGenerator;
        [SerializeField] private VehicleForce _vehicleForce;
        [SerializeField] private Bomb _raycastDetoucher;
        [SerializeField] private Button _bombButton;

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

        [Header("Tool Selection")] [SerializeField] [SerializedDictionary("Button", "Tool Object")]
        private SerializedDictionary<Button, GameObject> _tools;

        [Header("Visualizer")] [SerializeField]
        private EntityVisualizer _entityVisualizer;

        [SerializeField] private string _activeToolName = "";

        private void Start()
        {
            SetupButtons();
            SetupPanels();
            SetupToolButtons();
            InitializeVisualizer();
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
                _entityVisualizer = FindObjectOfType<EntityVisualizer>();

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

                // Активируем визуализатор если выбран инструмент "SaveSpawn"
                if (_entityVisualizer != null && selectedTool.name.Contains("SaveSpawn"))
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
                if (_saveNameInput != null)
                {
                    _saveNameInput.text = "";
                    _saveNameInput.interactable = true;
                }
            }
        }

        private void OnConfirmSaveButtonPressed()
        {
            if (_saveNameInput != null && !string.IsNullOrEmpty(_saveNameInput.text))
            {
                string worldName = _saveNameInput.text.Trim();
                SaveSystem.Instance.SaveWorld(worldName);

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.CurrentWorldName = worldName;
                }
            }
            else
            {
                Debug.LogError("Save name cannot be empty.");
            }

            if (_savePanel != null)
            {
                _savePanel.SetActive(false);
            }
        }

        private void OnCancelSaveButtonPressed()
        {
            if (_savePanel != null)
            {
                _savePanel.SetActive(false);
            }
        }

        public void RequestExitToMenu()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(true);

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

            // Показываем панель загрузки
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(true);
            }

            // Загружаем скриншот сразу при начале загрузки
            if (GameManager.Instance != null)
            {
                string screenshotPath = System.IO.Path.Combine(Application.persistentDataPath, worldName + ".png");
                GameManager.Instance.LoadScreenshotToImage(screenshotPath);
            }

            // Сохраняем карту
            if (SaveSystem.Instance != null)
            {
                bool success = await SaveSystem.Instance.SaveWorldAsync(worldName);
                if (!success)
                {
                    Debug.LogError("Failed to save world.");
                    if (_loadingPanel != null)
                    {
                        _loadingPanel.SetActive(false);
                    }

                    return;
                }

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.CurrentWorldName = worldName;

                    // Обновляем скриншот после сохранения (на случай если он изменился)
                    string screenshotPath = System.IO.Path.Combine(Application.persistentDataPath, worldName + ".png");
                    GameManager.Instance.LoadScreenshotToImage(screenshotPath);
                }
            }
            else
            {
                Debug.LogError("SaveSystem.Instance is null!");
                if (_loadingPanel != null)
                {
                    _loadingPanel.SetActive(false);
                }

                return;
            }

            // Загружаем меню
            SceneManager.LoadScene(0);
        }


        private void OnExitWithoutSavePressed()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }

            SceneManager.LoadScene(0);
        }

        private void OnCancelExitPressed()
        {
            if (_exitMenuPanel != null)
            {
                _exitMenuPanel.SetActive(false);
            }
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


