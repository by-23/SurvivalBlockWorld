using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelLoadButton : MonoBehaviour
{
    [Header("Level Settings")]
    [SerializeField] private string levelName = ""; // Имя сцены для загрузки
    [SerializeField] private bool useBuildIndex = false; // Использовать индекс сборки вместо имени
    [SerializeField] private int buildIndex = 0; // Индекс сцены в Build Settings
    
    [Header("UI Settings")]
    [SerializeField] private Button button; // Ссылка на кнопку
    [SerializeField] private bool showLoadingScreen = true; // Показывать экран загрузки
    
    [Header("Loading Screen")]
    [SerializeField] private GameObject loadingScreenPrefab; // Префаб экрана загрузки
    
    private void Start()
    {
        // Если кнопка не назначена, попробуем найти её на этом объекте
        if (button == null)
        {
            button = GetComponent<Button>();
        }
        
        // Добавляем обработчик события нажатия
        if (button != null)
        {
            button.onClick.AddListener(LoadLevel);
        }
        else
        {
            Debug.LogError("LevelLoadButton: Button component not found!");
        }
    }
    
    private void OnDestroy()
    {
        // Убираем обработчик при уничтожении объекта
        if (button != null)
        {
            button.onClick.RemoveListener(LoadLevel);
        }
    }
    
    /// <summary>
    /// Загружает выбранный уровень
    /// </summary>
    public void LoadLevel()
    {
        if (useBuildIndex)
        {
            LoadLevelByIndex(buildIndex);
        }
        else
        {
            LoadLevelByName(levelName);
        }
    }
    
    /// <summary>
    /// Загружает уровень по имени сцены
    /// </summary>
    /// <param name="sceneName">Имя сцены</param>
    public void LoadLevelByName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("LevelLoadButton: Scene name is empty!");
            return;
        }
        
        Debug.Log($"Loading level: {sceneName}");
        
        if (showLoadingScreen && loadingScreenPrefab != null)
        {
            ShowLoadingScreen();
        }
        
        // Загружаем сцену
        SceneManager.LoadScene(sceneName);
    }
    
    /// <summary>
    /// Загружает уровень по индексу сборки
    /// </summary>
    /// <param name="index">Индекс сцены в Build Settings</param>
    public void LoadLevelByIndex(int index)
    {
        if (index < 0 || index >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError($"LevelLoadButton: Invalid build index {index}! Available scenes: {SceneManager.sceneCountInBuildSettings}");
            return;
        }
        
        string sceneName = GetSceneNameByBuildIndex(index);
        
        if (showLoadingScreen && loadingScreenPrefab != null)
        {
            ShowLoadingScreen();
        }
        
        // Загружаем сцену
        SceneManager.LoadScene(index);
    }
    
    /// <summary>
    /// Показывает экран загрузки
    /// </summary>
    private void ShowLoadingScreen()
    {
        if (loadingScreenPrefab != null)
        {
            Instantiate(loadingScreenPrefab);
        }
    }
    
    /// <summary>
    /// Получает имя сцены по индексу сборки
    /// </summary>
    /// <param name="buildIndex">Индекс сцены</param>
    /// <returns>Имя сцены</returns>
    private string GetSceneNameByBuildIndex(int buildIndex)
    {
        string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        return sceneName;
    }
    
    /// <summary>
    /// Устанавливает имя уровня для загрузки
    /// </summary>
    /// <param name="newLevelName">Новое имя уровня</param>
    public void SetLevelName(string newLevelName)
    {
        levelName = newLevelName;
        useBuildIndex = false;
    }
    
    /// <summary>
    /// Устанавливает индекс уровня для загрузки
    /// </summary>
    /// <param name="newBuildIndex">Новый индекс уровня</param>
    public void SetBuildIndex(int newBuildIndex)
    {
        buildIndex = newBuildIndex;
        useBuildIndex = true;
    }
    
    /// <summary>
    /// Проверяет, существует ли сцена с указанным именем
    /// </summary>
    /// <param name="sceneName">Имя сцены</param>
    /// <returns>True, если сцена существует</returns>
    public bool IsSceneExists(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (name == sceneName)
            {
                return true;
            }
        }
        return false;
    }
}
