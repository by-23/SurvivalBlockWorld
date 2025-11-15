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
            _likeButtonGraphic = _likeButton.targetGraphic;
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
