using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField] public Transform character;
    public float sensitivity = 2;
    public float smoothing = 1.5f;

    Vector2 velocity;
    Vector2 frameVelocity;


    void Reset()
    {
        character = GetComponentInParent<PlayerMovement>().transform;
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
