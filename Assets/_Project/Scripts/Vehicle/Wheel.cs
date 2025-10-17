using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Wheel : MonoBehaviour
{
    public bool _Active;
    public float _force = 10;

    Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!_Active) return;
        //if (Player.Instance._playerMode != PlayerMode.VehicleControl) return;

        if (_rb)
        {
            _rb.AddTorque(transform.forward * (InputManager.Instance._MoveInput.y * _force * 10 * Time.deltaTime), ForceMode.Force);
        }
    }
}
