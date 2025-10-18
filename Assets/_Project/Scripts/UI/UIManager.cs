using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Gameplay UI")] [SerializeField]
    private Laser _laser;

    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RopeGenerator _ropeGenerator;
    [SerializeField] private VehicleForce _vehicleForce;

    [Header("Buttons")] [SerializeField] private Button _saveButton;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _mapsButton;

    [Header("Save UI")] [SerializeField] private GameObject _savePanel;
    [SerializeField] private TMP_InputField _saveNameInput;
    [SerializeField] private Button _confirmSaveButton;
    [SerializeField] private Button _cancelSaveButton;

    [Header("Load UI")] [SerializeField] private GameObject _loadPanel;
    [SerializeField] private SaveListManager _saveListManager;
    [SerializeField] private Button _closeLoadPanelButton;


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

        if (_loadButton != null)
        {
            _loadButton.onClick.AddListener(OnLoadButtonPressed);
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

    private void OnMapLoadRequested(string mapName)
    {
        Debug.Log($"Loading map: {mapName}");
        SaveSystem.Instance.LoadWorld(mapName);

        // Close the load panel after starting the load
        if (_loadPanel != null)
        {
            _loadPanel.SetActive(false);
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
        if (_vehicleForce != null)
        {
            // You'll need to decide how to manage the 'enter' state
            // For now, I'll assume it's just to enter.
            _vehicleForce.EnterCar(true);
        }
    }

    // --- Placeholder methods for buttons without found scripts ---

    public void OnCopyButtonPressed()
    {
        Debug.Log("Copy button pressed - functionality not implemented.");
    }

    public void OnPasteButtonPressed()
    {
        Debug.Log("Paste button pressed - functionality not implemented.");
    }

    public void OnSpawnButtonPressed()
    {
        Debug.Log("Spawn button pressed - functionality not implemented.");
    }

    public void OnBombButtonPressed()
    {
        Debug.Log("Bomb button pressed - functionality not implemented.");
    }

    public void OnRemoveButtonPressed()
    {
        Debug.Log("Remove button pressed - functionality not implemented.");
    }

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (_saveListManager != null)
        {
            _saveListManager.OnMapLoadRequested -= OnMapLoadRequested;
        }
    }
}
