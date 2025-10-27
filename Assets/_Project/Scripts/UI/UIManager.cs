using Assets._Project.Scripts.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Управляет основным игровым UI: кнопками действия игрока и панелью сохранения
/// </summary>
public class UIManager : MonoBehaviour
{
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

    [Header("Load UI")] [SerializeField] private Button _closeLoadPanelButton;
    [SerializeField] private MapListUI mapListUI;

    private void Start()
    {
        SetupButtons();
        SetupPanels();
    }

    private void SetupButtons()
    {
        if (_saveButton != null)
        {
            _saveButton.onClick.AddListener(OnSaveButtonPressed);
        }

        if (_mapsButton != null)
        {
            _mapsButton.onClick.AddListener(OnMapsButtonPressed);
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
    }

    private void SetupPanels()
    {
        if (_savePanel != null)
        {
            _savePanel.SetActive(false);
        }
    }

    // Button functions
    private void OnSaveButtonPressed()
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

    private void OnMapsButtonPressed()
    {
        if (mapListUI != null)
        {
            mapListUI.OpenMapList();
        }
        else
        {
            Debug.LogError("UIManager: _mapListUIController is NULL!");
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
        if (mapListUI != null)
        {
            mapListUI.CloseMapList();
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
}


