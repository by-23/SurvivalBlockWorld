using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField] private Laser _laser;
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private RopeGenerator _ropeGenerator;
    [SerializeField] private VehicleForce _vehicleForce;
    [SerializeField] private Button _saveButton;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _mapsButton;


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
    }

    // Button functions
    public void OnSaveButtonPressed()
    {
        SaveSystem.Instance.SaveWorld();
    }

    public void OnLoadButtonPressed()
    {
        SaveSystem.Instance.LoadWorld();
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
}
