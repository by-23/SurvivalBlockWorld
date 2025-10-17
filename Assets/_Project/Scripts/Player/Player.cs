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
    UIManager _UIManager;
    CapsuleCollider _capsuleCollider;
    public VehicleForce _vehicleForce;
    [SerializeField] private Rigidbody _rb;
    
    [Space(5)]
    public PlayerMode _playerMode;

    private void Awake()
    {
        _instance = this;
        
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
            _rb.isKinematic = false;

            _vehicleForce.EnterCar(false);
            _vehicleForce = null;
        }
        else if (_playerMode == PlayerMode.PlayerControl)
        {
            _playerMode = PlayerMode.VehicleControl;

            _playerMovement.enabled = false;
            _playerCamera.enabled = false;

            _capsuleCollider.enabled = false;
            _rb.isKinematic = true;

            _vehicleForce.EnterCar(true);
        }

    }
}
public enum PlayerMode
{
    PlayerControl,
    VehicleControl
}