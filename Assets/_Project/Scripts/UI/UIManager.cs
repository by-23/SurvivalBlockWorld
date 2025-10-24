using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Gameplay UI")] [SerializeField]
    private Laser _laser;

    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RopeGenerator _ropeGenerator;
    [SerializeField] private VehicleForce _vehicleForce;

    [Header("Buttons")] [SerializeField] private Button _saveButton;
    [SerializeField] private Button _mapsButton;

    [Header("Save UI")] [SerializeField] private GameObject _savePanel;
    [SerializeField] private TMP_InputField _saveNameInput;
    [SerializeField] private Button _confirmSaveButton;
    [SerializeField] private Button _cancelSaveButton;

    [Header("Load UI")] [SerializeField] private GameObject _loadPanel;
    [SerializeField] private SaveListManager _saveListManager;
    [SerializeField] private Button _closeLoadPanelButton;

    [Header("Scene Settings")] [SerializeField]
    private int _mapSceneIndex = 0; // Индекс MapScene в Build Settings


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        if (_saveButton != null)
        {
            _saveButton.onClick.AddListener(OnSaveButtonPressed);
        }

        if (_mapsButton != null)
        {
            _mapsButton.onClick.AddListener(OnLoadButtonPressed);
        }

        if (_confirmSaveButton != null)
        {
            _confirmSaveButton.onClick.AddListener(OnConfirmSaveButtonPressed);
        }

        if (_cancelSaveButton != null)
        {
            _cancelSaveButton.onClick.AddListener(OnCancelSaveButtonPressed);
        }

        if (_closeLoadPanelButton != null)
        {
            _closeLoadPanelButton.onClick.AddListener(OnCloseLoadPanelButtonPressed);
        }

        if (_savePanel != null)
        {
            _savePanel.SetActive(false);
        }

        if (_loadPanel != null)
        {
            _loadPanel.SetActive(false);
        }

        // Subscribe to save list manager events
        if (_saveListManager != null)
        {
            _saveListManager.OnMapLoadRequested += OnMapLoadRequested;
            _saveListManager.OnMapDeleteRequested += OnMapDeleteRequested;
        }
    }

    // Button functions
    public void OnSaveButtonPressed()
    {
        if (_savePanel != null)
        {
            _savePanel.SetActive(true);
            // Optionally clear previous input
            if (_saveNameInput != null)
            {
                _saveNameInput.text = "";
            }
        }
    }

    public void OnLoadButtonPressed()
    {
        if (_loadPanel != null)
        {
            _loadPanel.SetActive(true);
            if (_saveListManager != null)
            {
                _saveListManager.LoadSaveList();
            }
        }
    }

    private void OnConfirmSaveButtonPressed()
    {
        if (_saveNameInput != null && !string.IsNullOrEmpty(_saveNameInput.text))
        {
            string worldName = _saveNameInput.text;
            SaveSystem.Instance.SaveWorld(worldName);
        }
        else
        {
            Debug.LogError("Save name cannot be empty.");
            // Here you might want to show a message to the user
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

    private void OnCloseLoadPanelButtonPressed()
    {
        if (_loadPanel != null)
        {
            _loadPanel.SetActive(false);
        }
    }

    private async void OnMapLoadRequested(string mapName)
    {
        Debug.Log($"Loading map: {mapName}");

        // Close the load panel after starting the load
        if (_loadPanel != null)
        {
            _loadPanel.SetActive(false);
        }

        // Сначала переходим к MapScene, а затем загружаем мир
        await LoadMapSceneAsync();

        // После загрузки сцены загружаем мир
        if (SaveSystem.Instance != null)
        {
            bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, OnWorldLoadProgress);

            if (loadSuccess)
            {
                Debug.Log($"Мир '{mapName}' успешно загружен в MapScene");

                // Убеждаемся, что InputManager и Player готовы после загрузки мира
                InputManager.ForceActivateInputManager();
                if (Player.Instance != null)
                {
                    Player.Instance.ForcePlayerControlMode();
                }
            }
            else
            {
                Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                // Return to load panel
                if (_loadPanel != null)
                {
                    _loadPanel.SetActive(true);
                }
            }
        }
    }

    private void OnMapDeleteRequested(string mapName)
    {
        Debug.Log($"Deleting map: {mapName}");
        if (_saveListManager != null)
        {
            _saveListManager.DeleteMap(mapName);
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

    /// <summary>
    /// Обработчик прогресса загрузки мира
    /// </summary>
    /// <param name="progress">Прогресс от 0 до 1</param>
    private void OnWorldLoadProgress(float progress)
    {
        Debug.Log($"Прогресс загрузки мира: {progress * 100:F1}%");
        // Здесь можно обновить UI прогресса загрузки
    }

    /// <summary>
    /// Асинхронно загружает MapScene и ждет полной загрузки
    /// </summary>
    private async Task LoadMapSceneAsync()
    {
        Debug.Log($"Начинаем загрузку сцены по индексу: {_mapSceneIndex}");

        // Проверяем валидность индекса
        if (_mapSceneIndex < 0 || _mapSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError(
                $"Неверный индекс сцены: {_mapSceneIndex}. Доступно сцен: {SceneManager.sceneCountInBuildSettings}");
            return;
        }

        // Загружаем сцену асинхронно по индексу
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(_mapSceneIndex);

        if (asyncLoad == null)
        {
            Debug.LogError($"Не удалось начать загрузку сцены с индексом {_mapSceneIndex}");
            return;
        }

        // Ждем завершения загрузки
        while (!asyncLoad.isDone)
        {
            float progress = asyncLoad.progress;
            Debug.Log($"Прогресс загрузки сцены: {progress * 100:F1}%");

            // Обновляем UI прогресса загрузки сцены
            OnSceneLoadProgress(progress);

            // Ждем один кадр
            await Task.Yield();
        }

        Debug.Log($"Сцена с индексом {_mapSceneIndex} успешно загружена");
    }

    /// <summary>
    /// Обработчик прогресса загрузки сцены
    /// </summary>
    /// <param name="progress">Прогресс от 0 до 1</param>
    private void OnSceneLoadProgress(float progress)
    {
        Debug.Log($"Прогресс загрузки сцены: {progress * 100:F1}%");
        // Здесь можно обновить UI прогресса загрузки сцены
    }
}
