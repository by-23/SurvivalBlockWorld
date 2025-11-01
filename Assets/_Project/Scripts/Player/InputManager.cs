using UnityEngine;

public class InputManager : MonoBehaviour
{
    private static InputManager _instance;

    public static InputManager Instance
    {
        get { return _instance; }
    }

    public bool _TOUCH;

    public bool _Run, _Press, _Fire, _Build, _Props, _Laser;
    public float _MouseWheel;
    public Vector2 _MoveInput;
    public Vector2 _ViewInput;
    public float _CameraSensity = 5;

    [SerializeField] bl_MovementJoystick _Joystick;


    private void Awake()
    {
        // Если уже есть экземпляр, уничтожаем этот
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        // Делаем InputManager постоянным между сценами
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Инициализируем bl_MobileInput
        bl_MobileInput.Initialize();

        // Проверяем наличие EventSystem (необходимо для работы UI джойстика)
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Автоматически находим джойстик, если он не назначен
        if (_Joystick == null)
        {
            _Joystick = bl_MovementJoystick.Instance;
            if (_Joystick == null)
            {
                _Joystick = FindFirstObjectByType<bl_MovementJoystick>();
            }
        }

        // Автоматически определяем режим касаний:
        // 1. Если это мобильная платформа - включаем режим касаний
        // 2. Если джойстик найден и используется - включаем режим касаний
        if (Application.isMobilePlatform)
        {
            _TOUCH = true;
        }
        else if (_Joystick != null && _Joystick.gameObject.activeInHierarchy)
        {
            // В редакторе тоже можно использовать джойстик, если он активен
            _TOUCH = true;
        }
    }

    private void Update()
    {
        if (_TOUCH)
        {
            // Получаем ввод с клавиатуры/мыши
            Vector2 keyboardMove = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            Vector2 mouseView = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            bool keyboardRun = Input.GetKey(KeyCode.LeftShift);
            bool keyboardLaser = Input.GetKey(KeyCode.E);
            bool keyboardFire = Input.GetMouseButton(0);

            // Движение: приоритет клавиатуре, иначе джойстик
            if (keyboardMove.magnitude > 0.01f)
            {
                _MoveInput = keyboardMove;
                _Run = keyboardRun;
            }
            else
            {
                // Используем сенсорное управление для движения
                if (_Joystick == null || _Joystick.sourceJoystick == null)
                {
                    _Joystick = bl_MovementJoystick.Instance;
                    if (_Joystick == null)
                    {
                        _Joystick = FindFirstObjectByType<bl_MovementJoystick>();
                    }
                }

                if (_Joystick != null && _Joystick.sourceJoystick != null)
                {
                    try
                    {
                        var source = _Joystick.sourceJoystick;
                        float horizontal = source != null ? source.Horizontal : _Joystick.Horizontal;
                        float vertical = source != null ? source.Vertical : _Joystick.Vertical;
                        _MoveInput = new Vector2(horizontal, vertical);

                        if (source != null && source.Vertical >= _Joystick.RunningOnMagnitudeOf)
                        {
                            _Run = true;
                        }
                        else
                        {
                            _Run = _Joystick.isRunning;
                        }
                    }
                    catch (System.Exception)
                    {
                        _MoveInput = Vector2.zero;
                        _Run = false;
                    }
                }
                else
                {
                    _MoveInput = Vector2.zero;
                    _Run = false;
                }
            }

            // Камера: приоритет мыши, иначе сенсорная панель
            if (mouseView.magnitude > 0.01f)
            {
                _ViewInput = mouseView;
            }
            else
            {
                _ViewInput = bl_TouchPad.GetInput(_CameraSensity);
            }

            // Действия: комбинируем клавиатуру и UI кнопки
            _Run = keyboardRun || _Run;
            _Laser = keyboardLaser; // UI кнопки работают напрямую через _laser.Press(), клавиатура через _Laser
            _Fire = keyboardFire;
        }
        else
        {
            _MoveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            _ViewInput = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            //_MouseWheel = Input.GetAxis("Mouse ScrollWheel");

            _Fire = Input.GetMouseButton(0);
            _Run = Input.GetKey(KeyCode.LeftShift);
            _Laser = Input.GetKey(KeyCode.E);

            //if (Input.GetKeyDown(KeyCode.E))
            //EnterExitCar();
        }
    }

    public void EnterExitCar()
    {
        Player.Instance.EnterCar();
    }

    public void Press(bool press)
    {
        _Press = press;
    }

    public void Props(bool props)
    {
        _Props = props;
    }

    public void RotateProps(float _value)
    {
        // Player.Instance._BuildingTool.RotateProps(_value);
    }

    /// <summary>
    /// Проверяет, существует ли InputManager, и создает его при необходимости
    /// </summary>
    public static void EnsureInputManagerExists()
    {
        if (_instance == null)
        {
            // Ищем InputManager в сцене
            InputManager existingManager = FindFirstObjectByType<InputManager>();
            if (existingManager != null)
            {
                _instance = existingManager;
                DontDestroyOnLoad(existingManager.gameObject);
            }
            else
            {
                // Создаем новый InputManager программно
                GameObject inputManagerObj = new GameObject("InputManager");
                inputManagerObj.AddComponent<InputManager>();
            }
        }
    }

    /// <summary>
    /// Проверяет, активен ли InputManager
    /// </summary>
    /// <returns>True, если InputManager активен и готов к работе</returns>
    public static bool IsInputManagerReady()
    {
        return _instance != null && _instance.gameObject != null &&
               _instance.gameObject.activeInHierarchy && _instance.enabled;
    }

    /// <summary>
    /// Принудительно активирует InputManager
    /// </summary>
    public static void ForceActivateInputManager()
    {
        EnsureInputManagerExists();

        if (_instance != null)
        {
            if (_instance.gameObject != null)
            {
                _instance.gameObject.SetActive(true);
            }

            _instance.enabled = true;
        }
    }
}

