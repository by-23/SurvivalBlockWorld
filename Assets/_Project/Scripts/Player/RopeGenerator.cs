using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.VisualScripting;

public class RopeGenerator : MonoBehaviour
{
    private static RopeGenerator _instance;
    public static RopeGenerator Instance { get { return _instance; } }

    [SerializeField] int _ropeCountLimit = 5;
    [SerializeField] Transform _handPivot;
    [SerializeField] Hook _hookPrefab;
    [SerializeField] Rope _ropePrefab;
    // public Ragdoll _ragdoll;
    [SerializeField] float _deflectPower = 50;
    [SerializeField] LayerMask _layerMask;
    [SerializeField] Camera _camera;
    public List<Rope> _ropes;

    private InputManager _input;
    private Rope _rope;
    private bool _isCompleted;
    private Vector3 _oldAngles;
    private Player _player;

    void Awake()
    {
        _instance = this;

        _player = GetComponentInParent<Player>();

        _isCompleted = true;
    }

    /*private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            Hook();

        if (Input.GetKeyDown(KeyCode.O))
            Clear();
    }*/

    public void Hook()
    {
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 300, _layerMask))
        {
            Hook hook = Instantiate(_hookPrefab);
            hook.transform.position = hit.point;
            hook.transform.SetParent(hit.collider.gameObject.transform);

            Rigidbody rb = hit.transform.GetComponent<Rigidbody>();

            /*Cube cube = hook.GetComponentInParent<Cube>();

            if (cube)
                cube.gameObject.name = "Hook";*/

            if (rb)
            {
                hook.joint.connectedBody = rb;
            }
            else
            {

                Rigidbody _addRB = hit.collider.AddComponent<Rigidbody>();
                _addRB.isKinematic = true;

                hook.joint.connectedBody = _addRB;
            }

            if (_isCompleted)
            {
                /// Clear Ropes ///
                if (_ropes.Count >= _ropeCountLimit)
                    Clear();
                /// Clear Ropes END ///

                _rope = Instantiate(_ropePrefab).GetComponent<Rope>();
                _rope.ropeGenerator = this;

                _ropes.Add(_rope);

                _rope.hooks[0] = hook.gameObject;
                _rope.hooks[1] = _handPivot.gameObject;

            }
            else
            {
                if (!_rope)
                {
                    _isCompleted = !_isCompleted;
                    _rope = null;
                    return;
                }

                _rope.hooks[1] = hook.gameObject;


                for (int i = 0; i < _rope.hooks.Length; i++)
                {
                    var _hook = _rope.hooks[i].GetComponent<Hook>();

                    if (_hook)
                    {
                        if (_hook.enabled)
                        {

                            _hook.rope = _rope;

                            if (i == 0)
                            {
                                _hook.target = _rope.hooks[1].transform;
                            }
                            else if (i == 1)
                            {
                                _hook.target = _rope.hooks[0].transform;
                            }

                            // Ragdoll ragdoll = _rope.hooks[i].GetComponentInParent<Ragdoll>();
                            //
                            // if (ragdoll)
                            //     ragdoll.ChangeKinematic(_rope);

                        }
                    }
                }

            }
            _isCompleted = !_isCompleted;
        }
    }

    public void Cancel()
    {
        if (_isCompleted) return;

        if (_ropes.Count != 0)
        {
            _ropes[_ropes.Count - 1].Clear();
            _ropes.RemoveAt(_ropes.Count - 1);
        }

        _isCompleted = true;
    }

    public void Clear()
    {

        foreach (Rope rope in _ropes)
        {
            if (rope)
                rope.Clear();
        }

        _ropes.Clear();

        //CharacterManager.instance.JointsDisconnect();

        _isCompleted = true;
    }
}
