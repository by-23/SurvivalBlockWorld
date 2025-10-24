using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class MainMenuUIController : MonoBehaviour
{
    [Header("Main Menu UI")] [SerializeField]
    private GameObject _mainMenuPanel;

    [Header("Main Menu Buttons")] [SerializeField]
    private Button _exitButton;

    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _playButton;

    [Header("Map Selection UI")] [SerializeField]
    private GameObject _mapSelectionPanel;

    [SerializeField] private GameObject _newGame;

    [SerializeField] private Button _backButton;
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private SaveListManager _saveListManager;

    [Header("Scene Settings")] [SerializeField]
    private int _mapSceneIndex = 0; // Индекс MapScene в Build Settings

    private void Start()
    {
        SetupButtons();
        SetupMapPanel();
    }

    private void SetupButtons()
    {
        if (_exitButton != null)
        {
            _exitButton.onClick.AddListener(OnExitButtonPressed);
        }

        if (_settingsButton != null)
        {
            _settingsButton.onClick.AddListener(OnSettingsButtonPressed);
        }

        if (_playButton != null)
        {
            _playButton.onClick.AddListener(OnPlayButtonPressed);
        }

        if (_backButton != null)
        {
            _backButton.onClick.AddListener(OnBackButtonPressed);
        }
    }

    private void SetupMapPanel()
    {
        // Initialize main menu as active
        if (_mainMenuPanel != null)
        {
            _mainMenuPanel.SetActive(true);
        }

        // Initialize map selection panel as inactive
        if (_mapSelectionPanel != null)
        {
            _mapSelectionPanel.SetActive(false);
        }

        // Initialize loading panel as inactive
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(false);
        }

        // Subscribe to save list manager events
        if (_saveListManager != null)
        {
            _saveListManager.OnMapLoadRequested += OnMapLoadRequested;
            _saveListManager.OnMapDeleteRequested += OnMapDeleteRequested;
            _saveListManager.OnLoadingStarted += OnLoadingStarted;
            _saveListManager.OnLoadingCompleted += OnLoadingCompleted;
        }
    }

    private void OnExitButtonPressed()
    {
        Debug.Log("Выход из игры");
        Application.Quit();

        // For editor testing
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnSettingsButtonPressed()
    {
        Debug.Log("Настройки - функционал пока не реализован");
        // TODO: Implement settings functionality
    }

    private void OnBackButtonPressed()
    {
        CloseMapSelection();
    }

    private void OnPlayButtonPressed()
    {
        // Hide main menu
        if (_mainMenuPanel != null)
        {
            _mainMenuPanel.SetActive(false);
        }

        // Show map selection panel
        if (_mapSelectionPanel != null)
        {
            _mapSelectionPanel.SetActive(true);
        }

        if (_newGame != null)
        {
            _newGame.SetActive(true);
        }

        // Show loading panel
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(true);
        }

        // Load save list (this will hide loading panel when done)
        if (_saveListManager != null)
        {
            _saveListManager.LoadSaveList();
        }
    }

    private async void OnMapLoadRequested(string mapName)
    {

        // Close the map selection panel
        if (_mapSelectionPanel != null)
        {
            _mapSelectionPanel.SetActive(false);
        }

        if (_newGame != null)
        {
            _newGame.SetActive(false);
        }

        // Show loading panel
        ShowLoadingPanel();

        // Сначала переходим к MapScene, а затем загружаем мир
        await LoadMapSceneAsync();

        // После загрузки сцены загружаем мир
        if (SaveSystem.Instance != null)
        {
            bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, OnWorldLoadProgress);

            if (loadSuccess)
            {
               
                // Убеждаемся, что InputManager и Player готовы после загрузки мира
                InputManager.ForceActivateInputManager();
                if (Player.Instance != null)
                {
                    Player.Instance.ForcePlayerControlMode();
                }

                HideLoadingPanel();
            }
            else
            {
                Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                HideLoadingPanel();
                // Return to map selection
                if (_mapSelectionPanel != null)
                {
                    _mapSelectionPanel.SetActive(true);
                }

                if (_newGame != null)
                {
                    _newGame.SetActive(true);
                }
            }
        }
    }

    private void OnMapDeleteRequested(string mapName)
    {
        Debug.Log($"Удаление карты: {mapName}");

        if (_saveListManager != null)
        {
            _saveListManager.DeleteMap(mapName);
        }
    }

    private void OnLoadingStarted()
    {
        ShowLoadingPanel();
    }

    private void OnLoadingCompleted()
    {
        HideLoadingPanel();
    }

    /// <summary>
    /// Обработчик прогресса загрузки мира
    /// </summary>
    /// <param name="progress">Прогресс от 0 до 1</param>
    private void OnWorldLoadProgress(float progress)
    {
        // Здесь можно обновить UI прогресса загрузки
    }

    /// <summary>
    /// Асинхронно загружает MapScene и ждет полной загрузки
    /// </summary>
    private async Task LoadMapSceneAsync()
    {
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

            // Ждем один кадр
            await Task.Yield();
        }
    }


    // Public method to close map selection panel and return to main menu
    public void CloseMapSelection()
    {
        // Hide map selection panel
        if (_mapSelectionPanel != null)
        {
            _mapSelectionPanel.SetActive(false);
        }

        if (_newGame != null)
        {
            _newGame.SetActive(false);
        }

        // Hide loading panel
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(false);
        }

        // Show main menu
        if (_mainMenuPanel != null)
        {
            _mainMenuPanel.SetActive(true);
        }
    }

    // Public methods to control loading panel
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

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (_saveListManager != null)
        {
            _saveListManager.OnMapLoadRequested -= OnMapLoadRequested;
            _saveListManager.OnMapDeleteRequested -= OnMapDeleteRequested;
            _saveListManager.OnLoadingStarted -= OnLoadingStarted;
            _saveListManager.OnLoadingCompleted -= OnLoadingCompleted;
        }
    }
}
