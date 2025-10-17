using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleCamera : MonoBehaviour
{
    [SerializeField] bool _RotateActive;
    [SerializeField] float _cameraSensity = 5;
    [SerializeField] float _minorSensitiv = 10;
    [SerializeField] float _moveSmooth = 10;
    [SerializeField] float zoomRatio = 0.5f;
    [SerializeField] float defaultFOV = 60f;
    [SerializeField] float TopClamp = 70.0f;
    [SerializeField] float BottomClamp = -30.0f;
    [SerializeField] Rigidbody _rigidbodyCar;

    private const float _threshold = 0.01f;
    private float _TargetYaw;
    private float _TargetPitch;
    private Vector2 _CameraImput;
    private Camera _camera;
    private float _startRate, _currentRate;
    private bool _startAutoRotate;

    private void Awake()
    {
        _camera = GetComponentInChildren<Camera>();
        _currentRate = 2;
    }

    void FixedUpdate()
    {
        CameraRotation();
       
        transform.position = Vector3.MoveTowards(transform.position, _rigidbodyCar.transform.position, _moveSmooth * Time.deltaTime);
    }

    private void CameraRotation()
    {
        _CameraImput = InputManager.Instance._ViewInput;


        // if there is an input and camera position is not fixed
        if (InputManager.Instance._ViewInput.sqrMagnitude >= _threshold)
        {
            _TargetYaw += _CameraImput.x * (_cameraSensity / _minorSensitiv);
            _TargetPitch += -_CameraImput.y * (_cameraSensity / _minorSensitiv);
        }

        // clamp our rotations so our values are limited 360 degrees
        _TargetYaw = ClampAngle(_TargetYaw, float.MinValue, float.MaxValue);
        _TargetPitch = ClampAngle(_TargetPitch, BottomClamp, TopClamp);



        if (_RotateActive)
        {
            if (InputManager.Instance._ViewInput.magnitude < 0.2f)
            {
                if (_startRate < _currentRate)
                    _startRate += Time.deltaTime;

                if (_startRate >= _currentRate)
                    _startAutoRotate = true;
            }
            else
            {
                _startRate = 0;
                _startAutoRotate = false;
            }

            if (_startAutoRotate)
            {
                transform.rotation = Quaternion.SlerpUnclamped(transform.rotation, _rigidbodyCar.transform.rotation, 5 * Time.deltaTime);
                _TargetYaw = _rigidbodyCar.transform.eulerAngles.y;
                _TargetPitch = _rigidbodyCar.transform.eulerAngles.x;
            }
            else
            {
                transform.rotation = Quaternion.SlerpUnclamped(transform.rotation, Quaternion.Euler(_TargetPitch, _TargetYaw, 0.0f), 15 * Time.deltaTime);
            }
        }
        else
        {
            transform.rotation = Quaternion.Euler(_TargetPitch, _TargetYaw, 0.0f);
        }

        float acc = _rigidbodyCar.velocity.magnitude;
        _camera.fieldOfView = defaultFOV + acc * zoomRatio * Time.deltaTime;
    }

    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }
}
