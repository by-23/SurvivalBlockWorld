using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Threading.Tasks;
using System;

public class MapItemView : MonoBehaviour
{
    [Header("UI Components")] [SerializeField]
    private TextMeshProUGUI _mapNameText;

    [SerializeField] private Image _screenshotImage;
    [SerializeField] private Image _likeIcon;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _deleteButton;
    [SerializeField] private Button _likeButton;
    [SerializeField] private Button _publishButton;
    [SerializeField] private TextMeshProUGUI _publishButtonText;
    [SerializeField] private TextMeshProUGUI _likesText;

    [Header("Like State")] [SerializeField]
    private int _likesCount;

    [SerializeField] private Color _likedColor = Color.white;
    [SerializeField] private Color _unlikedColor = Color.black;
    [SerializeField] private Color _publishedColor = Color.green;
    [SerializeField] private Color _unpublishedColor = Color.gray;

    private string _mapName;
    private string _screenshotPath;
    private bool _isLiked;
    private bool _isPublished;
    private Graphic _likeButtonGraphic;
    private Graphic _publishButtonGraphic;

    public System.Action<string> OnLoadMapRequested;
    public System.Action<string> OnDeleteMapRequested;
    public System.Action<string, int> OnLikeValueChanged;
    public System.Action<string> OnPublishRequested;

    Sprite _currentMapIcon;

