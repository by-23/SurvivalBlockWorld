using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class MapItemView : MonoBehaviour
{
    [Header("UI Components")] [SerializeField]
    private TextMeshProUGUI _mapNameText;

    [SerializeField] private Image _screenshotImage;
    [SerializeField] private Button _loadButton;
    [SerializeField] private TextMeshProUGUI _loadButtonText;

    private string _mapName;
    private string _screenshotPath;

    public System.Action<string> OnLoadMapRequested;

    private void Awake()
    {
        if (_loadButton != null)
        {
            _loadButton.onClick.AddListener(OnLoadButtonClicked);
        }
    }

    public void SetMapData(string mapName, string screenshotPath)
    {
        _mapName = mapName;
        _screenshotPath = screenshotPath;

        // Set map name
        if (_mapNameText != null)
        {
            _mapNameText.text = mapName;
        }

        // Load screenshot
        LoadScreenshot(screenshotPath);

        // Set button text
        if (_loadButtonText != null)
        {
            _loadButtonText.text = "Загрузить";
        }
    }

    private void LoadScreenshot(string screenshotPath)
    {
        if (_screenshotImage == null || string.IsNullOrEmpty(screenshotPath))
        {
            return;
        }

        try
        {
            if (File.Exists(screenshotPath))
            {
                byte[] imageData = File.ReadAllBytes(screenshotPath);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);

                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                _screenshotImage.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Screenshot file not found: {screenshotPath}");
                // You could set a default image here
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load screenshot from {screenshotPath}: {e.Message}");
        }
    }

    private void OnLoadButtonClicked()
    {
        OnLoadMapRequested?.Invoke(_mapName);
    }

    public void SetInteractable(bool interactable)
    {
        if (_loadButton != null)
        {
            _loadButton.interactable = interactable;
        }
    }
}
