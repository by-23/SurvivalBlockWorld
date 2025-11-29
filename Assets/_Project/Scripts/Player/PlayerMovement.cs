using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float _moveSpeed = 5;
    [SerializeField] float _runSpeed = 9;
    [SerializeField] float _gravity = 9.81f;
    [SerializeField] float _jumpHeight = 3;
    [SerializeField] float _slopeLimit = 45f;
    [SerializeField] float _stepOffset = 0.3f;

    [Header("Ground Check")] [SerializeField]
    bool _Grounded;

    [Header("Laser")] [SerializeField] Laser _laser;

    public float _GroundedOffset = -0.14f;
    public float _GroundedRadius = 0.28f;
    public LayerMask _GroundLayers;

    private CharacterController _characterController;
    private Vector3 _velocity;
    private float _speed;

    private bool _levitateMode;
    private bool _levitateUp;
    private bool _levitateDown;
    [SerializeField] float _levitateSpeed = 4f;

    void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            _characterController = gameObject.AddComponent<CharacterController>();
        }

        _characterController.height = 2f;
        _characterController.radius = 0.5f;
        _characterController.center = new Vector3(0, 1, 0);
        _characterController.slopeLimit = _slopeLimit;
        _characterController.stepOffset = _stepOffset;
        _characterController.skinWidth = 0.08f;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterPlayerMovement(this);
        }
    }

    private void Update()
    {
        GroundedCheck();
        HandleMovement();
        if (_levitateMode)
        {
            HandleLevitation();
        }
        else
        {
            HandleGravity();
            HandleJump();
        }

        HandleLaser();
    }

    private void HandleMovement()
    {
        if (Player.Instance != null && Player.Instance._playerMode != PlayerMode.PlayerControl)
            return;

        if (InputManager.Instance._Run)
            _speed = _runSpeed;
        else
            _speed = _moveSpeed;

        Vector2 moveInput = InputManager.Instance._MoveInput;
        Vector3 moveDirection = Vector3.zero;
        float deadZone = InputManager.Instance._TOUCH ? 0.05f : 0.1f;

        if (moveInput.magnitude > deadZone && Camera.main != null)
        {
            Vector3 forward = Camera.main.transform.forward;
            Vector3 right = Camera.main.transform.right;

            forward.y = 0;
            right.y = 0;

            forward.Normalize();
            right.Normalize();

            moveDirection = forward * moveInput.y + right * moveInput.x;
            moveDirection.Normalize();
        }

        Vector3 movement = moveDirection * (_speed * Time.deltaTime);

        if (!_levitateMode && _Grounded && movement.magnitude > 0.1f)
        {
            movement.y -= 0.1f;
        }

        _characterController.Move(movement);
    }

    private void HandleGravity()
    {
        if (_Grounded)
        {
            if (_velocity.y < 0)
            {
                _velocity.y = -2f;
            }
        }
        else
        {
            _velocity.y -= _gravity * Time.deltaTime;
        }

        _characterController.Move(_velocity * Time.deltaTime);
    }

    private void HandleLevitation()
    {
        float y = 0f;
        if (_levitateUp) y += 1f;
        if (_levitateDown) y -= 1f;

        _velocity.y = 0f;
        Vector3 vertical = new Vector3(0f, y * _levitateSpeed, 0f) * Time.deltaTime;

        _characterController.Move(vertical);
    }

    public void SetLevitateMode(bool active)
    {
        _levitateMode = active;
        _levitateUp = false;
        _levitateDown = false;
        if (active)
        {
            _velocity.y = 0f;
        }
    }

    public void SetLevitateUp(bool isPressed)
    {
        _levitateUp = isPressed;
    }

    public void SetLevitateDown(bool isPressed)
    {
        _levitateDown = isPressed;
    }

    private void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }
    }

    private void GroundedCheck()
    {
        _Grounded = _characterController.isGrounded;

        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - _GroundedOffset,
            transform.position.z);
        bool physicsGrounded =
            Physics.CheckSphere(spherePosition, _GroundedRadius, _GroundLayers, QueryTriggerInteraction.Ignore);

        _Grounded = physicsGrounded || _characterController.isGrounded;

        if (!_Grounded)
        {
            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 0.1f;
            float raycastDistance = 0.5f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, _GroundLayers))
            {
                float angle = Vector3.Angle(hit.normal, Vector3.up);
                if (angle < _slopeLimit && _velocity.y > -5f)
                {
                    _Grounded = true;
                }
            }
        }
    }

    public void Jump()
    {
        if (_Grounded)
        {
            _velocity.y = Mathf.Sqrt(_jumpHeight * -2f * -_gravity);
        }
    }

    private bool _lastLaserState;

    private void HandleLaser()
    {
        if (Player.Instance != null && Player.Instance._playerMode != PlayerMode.PlayerControl)
            return;

        if (_laser == null)
        {
            _laser = FindFirstObjectByType<Laser>();
            if (_laser == null)
            {
                return;
            }
        }

        if (!_laser.gameObject.activeInHierarchy)
        {
            _laser.gameObject.SetActive(true);
        }

        bool currentLaserState = InputManager.Instance._Laser;
        if (currentLaserState != _lastLaserState)
        {
            _laser.Press(currentLaserState);
            _lastLaserState = currentLaserState;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            new Vector3(transform.position.x, transform.position.y - _GroundedOffset, transform.position.z),
            _GroundedRadius);

        Gizmos.color = Color.red;
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;
        Gizmos.DrawRay(rayStart, Vector3.down * 0.5f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.1f);
    }
}