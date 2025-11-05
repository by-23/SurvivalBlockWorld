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
        if (_Joystick == null)
            _Joystick = FindAnyObjectByType<bl_MovementJoystick>();
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Инициализируем bl_MobileInput
        bl_MobileInput.Initialize();
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
            _Build = Input.GetKeyDown(KeyCode.E);

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
    
}

