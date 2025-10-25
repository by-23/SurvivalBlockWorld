using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Scripts.UI
{
    public class UIManager : BaseSaveUIController
    {
        public static UIManager Instance { get; private set; }

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

        [Header("Load UI")] [SerializeField] private GameObject _loadPanel;
        [SerializeField] private Button _closeLoadPanelButton;
        [SerializeField] private MapListUIController _mapListUIController;


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

        protected override void Start()
        {
            base.Start();
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

            if (_loadPanel != null)
            {
                _loadPanel.SetActive(false);
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

        public void OnMapsButtonPressed()
        {
            _mapListUIController.OnMapsListOpened();
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

        protected override void OnMapLoadRequested(string mapName)
        {
            // Close the load panel after starting the load
            if (_loadPanel != null)
            {
                _loadPanel.SetActive(false);
            }

            // Load only world (no scene change)
            LoadWorldOnly(mapName);
        }

        private async void LoadWorldOnly(string mapName)
        {
            // Загружаем только мир без смены сцены (мы уже в игровой сцене)
            bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, false);

            if (loadSuccess)
            {
                // Убеждаемся, что InputManager и Player готовы после загрузки мира
                InputManager.ForceActivateInputManager();
                if (Player.Instance != null)
                {
                    Player.Instance.ForcePlayerControlMode();
                }
            }
            else
            {
                // Return to load panel on error
                if (_loadPanel != null)
                {
                    _loadPanel.SetActive(true);
                }
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
}
