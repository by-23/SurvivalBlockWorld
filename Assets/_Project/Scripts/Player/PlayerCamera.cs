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

        Vector2 rawFrameVelocity = Vector2.Scale(mouseDelta, Vector2.one * sensitivity);
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1 / smoothing);
        velocity += frameVelocity;
        velocity.y = Mathf.Clamp(velocity.y, -90, 90);

        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);
        character.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }
}