    private void Awake()
    {
        if (_loadButton != null)
        {
            _loadButton.onClick.AddListener(OnLoadButtonClicked);
        }

        if (_deleteButton != null)
        {
            _deleteButton.onClick.AddListener(OnDeleteButtonClicked);
        }

        if (_likeButton != null)
        {
            _likeButton.onClick.AddListener(OnLikeButtonClicked);
            _likeButtonGraphic = _likeIcon;
        }

        if (_publishButton != null)
        {
            _publishButton.onClick.AddListener(OnPublishButtonClicked);
            _publishButtonGraphic = _publishButton.targetGraphic;

            if (_publishButtonText == null)
            {
                _publishButtonText = _publishButton.GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        UpdateLikesUI();
        UpdatePublishUI();
    }

    public Sprite GetScreenshotSprite()
    {
        return _screenshotImage?.sprite;
    }

    public void SetMapData(string mapName, string screenshotPath, int likesCount)
    {
        _mapName = mapName;
        _screenshotPath = screenshotPath;
        _likesCount = Mathf.Max(0, likesCount);
        _isLiked = false;

        if (_mapNameText != null)
        {
            _mapNameText.text = mapName;
            _mapNameText.ForceMeshUpdate();
        }

        // Load screenshot
        LoadScreenshot(screenshotPath);

        UpdateLikesUI();
    }

    public void SetLikedState(bool isLiked)
    {
        _isLiked = isLiked;
        UpdateLikeButtonVisual();
    }

    public void UpdateLikesCount(int newCount)
    {
        _likesCount = Mathf.Max(0, newCount);
        UpdateLikesText();
    }

    private void LoadScreenshot(string screenshotPath)
    {
        if (_screenshotImage == null || string.IsNullOrEmpty(screenshotPath))
        {
            return;
        }

        _ = LoadScreenshotAsync(screenshotPath);
    }

    private async Task LoadScreenshotAsync(string screenshotPath)
    {
        if (_screenshotImage == null || string.IsNullOrEmpty(screenshotPath))
        {
            return;
        }

        try
        {
            byte[] imageData = null;

            if (IsUrl(screenshotPath))
            {
                imageData = await DownloadImageFromUrlAsync(screenshotPath);
            }
            else if (File.Exists(screenshotPath))
            {
                imageData = await File.ReadAllBytesAsync(screenshotPath);
            }
            else
            {
                Debug.LogWarning($"Screenshot file not found: {screenshotPath}");
                return;
            }

            if (imageData == null || imageData.Length == 0)
            {
                Debug.LogWarning($"Screenshot data is empty: {screenshotPath}");
                return;
            }

            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(imageData))
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                _screenshotImage.sprite = sprite;
                _currentMapIcon = sprite;
            }
            else
            {
                Debug.LogWarning($"Failed to load image data from: {screenshotPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load screenshot from {screenshotPath}: {e.Message}");
        }
    }

    private bool IsUrl(string path)
    {
        return !string.IsNullOrEmpty(path) && (path.StartsWith("http://") || path.StartsWith("https://"));
    }

    private async Task<byte[]> DownloadImageFromUrlAsync(string url)
    {
        try
        {
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.data;
                }
                else
                {
                    Debug.LogError($"Failed to download image from URL: {url}, Error: {request.error}");
                    return null;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception while downloading image from URL {url}: {e.Message}");
            return null;
        }
    }

    private void OnLoadButtonClicked()
    {
        OnLoadMapRequested?.Invoke(_mapName);
    }

    private void OnDeleteButtonClicked()
    {
        OnDeleteMapRequested?.Invoke(_mapName);
    }

    private void OnLikeButtonClicked()
    {
        if (_isLiked)
        {
            if (_likesCount > 0)
            {
                _likesCount--;
            }

            _isLiked = false;
        }
        else
        {
            _likesCount++;
            _isLiked = true;
        }

        UpdateLikesUI();
        OnLikeValueChanged?.Invoke(_mapName, _likesCount);
    }

    private void OnPublishButtonClicked()
    {
        OnPublishRequested?.Invoke(_mapName);
    }

    public void SetInteractable(bool interactable)
    {
        if (_loadButton != null)
        {
            _loadButton.interactable = interactable;
        }

        if (_deleteButton != null)
        {
            _deleteButton.interactable = interactable;
        }
    }

    public void SetLikesEnabled(bool isEnabled)
    {
        if (_likeButton != null)
        {
            _likeButton.interactable = isEnabled;
        }

        if (!isEnabled)
        {
            _isLiked = false;
            UpdateLikeButtonVisual();
        }
    }

    public void SetLikesVisible(bool isVisible)
    {
        if (_likeButton != null)
        {
            _likeButton.gameObject.SetActive(isVisible);
        }

        if (_likesText != null)
        {
            _likesText.gameObject.SetActive(isVisible);
        }
    }

    public void SetPublishedState(bool isPublished)
    {
        _isPublished = isPublished;
        UpdatePublishUI();
    }

    public void SetPublishButtonEnabled(bool isEnabled)
    {
        if (_publishButton != null)
        {
            _publishButton.interactable = isEnabled;
            _publishButton.gameObject.SetActive(isEnabled);
        }
    }

    public void SetDeleteButtonEnabled(bool isEnabled)
    {
        if (_deleteButton != null)
        {
            _deleteButton.interactable = isEnabled;
            _deleteButton.gameObject.SetActive(isEnabled);
        }
    }

    private void UpdateLikesUI()
    {
        UpdateLikesText();
        UpdateLikeButtonVisual();
    }

    private void UpdateLikesText()
    {
        if (_likesText != null)
        {
            _likesText.text = _likesCount.ToString();
        }
    }

    private void UpdateLikeButtonVisual()
    {
        if (_likeButtonGraphic == null && _likeButton != null)
        {
            _likeButtonGraphic = _likeButton.targetGraphic;
        }

        if (_likeButtonGraphic != null)
        {
            _likeButtonGraphic.color = _isLiked ? _likedColor : _unlikedColor;
        }
    }

    private void UpdatePublishUI()
    {
        UpdatePublishButtonVisual();
    }

    private void UpdatePublishButtonVisual()
    {
        if (_publishButtonGraphic == null && _publishButton != null)
        {
            _publishButtonGraphic = _publishButton.targetGraphic;
        }

        if (_publishButtonGraphic != null)
        {
            _publishButtonGraphic.color = _isPublished ? _publishedColor : _unpublishedColor;
        }

        if (_publishButtonText != null)
        {
            _publishButtonText.text = _isPublished ? "Unpublish" : "Publish";
        }
    }
}
