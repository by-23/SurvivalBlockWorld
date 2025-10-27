using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// UI контроллер для работы со списком карт
    /// </summary>
    public class MapListUI : MonoBehaviour
    {
        [Header("Save List Manager")] [SerializeField]
        protected MapList mapList;

        [Header("Map Selection UI")] 
        [SerializeField] private GameObject _mapsMenu;
        [SerializeField] private GameObject _newGame;
        [SerializeField] private GameObject _loadingPanel;

        [Header("Scene Settings")] [SerializeField]
        private int _mapSceneIndex = 0; // Индекс MapScene в Build Settings

        private void Start()
        {
            SubscribeToSaveListEvents();
            
        }

        private void OnDestroy()
        {
            UnsubscribeFromSaveListEvents();
        }

        /// <summary>
        /// Подписывается на события MapList
        /// </summary>
        private void SubscribeToSaveListEvents()
        {
            if (mapList != null)
            {
                mapList.OnMapLoadRequested += OnMapLoadRequested;
                mapList.OnMapDeleteRequested += OnMapDeleteRequested;
                mapList.OnLoadingStarted += OnLoadingStarted;
                mapList.OnLoadingCompleted += OnLoadingCompleted;
            }
        }

        /// <summary>
        /// Отписывается от событий MapList
        /// </summary>
        private void UnsubscribeFromSaveListEvents()
        {
            if (mapList != null)
            {
                mapList.OnMapLoadRequested -= OnMapLoadRequested;
                mapList.OnMapDeleteRequested -= OnMapDeleteRequested;
                mapList.OnLoadingStarted -= OnLoadingStarted;
                mapList.OnLoadingCompleted -= OnLoadingCompleted;
            }
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

            if (mapList != null)
            {
                mapList.LoadSaveList();
            }
            else
            {
                Debug.LogError("MapListUIController: SaveListManager is NULL!");
            }
        }

        /// <summary>
        /// Обработчик запроса загрузки карты
        /// </summary>
        /// <param name="mapName">Имя карты</param>
        private void OnMapLoadRequested(string mapName)
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

            // Show loading panel
            ShowLoadingPanel();

            // Проверяем, находимся ли мы в меню (сцена с индексом 0)
            bool isInMenu = SceneManager.GetActiveScene().buildIndex == 0;

            if (isInMenu)
            {
                // Если в меню - сначала загружаем сцену, затем мир
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
                        CloseMapList();
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();
                       
                        gameObject.SetActive(true);
                        

                        if (_newGame != null)
                        {
                            _newGame.SetActive(true);
                        }
                    }
                }
            }
            else
            {
                // Если не в меню - загружаем только мир без смены сцены
                if (SaveSystem.Instance != null)
                {
                    bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, false, OnWorldLoadProgress);

                    if (loadSuccess)
                    {
                        // Убеждаемся, что InputManager и Player готовы после загрузки мира
                        InputManager.ForceActivateInputManager();
                        if (Player.Instance != null)
                        {
                            Player.Instance.ForcePlayerControlMode();
                        }

                        HideLoadingPanel();
                        CloseMapList();
                    }
                    else
                    {
                        Debug.LogError($"Ошибка загрузки мира '{mapName}'");
                        HideLoadingPanel();
                        
                        gameObject.SetActive(true);

                        if (_newGame != null)
                        {
                            _newGame.SetActive(true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Обработчик запроса удаления карты
        /// </summary>
        /// <param name="mapName">Имя карты</param>
        private void OnMapDeleteRequested(string mapName)
        {
            Debug.Log($"Удаление карты: {mapName}");
            if (mapList != null)
            {
                mapList.DeleteMap(mapName);
            }
        }

        /// <summary>
        /// Обработчик начала загрузки
        /// </summary>
        private void OnLoadingStarted()
        {
            ShowLoadingPanel();
        }

        /// <summary>
        /// Обработчик завершения загрузки
        /// </summary>
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
                CloseMapList();
                // Ждем один кадр
                await Task.Yield();
            }
        }


        // Public method to close map selection panel and return to main menu
        public void CloseMapList()
        {
            gameObject.SetActive(false);

            if (_newGame != null)
            {
                _newGame.SetActive(false);
            }

            // Hide loading panel
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
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
    }
}
