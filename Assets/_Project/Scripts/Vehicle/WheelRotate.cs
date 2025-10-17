using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class WheelRotate : MonoBehaviour
{
    [SerializeField] float _angle = 25;

    Vector3 _rotation = Vector3.zero;


    private void Update()
    {

        _rotation = transform.up * InputManager.Instance._MoveInput.x * _angle;
        transform.localEulerAngles = new Vector3(0, _rotation.y, transform.localEulerAngles.z);

    }
}
