using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float _moveSpeed = 5;
    [SerializeField] float _runSpeed = 9;
    [SerializeField] float _gravity = 9.81f;
    [SerializeField] float _jumpHeight = 3;
    [SerializeField] float _slopeLimit = 45f; // Максимальный угол наклона для ходьбы
    [SerializeField] float _stepOffset = 0.3f; // Высота ступеньки, которую может преодолеть

    [Header("Ground Check")] [SerializeField]
    bool _Grounded;

    [Header("Laser")] [SerializeField] Laser _laser;

    public float _GroundedOffset = -0.14f;
    public float _GroundedRadius = 0.28f;
    public LayerMask _GroundLayers;

    private CharacterController _characterController;
    private Vector3 _velocity;
    private float _speed;

    void Awake()
    {
        // Получаем CharacterController или создаем его
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            _characterController = gameObject.AddComponent<CharacterController>();
        }

        // Настраиваем CharacterController для работы с наклонами
        _characterController.height = 2f;
        _characterController.radius = 0.5f;
        _characterController.center = new Vector3(0, 1, 0);
        _characterController.slopeLimit = _slopeLimit;
        _characterController.stepOffset = _stepOffset;
        _characterController.skinWidth = 0.08f; // Толщина кожи для лучшего контакта

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Убеждаемся, что InputManager существует
        InputManager.EnsureInputManagerExists();
    }

    private void Update()
    {
        GroundedCheck();
        HandleMovement();
        HandleGravity();
        HandleJump();
        HandleLaser();

        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private void HandleMovement()
    {
        // Проверяем, что InputManager доступен
        if (!InputManager.IsInputManagerReady())
        {
            InputManager.ForceActivateInputManager();
            if (!InputManager.IsInputManagerReady())
                return;
        }

        // Проверяем режим игрока
        if (Player.Instance != null && Player.Instance._playerMode != PlayerMode.PlayerControl)
            return;

        // Определяем скорость
        if (InputManager.Instance._Run)
            _speed = _runSpeed;
        else
            _speed = _moveSpeed;

        // Получаем ввод
        Vector2 moveInput = InputManager.Instance._MoveInput;

        // Создаем вектор движения относительно камеры
        Vector3 moveDirection = Vector3.zero;

        // Уменьшаем порог для более чувствительного отклика на джойстик
        float deadZone = InputManager.Instance._TOUCH ? 0.05f : 0.1f;

        if (moveInput.magnitude > deadZone && Camera.main != null)
        {
            // Получаем направление камеры (без наклона по Y)
            Vector3 forward = Camera.main.transform.forward;
            Vector3 right = Camera.main.transform.right;

            // Убираем компонент Y из направлений
            forward.y = 0;
            right.y = 0;

            // Нормализуем
            forward.Normalize();
            right.Normalize();

            // Создаем направление движения
            moveDirection = forward * moveInput.y + right * moveInput.x;
            moveDirection.Normalize();
        }

        // Применяем движение
        Vector3 movement = moveDirection * (_speed * Time.deltaTime);

        // Добавляем небольшую силу вниз для лучшего контакта с наклонными поверхностями
        if (_Grounded && movement.magnitude > 0.1f)
        {
            movement.y -= 0.1f; // Небольшая сила вниз для прижатия к поверхности
        }

        _characterController.Move(movement);
    }

    private void HandleGravity()
    {
        if (_Grounded)
        {
            // Если на земле, сбрасываем вертикальную скорость
            if (_velocity.y < 0)
            {
                _velocity.y = -2f; // Небольшая скорость вниз для лучшего контакта с землей
            }
        }
        else
        {
            // Если в воздухе, применяем гравитацию
            _velocity.y -= _gravity * Time.deltaTime;
        }

        // Применяем вертикальное движение
        _characterController.Move(_velocity * Time.deltaTime);
    }

    private void HandleJump()
    {
        // Проверяем ввод прыжка (пробел или клик на экране)
        // Работает и от клавиатуры, и от сенсорных кнопок
        bool jumpInput = Input.GetKeyDown(KeyCode.Space) ||
                         (InputManager.Instance._TOUCH && InputManager.Instance._Press);

        if (jumpInput && _Grounded)
        {
            _velocity.y = Mathf.Sqrt(_jumpHeight * -2f * -_gravity);
        }
    }

    private void GroundedCheck()
    {
        // Используем встроенную проверку CharacterController
        _Grounded = _characterController.isGrounded;

        // Дополнительная проверка с помощью Physics.CheckSphere для более точного определения
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - _GroundedOffset,
            transform.position.z);
        bool physicsGrounded =
            Physics.CheckSphere(spherePosition, _GroundedRadius, _GroundLayers, QueryTriggerInteraction.Ignore);

        // Используем более точную проверку
        _Grounded = physicsGrounded || _characterController.isGrounded;

        // Дополнительная проверка с помощью Raycast для определения угла поверхности
        if (!_Grounded)
        {
            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 1.5f, _GroundLayers))
            {
                float angle = Vector3.Angle(hit.normal, Vector3.up);
                // Если угол поверхности меньше лимита наклона, считаем что мы на земле
                if (angle < _slopeLimit)
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
        // Проверяем, что InputManager доступен
        if (!InputManager.IsInputManagerReady())
        {
            InputManager.ForceActivateInputManager();
            if (!InputManager.IsInputManagerReady())
                return;
        }

        // Проверяем режим игрока
        if (Player.Instance != null && Player.Instance._playerMode != PlayerMode.PlayerControl)
            return;

        // Проверяем, что лазер доступен
        if (_laser == null)
        {
            // Пытаемся найти лазер автоматически
            _laser = FindFirstObjectByType<Laser>();
            if (_laser == null)
            {
                return;
            }
        }

        // Убеждаемся, что лазер активен
        if (!_laser.gameObject.activeInHierarchy)
        {
            _laser.gameObject.SetActive(true);
        }

        // Обновляем лазер только если изменилось состояние клавиши E
        // Это позволяет UI кнопкам работать независимо
        bool currentLaserState = InputManager.Instance._Laser;
        if (currentLaserState != _lastLaserState)
        {
            _laser.Press(currentLaserState);
            _lastLaserState = currentLaserState;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Визуализация проверки земли
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            new Vector3(transform.position.x, transform.position.y - _GroundedOffset, transform.position.z),
            _GroundedRadius);

        // Визуализация Raycast для проверки наклонов
        Gizmos.color = Color.red;
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;
        Gizmos.DrawRay(rayStart, Vector3.down * 1.5f);

        // Показываем лимит наклона
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.1f);
    }
}