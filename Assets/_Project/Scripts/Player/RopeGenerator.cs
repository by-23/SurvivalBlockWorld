using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class RopeGenerator : MonoBehaviour
{
    private static RopeGenerator _instance;

    public static RopeGenerator Instance
    {
        get { return _instance; }
    }

    [SerializeField] int _ropeCountLimit = 5;
    [SerializeField] Transform _handPivot;
    [SerializeField] Hook _hookPrefab;

    [SerializeField] Rope _ropePrefab;
    [SerializeField] float _slowdownDistance = 3f;
    [SerializeField] float _slowdownSpeed = 2f;
    const float HookRayDistance = 300f;

    [SerializeField] float _deflectPower = 50;
    [SerializeField] LayerMask _layerMask;
    [SerializeField] LayerMask _groundLayerMask;
    [SerializeField] Camera _camera;
    public List<Rope> _ropes;
    readonly List<AttachmentRecord> _attachments = new();

    private InputManager _input;
    private Rope _rope;
    private bool _isCompleted = true;
    private Vector3 _oldAngles;
    private Player _player;

    void Awake()
    {
        _instance = this;
        _isCompleted = true;
        _player = GetComponentInParent<Player>();
        _ropes ??= new List<Rope>(_ropeCountLimit);
    }

    void Update()
    {
        CleanupDestroyedAttachments();
        ApplyProximitySlowdown();
    }

    public void Hook()
    {
        if (!TryGetHookHit(out RaycastHit hit))
            return;

        Hook hook = CreateHook(hit);
        hook.joint.connectedBody = ResolveConnectedBody(hit);
        GameObject targetObject = hit.collider ? hit.collider.gameObject : null;

        if (_isCompleted)
        {
            PrepareNewRope(hook, targetObject);
            _isCompleted = false;
            return;
        }

        if (CompleteCurrentRope(hook, targetObject))
        {
            _isCompleted = true;
            return;
        }

        _isCompleted = true;
    }

    public void Cancel()
    {
        if (_isCompleted) return;

        if (_ropes.Count != 0)
        {
            Rope rope = _ropes[_ropes.Count - 1];
            DetachRope(rope);
        }

        _isCompleted = true;
    }

    public void Clear()
    {
        for (int i = _ropes.Count - 1; i >= 0; i--)
        {
            Rope rope = _ropes[i];
            DetachRope(rope);
        }

        _isCompleted = true;
    }

    bool TryGetHookHit(out RaycastHit hit)
    {
        hit = default;

        if (!_camera)
            return false;

        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);
        return Physics.Raycast(ray, out hit, HookRayDistance, _layerMask);
    }

    Hook CreateHook(RaycastHit hit)
    {
        Hook hook = Instantiate(_hookPrefab);
        hook.transform.position = hit.point;
        hook.transform.SetParent(hit.collider.transform, true);
        return hook;
    }

    Rigidbody ResolveConnectedBody(RaycastHit hit)
    {
        Entity entity = hit.collider.GetComponentInParent<Entity>();
        if (entity != null)
        {
            entity.EnablePhysics();
            Rigidbody entityRb = entity.GetComponent<Rigidbody>();
            if (entityRb)
                return entityRb;
        }

        Rigidbody rb = hit.rigidbody ? hit.rigidbody : hit.collider.GetComponent<Rigidbody>();
        if (rb)
        {
            if (rb.isKinematic && !IsGroundLayer(hit.collider.gameObject.layer))
                rb.isKinematic = false;
            return rb;
        }

        bool isGround = IsGroundLayer(hit.collider.gameObject.layer);
        Rigidbody addedRb = hit.collider.gameObject.AddComponent<Rigidbody>();
        addedRb.isKinematic = isGround;
        addedRb.mass = 1f;
        addedRb.drag = 0.5f;
        addedRb.angularDrag = 0.5f;
        return addedRb;
    }

    void PrepareNewRope(Hook hook, GameObject targetObject)
    {
        if (_ropes.Count >= _ropeCountLimit)
            Clear();

        _rope = Instantiate(_ropePrefab);
        _rope.ropeGenerator = this;
        _ropes.Add(_rope);

        _rope.hooks[0] = hook.gameObject;
        _rope.hooks[1] = _handPivot.gameObject;
        RegisterAttachment(targetObject, _rope);
    }

    bool CompleteCurrentRope(Hook hook, GameObject targetObject)
    {
        if (!_rope)
            return false;

        _rope.hooks[1] = hook.gameObject;
        UpdateHookTargets(_rope);
        RegisterAttachment(targetObject, _rope);
        return true;
    }

    void UpdateHookTargets(Rope rope)
    {
        for (int i = 0; i < rope.hooks.Length; i++)
        {
            Hook ropeHook = rope.hooks[i].GetComponent<Hook>();

            if (!ropeHook || !ropeHook.enabled)
                continue;

            ropeHook.rope = rope;
            ropeHook.target = i == 0 ? rope.hooks[1].transform : rope.hooks[0].transform;
        }
    }

    bool IsGroundLayer(int layer)
    {
        return (_groundLayerMask.value & (1 << layer)) != 0;
    }

    void RegisterAttachment(GameObject targetObject, Rope rope)
    {
        if (!targetObject || !rope)
            return;

        if (targetObject == _handPivot.gameObject)
            return;

        for (int i = 0; i < _attachments.Count; i++)
        {
            AttachmentRecord record = _attachments[i];
            if (record.Rope == rope && record.Target == targetObject)
                return;
        }

        _attachments.Add(new AttachmentRecord(targetObject, rope));
    }

    void CleanupDestroyedAttachments()
    {
        if (_attachments.Count == 0)
            return;

        HashSet<Rope> ropesToDetach = null;

        foreach (AttachmentRecord record in _attachments)
        {
            if (record.Target != null)
                continue;

            if (!record.Rope)
                continue;

            ropesToDetach ??= new HashSet<Rope>();
            ropesToDetach.Add(record.Rope);
        }

        if (ropesToDetach == null)
        {
            _attachments.RemoveAll(record => record.Rope == null);
            return;
        }

        foreach (Rope rope in ropesToDetach)
            DetachRope(rope);

        _attachments.RemoveAll(record => record.Rope == null || record.Target == null);
    }

    void ApplyProximitySlowdown()
    {
        if (_slowdownDistance <= 0f || _slowdownSpeed < 0f)
            return;

        if (_ropes == null || _ropes.Count == 0)
            return;

        float slowdownDistanceSqr = _slowdownDistance * _slowdownDistance;
        float targetSpeed = Mathf.Max(0f, _slowdownSpeed);

        for (int i = 0; i < _ropes.Count; i++)
        {
            Rope rope = _ropes[i];
            if (!rope || rope.hooks == null || rope.hooks.Length < 2)
                continue;

            GameObject firstHook = rope.hooks[0];
            GameObject secondHook = rope.hooks[1];

            if (!firstHook || !secondHook)
                continue;

            Vector3 delta = firstHook.transform.position - secondHook.transform.position;
            if (delta.sqrMagnitude >= slowdownDistanceSqr)
                continue;

            ApplyRigidbodySlowdown(GetConnectedBody(firstHook), targetSpeed);
            ApplyRigidbodySlowdown(GetConnectedBody(secondHook), targetSpeed);
        }
    }

    Rigidbody GetConnectedBody(GameObject hookObject)
    {
        if (!hookObject)
            return null;

        if (!hookObject.TryGetComponent(out Hook hook) || !hook.joint)
            return null;

        return hook.joint.connectedBody;
    }

    void ApplyRigidbodySlowdown(Rigidbody rb, float targetSpeed)
    {
        if (!rb || rb.isKinematic)
            return;

        Vector3 velocity = rb.velocity;
        float targetSqr = targetSpeed * targetSpeed;

        if (velocity.sqrMagnitude <= targetSqr)
            return;

        rb.velocity = velocity.normalized * targetSpeed;
    }

    void DetachRope(Rope rope)
    {
        if (!rope)
            return;

        rope.Clear();
    }

    void RemoveAttachmentsForRope(Rope rope)
    {
        _attachments.RemoveAll(record => record.Rope == rope);
    }

    public void OnRopeCleared(Rope rope)
    {
        if (!rope)
            return;

        RemoveAttachmentsForRope(rope);
        _ropes.Remove(rope);

        if (_rope == rope)
            _rope = null;

        _isCompleted = true;
    }

    class AttachmentRecord
    {
        public readonly GameObject Target;
        public readonly Rope Rope;

        public AttachmentRecord(GameObject targetObject, Rope ropeInstance)
        {
            Target = targetObject;
            Rope = ropeInstance;
        }
    }
}
