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


    private void Update()
    {
        if (_TOUCH)
        {
            _MoveInput = new Vector2(_Joystick.Horizontal, _Joystick.Vertical);
            _ViewInput = bl_TouchPad.GetInput(_CameraSensity);
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

