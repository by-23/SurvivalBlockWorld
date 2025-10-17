using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WheelJoint : MonoBehaviour
{
    public bool _Deactivate;

    [SerializeField] GameObject _cube;
    public Wheel _wheel;
    FixedJoint _fixedJoint;
    VehicleForce _vehicleForce;

    void Start()
    {
        _fixedJoint = GetComponent<FixedJoint>();
    }

    public void DeleteWheel()
    {
        if (!_wheel) return;

        _Deactivate = true;

        _fixedJoint.connectedBody = null;

        _wheel.gameObject.transform.parent = null;

        if (_wheel.GetComponent<HingeJoint>())
            Destroy(_wheel.GetComponent<HingeJoint>());

        Destroy(_wheel);
        Destroy(gameObject, 1);
    }

    public void CheckDisconnect()
    {
        if(_cube)
            _vehicleForce = _cube.GetComponentInParent<VehicleForce>();

        if (!_vehicleForce)
        {
            _Deactivate = true;

            _fixedJoint.connectedBody = null;

            if(_wheel)
            {
                _wheel.gameObject.transform.parent = null;

                if (_wheel.GetComponent<HingeJoint>())
                    Destroy(_wheel.GetComponent<HingeJoint>());

                Destroy(_wheel);

            }

        }

    }
}
