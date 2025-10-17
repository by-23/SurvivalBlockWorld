using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float _moveSpeed = 5;
    [SerializeField] float _runSpeed = 9;

    [Header("Jump")][SerializeField] float _jumpHeight = 3;
    [SerializeField] bool _Grounded;
    public float _GroundedOffset = -0.14f;
    public float _GroundedRadius = 0.28f;
    public LayerMask _GroundLayers;

    Rigidbody _rigidbody;
    float _speed;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        GroundedCheck();

        if (Input.GetKeyDown(KeyCode.RightBracket))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void FixedUpdate()
    {
        Move();
    }

    private void Move()
    {
        if (InputManager.Instance._Run) _speed = _runSpeed;
        else _speed = _moveSpeed;

        Vector2 targetVelocity = new Vector2(InputManager.Instance._MoveInput.x * _speed,
            InputManager.Instance._MoveInput.y * _speed);

        var v3 = transform.rotation * new Vector3(targetVelocity.x, _rigidbody.velocity.y, targetVelocity.y);
        v3.y = _rigidbody.velocity.y;

        if (!_Grounded) v3.y -= 0.3f;

        _rigidbody.velocity = v3;
    }

    public void Jump()
    {
        if (!_Grounded) return;

        _rigidbody.AddForce(Vector3.up * _jumpHeight * 5, ForceMode.Impulse);
    }


    private void GroundedCheck()
    {
        // set sphere position, with offset
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - _GroundedOffset,
            transform.position.z);
        _Grounded = Physics.CheckSphere(spherePosition, _GroundedRadius, _GroundLayers,
            QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            new Vector3(transform.position.x, transform.position.y - _GroundedOffset, transform.position.z),
            _GroundedRadius);
    }
}
