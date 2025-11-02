using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField] public Transform character;
    public float sensitivity = 2;
    public float smoothing = 1.5f;

    [Header("Performance Optimization")]
    [Tooltip("Оптимизирует Volume System для снижения нагрузки на CPU")]
    [SerializeField]
    private bool _optimizeVolumeSystem = true;

    Vector2 velocity;
    Vector2 frameVelocity;
    private UniversalAdditionalCameraData _cameraData;
    private bool _volumeSystemOptimized = false;
    private int _lastCameraTouchId = -1;
    private bool _ignoreFirstTouchFrame = false;

    void Awake()
    {
        _cameraData = GetComponent<UniversalAdditionalCameraData>();
    }

    void Start()
    {
        OptimizeVolumeSystem();
    }

    void Reset()
    {
        character = GetComponentInParent<PlayerMovement>().transform;
    }

    /// <summary>
    /// Оптимизирует Volume System для снижения нагрузки на CPU во время бездействия.
    /// Отключает постобработку или изменяет режим обновления Volume через рефлексию,
    /// что предотвращает избыточные вызовы VolumeStack.GetComponent() каждый кадр.
    /// </summary>
    private void OptimizeVolumeSystem()
    {
        if (!_optimizeVolumeSystem || _cameraData == null || _volumeSystemOptimized)
            return;

        // Вариант 1: Отключаем постобработку полностью (наиболее эффективно)
        // Это полностью устраняет вызовы VolumeStack.GetComponent()
        _cameraData.renderPostProcessing = false;

        // Вариант 2: Изменяем режим обновления Volume через рефлексию (если постобработка нужна)
        // Используем рефлексию для доступа к внутреннему свойству volumeFrameworkUpdateMode
        try
        {
            var volumeUpdateModeField = typeof(UniversalAdditionalCameraData)
                .GetProperty("volumeFrameworkUpdateMode",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (volumeUpdateModeField != null)
            {
                // VolumeFrameworkUpdateMode.UsePipelineSettings = 1 (вместо Every Frame = 2)
                volumeUpdateModeField.SetValue(_cameraData, 1);
#if UNITY_EDITOR
                Debug.Log($"[PlayerCamera] Volume System оптимизирован: режим обновления изменен через рефлексию");
#endif
            }
        }
        catch (System.Exception ex)
        {
#if UNITY_EDITOR
            Debug.LogWarning(
                $"[PlayerCamera] Не удалось изменить режим обновления Volume через рефлексию: {ex.Message}");
#endif
        }

        _volumeSystemOptimized = true;

#if UNITY_EDITOR
        Debug.Log($"[PlayerCamera] Постобработка отключена для оптимизации производительности");
#endif
    }

    void LateUpdate()
    {
        // Проверяем, что InputManager доступен
        if (!InputManager.IsInputManagerReady())
        {
            InputManager.ForceActivateInputManager();

            // Проверяем еще раз после попытки исправления
            if (!InputManager.IsInputManagerReady())
            {
                return;
            }
        }

        // Проверяем, что PlayerCamera включен
        if (!enabled)
        {
            return;
        }

        // Проверяем режим игрока
        if (Player.Instance != null && Player.Instance._playerMode != PlayerMode.PlayerControl)
        {
            return;
        }

        // Проверяем character transform
        if (character == null)
        {
            return;
        }

        Vector2 mouseDelta = InputManager.Instance._ViewInput;

        // Проверяем, находится ли касание камеры в области джойстика - если да, блокируем движение камеры
        if (IsTouchInJoystickArea())
        {
            // Сбрасываем отслеживание при блокировке
            _lastCameraTouchId = -1;
            _ignoreFirstTouchFrame = false;
            return;
        }

        // Обрабатываем начало и конец касания для предотвращения резкого скачка
        if (Application.isMobilePlatform && InputManager.Instance._TOUCH)
        {
            int currentTouchId = bl_MobileInput.GetUsableTouch();

            // Проверяем, не закончилось ли предыдущее касание
            if (_lastCameraTouchId != -1)
            {
                bool touchStillExists = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.touches[i].fingerId == _lastCameraTouchId)
                    {
                        touchStillExists = true;

                        // Если касание завершается, игнорируем последний кадр
                        if (Input.touches[i].phase == TouchPhase.Ended ||
                            Input.touches[i].phase == TouchPhase.Canceled)
                        {
                            _lastCameraTouchId = -1;
                            _ignoreFirstTouchFrame = false;
                            return;
                        }

                        break;
                    }
                }

                // Если касание исчезло (не найдено в массиве), игнорируем последний кадр
                if (!touchStillExists && currentTouchId == -1)
                {
                    _lastCameraTouchId = -1;
                    _ignoreFirstTouchFrame = false;
                    return;
                }
            }

            // Сбрасываем отслеживание, если касание закончилось
            if (currentTouchId == -1)
            {
                // Если было активное касание, игнорируем последний кадр перед сбросом
                if (_lastCameraTouchId != -1)
                {
                    _lastCameraTouchId = -1;
                    _ignoreFirstTouchFrame = false;
                    return;
                }

                _lastCameraTouchId = -1;
                _ignoreFirstTouchFrame = false;
            }
            else if (currentTouchId != _lastCameraTouchId)
            {
                // Начало нового касания
                _lastCameraTouchId = currentTouchId;

                // Находим касание для проверки фазы
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.touches[i].fingerId == currentTouchId)
                    {
                        // Игнорируем Began и Stationary - ждем первого Moved
                        if (Input.touches[i].phase == TouchPhase.Began ||
                            Input.touches[i].phase == TouchPhase.Stationary)
                        {
                            return;
                        }

                        // Для первого Moved помечаем, что нужно проверить на большой скачок
                        if (Input.touches[i].phase == TouchPhase.Moved)
                        {
                            _ignoreFirstTouchFrame = true;
                        }

                        break;
                    }
                }
            }

            // Если это первый кадр движения нового касания, проверяем на большой скачок
            if (_ignoreFirstTouchFrame)
            {
                float deltaMagnitude = mouseDelta.magnitude;
                // Если скачок слишком большой, считаем это артефактом первого касания и игнорируем
                if (deltaMagnitude > 50f)
                {
                    _ignoreFirstTouchFrame = false;
                    return;
                }

                _ignoreFirstTouchFrame = false;
            }
        }

        Vector2 rawFrameVelocity = Vector2.Scale(mouseDelta, Vector2.one * sensitivity);
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1 / smoothing);
        velocity += frameVelocity;
        velocity.y = Mathf.Clamp(velocity.y, -90, 90);

        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);
        character.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }

    /// <summary>
    /// Проверяет, находится ли касание камеры в области джойстика для передвижения
    /// Блокирует камеру только если ВСЕ касания находятся в области джойстика
    /// </summary>
    private bool IsTouchInJoystickArea()
    {
        if (InputManager.Instance == null || !InputManager.Instance._TOUCH)
            return false;

        bl_MovementJoystick movementJoystick = bl_MovementJoystick.Instance;
        if (movementJoystick == null || movementJoystick.sourceJoystick == null)
            return false;

        // Проверяем, есть ли активные касания (на мобильных) или мышь (на ПК)
        if (Application.isMobilePlatform)
        {
            if (Input.touchCount == 0)
                return false;

            // Получаем касание джойстика через рефлексию
            var sourceJoystick = movementJoystick.sourceJoystick;
            int joystickTouchId = -2;

            try
            {
                var lastIdField = sourceJoystick.GetType()
                    .GetField("lastId",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (lastIdField != null)
                {
                    joystickTouchId = (int)lastIdField.GetValue(sourceJoystick);
                }
            }
            catch (System.Exception)
            {
                // Если рефлексия не сработала, продолжаем без проверки по ID
            }

            // Проверяем все касания: если есть хотя бы одно касание ВНЕ области джойстика и не игнорируется - разрешаем камеру
            bool hasTouchOutsideJoystick = false;

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.touches[i];

                // Пропускаем касание джойстика (по ID)
                if (joystickTouchId != -2 && touch.fingerId == joystickTouchId)
                {
                    continue;
                }

                // Пропускаем игнорируемые касания (кнопки UI и т.д.)
                if (bl_MobileInput.ignoredTouches.Contains(touch.fingerId))
                {
                    continue;
                }

                // Если касание НЕ в области джойстика - есть касание для камеры
                if (!IsPositionInJoystickArea(touch.position))
                {
                    hasTouchOutsideJoystick = true;
                    break;
                }
            }

            // Блокируем камеру только если НЕТ касаний вне области джойстика
            return !hasTouchOutsideJoystick;
        }
        else
        {
            // На ПК проверяем позицию мыши
            return IsPositionInJoystickArea(Input.mousePosition);
        }
    }

    /// <summary>
    /// Проверяет, находится ли экранная позиция в области джойстика
    /// </summary>
    private bool IsPositionInJoystickArea(Vector2 screenPosition)
    {
        bl_MovementJoystick movementJoystick = bl_MovementJoystick.Instance;
        if (movementJoystick == null || movementJoystick.sourceJoystick == null)
            return false;

        var sourceJoystick = movementJoystick.sourceJoystick;
        RectTransform joystickRect = null;

        // Получаем RectTransform джойстика в зависимости от типа
        if (sourceJoystick is bl_Joystick joystick)
        {
            // Для bl_Joystick используем RectTransform компонента Image
            var backImage = joystick.GetComponent<UnityEngine.UI.Image>();
            if (backImage != null)
            {
                joystickRect = backImage.rectTransform;
            }
        }
        else if (sourceJoystick is bl_JoystickArea joystickArea)
        {
            // Для bl_JoystickArea используем areaTransform или joystickRoot
            joystickRect = joystickArea.areaTransform != null ? joystickArea.areaTransform : joystickArea.joystickRoot;
        }

        if (joystickRect == null)
            return false;

        // Проверяем, находится ли позиция в пределах RectTransform джойстика
        Canvas canvas = joystickRect.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;

        Vector2 localPoint;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickRect, screenPosition, uiCamera,
                   out localPoint) &&
               joystickRect.rect.Contains(localPoint);
    }
}

