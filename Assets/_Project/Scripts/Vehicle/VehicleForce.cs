using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleForce : MonoBehaviour
{
    public bool _Active;

    [SerializeField] float _force = 20;
    [SerializeField] float _tourqueForce = 20;
    [SerializeField] Vector3 _centerOfMass;
    [SerializeField] Rigidbody _rb;
    [SerializeField] VehicleCamera _vehicleCamera;
    [SerializeField] WheelJoint[] _wheelJoint;
    [SerializeField] BoxCollider _boxCollider;

    float _dirTurn;

    void Start()
    {

        _rb.centerOfMass = _centerOfMass;

        _wheelJoint = GetComponentsInChildren<WheelJoint>();

    }

    public void EnterCar(bool _enter)
    {
        _Active = _enter;

        foreach (WheelJoint joint in _wheelJoint)
        {
            joint._wheel._Active = _Active;
        }

        _boxCollider.enabled = !_Active;

        if (_Active)
        {
            Player.Instance.transform.parent = _rb.transform;
            Player.Instance.transform.localPosition = Vector3.zero;
            Player.Instance.transform.localRotation = Quaternion.EulerAngles(0, 0, 0);
        }
        else
        {
            Player.Instance.transform.parent = null;
            Player.Instance.transform.position = new Vector3(_rb.transform.position.x, _rb.transform.position.y, _rb.transform.position.z - 2f);
            Player.Instance.transform.rotation = Quaternion.LookRotation(_rb.transform.position, Vector3.up);
        }

        _vehicleCamera.gameObject.SetActive(_Active);
    }

    private void Update()
    {
        if (!_Active) return;
        //if (Player.Instance._playerMode != PlayerMode.VehicleControl) return;

        if (_rb)
        {
            _rb.AddForce(transform.forward * (InputManager.Instance._MoveInput.y * _force * 10 * Time.deltaTime), ForceMode.Force);

            if (InputManager.Instance._MoveInput.y > 0) _dirTurn = InputManager.Instance._MoveInput.x;
            if (InputManager.Instance._MoveInput.y < 0) _dirTurn = -InputManager.Instance._MoveInput.x;

            _rb.AddTorque(new Vector3(0, _dirTurn * _tourqueForce * 10 * Time.deltaTime, 0), ForceMode.Force);


        }
    }

    public void Break()
    {
        _rb.velocity = Vector3.LerpUnclamped(_rb.velocity, Vector3.zero, Time.deltaTime * 5);
    }

    public void CheckJoint()
    {
        foreach (WheelJoint joint in _wheelJoint)
        {
            joint.CheckDisconnect();
        }

        Invoke(nameof(DubleChack), 0.5f);
    }

    private void DubleChack()
    {
        int _deactivateCount = 0;

        foreach (WheelJoint joint in _wheelJoint)
        {
            if (joint._Deactivate)
                _deactivateCount++;
        }

        if (_deactivateCount >= 2)
        {
            foreach (WheelJoint joint in _wheelJoint)
            {
                joint.DeleteWheel();
            }


            _rb.transform.parent = null;

            Destroy(_boxCollider);
            Destroy(gameObject, 1);
        }
    }
}
