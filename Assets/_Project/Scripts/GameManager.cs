using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;

public class GameManager : MonoBehaviour
{
    private static GameManager _instance;

    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameManager>();
            }

            return _instance;
        }
    }

    [Header("World Data")] public string CurrentWorldName = string.Empty;
    public bool PendingExitToMenu = false;

    [Header("Screenshot Display")] [SerializeField]
    private Image _loadingScreenshotImage;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            Application.targetFrameRate = 30;
            QualitySettings.vSyncCount = 0;
            DontDestroyOnLoad(this.gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    [Header("Build Mode")] public bool BuildModeActive = false;
    [SerializeField] private PlayerMovement _playerMovement; // кеш ссылки на движение игрока

    public void RegisterPlayerMovement(PlayerMovement pm)
    {
        if (pm != null)
            _playerMovement = pm;
    }

    public void ToggleBuildMode()
    {
        SetBuildMode(!BuildModeActive);
    }

    public void SetBuildMode(bool active)
    {
        BuildModeActive = active;
        if (_playerMovement != null)
            _playerMovement.SetLevitateMode(active);
    }

    public void LevitateUp(bool isPressed)
    {
        if (_playerMovement != null)
            _playerMovement.SetLevitateUp(isPressed);
    }

    public void LevitateDown(bool isPressed)
    {
        if (_playerMovement != null)
            _playerMovement.SetLevitateDown(isPressed);
    }

    public void LoadScreenshotToImage(string screenshotPath)
    {
        if (_loadingScreenshotImage == null || string.IsNullOrEmpty(screenshotPath))
        {
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
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    _loadingScreenshotImage.sprite = sprite;
                    _loadingScreenshotImage.enabled = true;
                }
                else
                {
                    Debug.LogWarning($"Failed to load image from: {screenshotPath}");
                    _loadingScreenshotImage.enabled = false;
                }
            }
            else
            {
                Debug.LogWarning($"Screenshot file not found: {screenshotPath}");
                _loadingScreenshotImage.enabled = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load screenshot from {screenshotPath}: {e.Message}");
            _loadingScreenshotImage.enabled = false;
        }
    }

    public void ClearScreenshotImage()
    {
        if (_loadingScreenshotImage != null)
        {
            _loadingScreenshotImage.sprite = null;
            _loadingScreenshotImage.enabled = false;
        }
    }
}
