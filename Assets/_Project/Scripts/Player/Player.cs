using Assets._Project.Scripts.UI;
using UnityEngine;

public class Player : MonoBehaviour
{
    private static Player _instance;

    public static Player Instance
    {
        get { return _instance; }
    }

    [Header("Scripts Reference")] public PlayerMovement _playerMovement;

    public PlayerCamera _playerCamera;

    [Space(5)] [Header("Reference")] [SerializeField]
    Camera _camera;

    InputManager _InputManager;
    CapsuleCollider _capsuleCollider;
    public VehicleForce _vehicleForce;
    [SerializeField] private CharacterController _characterController;

    [Space(5)] public PlayerMode _playerMode;

    private void Awake()
    {
        _instance = this;

        // Убеждаемся, что игрок в режиме управления игроком по умолчанию
        _playerMode = PlayerMode.PlayerControl;

        // Убеждаемся, что компоненты движения включены
        if (_playerMovement != null)
        {
            _playerMovement.enabled = true;
        }

        if (_playerCamera != null)
        {
            _playerCamera.enabled = true;
        }
    }

    public void EnterCar()
    {
        if (!_vehicleForce) return;

        if (_playerMode == PlayerMode.VehicleControl)
        {
            _playerMode = PlayerMode.PlayerControl;

            _playerMovement.enabled = true;
            _playerCamera.enabled = true;

            _capsuleCollider.enabled = true;
            if (_characterController != null)
                _characterController.enabled = true;

            _vehicleForce.EnterCar(false);
            _vehicleForce = null;
        }
        else if (_playerMode == PlayerMode.PlayerControl)
        {
            _playerMode = PlayerMode.VehicleControl;

            _playerMovement.enabled = false;
            _playerCamera.enabled = false;

            _capsuleCollider.enabled = false;
            if (_characterController != null)
                _characterController.enabled = false;

            _vehicleForce.EnterCar(true);
        }
    }

    /// <summary>
    /// Принудительно включает режим управления игроком
    /// </summary>
    public void ForcePlayerControlMode()
    {
        _playerMode = PlayerMode.PlayerControl;

        if (_playerMovement != null)
        {
            _playerMovement.enabled = true;

            // Убеждаемся, что CharacterController включен
            CharacterController cc = _playerMovement.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = true;
            }
        }

        if (_playerCamera != null)
        {
            _playerCamera.enabled = true;

            // Восстанавливаем character transform если он null
            if (_playerCamera.character == null)
            {
                _playerCamera.character = _playerMovement?.transform;
            }
        }

        if (_capsuleCollider != null)
        {
            _capsuleCollider.enabled = true;
        }

        if (_characterController != null)
        {
            _characterController.enabled = true;
        }
    }
}

public enum PlayerMode
{
    PlayerControl,
    VehicleControl
}