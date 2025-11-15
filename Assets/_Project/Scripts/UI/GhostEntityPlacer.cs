using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Управляет ghost-предпросмотром Entity при размещении сохранённого объекта
    /// </summary>
    public class GhostEntityPlacer : MonoBehaviour
    {
        [Header("Placement Settings")] [SerializeField]
        private LayerMask _surfaceLayerMask = 1;

        [SerializeField] private float _maxGhostDistance = 8f;
        [SerializeField] private float _minGhostDistance = 2f;

        [SerializeField] private float _distanceMultiplier = 1f;

        [Header("Ghost Materials")] [SerializeField]
        private Material _ghostMaterialGreen;

        [SerializeField] private Material _ghostMaterialRed;

        [SerializeField] private float _ghostTransparency = 0.5f;

        private Entity _ghostEntity;
        private Camera _playerCamera;
        private readonly HashSet<Collider> _overlappingColliders = new HashSet<Collider>();

        private readonly List<Renderer> _combinedRenderers = new List<Renderer>();
        private EntityMaterialColorizer _materialColorizer;

        private Rigidbody _cachedRigidbody;
        private EntityMeshCombiner _cachedMeshCombiner;
        private Transform _cachedCombinedMeshObject;
        private Renderer[] _cachedCombinedMeshRenderers;
        private Collider _triggerCollider;
        private GameObject _cachedCubesHolder;
        private bool _wasCubesHolderActive;

        private bool _isActive;
        private bool _canPlace;
        private Bounds _cachedEntityBounds;
        private float _cachedEntityMaxSize;
        private Vector3 _lastValidPosition;
        private bool _hasLastValidPosition;
        private Vector3 _boundsCenterOffset;
        private float _cachedLowestLocalBottomY = float.MaxValue;
        private float _minRaycastHitY = float.MinValue;

        public bool IsActive => _isActive;
        public bool CanPlace => _isActive && _canPlace;

        private void Awake()
        {
            _playerCamera = Camera.main;
        }

        private void Update()
        {
            if (_isActive && _ghostEntity != null)
            {
                UpdateGhostPosition();
            }
        }

        internal void OnTriggerEnterInternal(Collider other)
        {
            if (_ghostEntity == null || other.transform.root == _ghostEntity.transform.root)
                return;

            int otherLayer = other.gameObject.layer;
            if ((_surfaceLayerMask & (1 << otherLayer)) != 0)
                return;

            _overlappingColliders.Add(other);
        }

        internal void OnTriggerExitInternal(Collider other)
        {
            _overlappingColliders.Remove(other);
        }

        public void Begin(Entity entity, Camera camera = null)
        {
            if (entity == null)
            {
                Debug.LogError("GhostEntityPlacer.Begin: entity is null");
                return;
            }

            if (camera != null)
                _playerCamera = camera;
            else if (_playerCamera == null)
                _playerCamera = Camera.main;

            _ghostEntity = entity;
            _overlappingColliders.Clear();

            _cachedMeshCombiner = _ghostEntity.GetComponent<EntityMeshCombiner>();
            if (_cachedMeshCombiner != null && !_cachedMeshCombiner.IsCombined)
            {
                _cachedMeshCombiner.CombineMeshes();
            }

            _cachedRigidbody = _ghostEntity.GetComponent<Rigidbody>();
            _cachedCombinedMeshObject = _ghostEntity.transform.Find("CombinedMesh");
            if (_cachedCombinedMeshObject != null)
            {
                _cachedCombinedMeshRenderers = _cachedCombinedMeshObject.GetComponentsInChildren<Renderer>();
            }
            else
            {
                _cachedCombinedMeshRenderers = null;
            }

            CollectCombinedRenderers();
            CacheEntityBounds();
            CacheLowestLocalBottomY();
            DisableCubesHolder();
            CreateTriggerCollider();
            InitializeMaterialColorizer();

            if (_cachedRigidbody != null)
            {
                _ghostEntity.SetKinematicState(true, true);
                _cachedRigidbody.useGravity = false;
            }

            _ghostEntity.SetGhostMode(true);
            _isActive = true;
            _minRaycastHitY = float.MinValue;
            UpdateGhostPosition();
        }

        public bool TryConfirm()
        {
            if (!_isActive || !_canPlace || _ghostEntity == null)
                return false;

            if (_materialColorizer != null)
            {
                _materialColorizer.Restore();
                _materialColorizer = null;
            }

            DestroyTriggerCollider();
            EnableCubesHolder();
            _ghostEntity.SetGhostMode(false);

            if (_cachedRigidbody != null)
            {
                _ghostEntity.SetKinematicState(false);
                _cachedRigidbody.useGravity = true;
            }

            ClearCache();
            return true;
        }

        public void Cancel()
        {
            if (_ghostEntity != null)
            {
                if (_materialColorizer != null)
                {
                    _materialColorizer.Restore();
                    _materialColorizer = null;
                }

                DestroyTriggerCollider();
                EnableCubesHolder();
                _ghostEntity.SetGhostMode(false);
                Destroy(_ghostEntity.gameObject);
            }

            ClearCache();
        }

        private void ClearCache()
        {
            _ghostEntity = null;
            _cachedRigidbody = null;
            _cachedMeshCombiner = null;
            _cachedCombinedMeshObject = null;
            _cachedCombinedMeshRenderers = null;
            _cachedCubesHolder = null;
            _isActive = false;
            _canPlace = false;
            _overlappingColliders.Clear();
            _cachedEntityMaxSize = 0f;
            _cachedEntityBounds = new Bounds();
            _hasLastValidPosition = false;
            _cachedLowestLocalBottomY = float.MaxValue;
            _minRaycastHitY = float.MinValue;
        }

        private void DisableCubesHolder()
        {
            if (_ghostEntity == null)
                return;

            FieldInfo field = typeof(Entity).GetField("_cubesHolder", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                _cachedCubesHolder = field.GetValue(_ghostEntity) as GameObject;
                if (_cachedCubesHolder != null)
                {
                    _wasCubesHolderActive = _cachedCubesHolder.activeSelf;
                    _cachedCubesHolder.SetActive(false);
                }
            }
        }

        private void EnableCubesHolder()
        {
            if (_cachedCubesHolder != null)
            {
                _cachedCubesHolder.SetActive(_wasCubesHolderActive);
            }
        }

        private void UpdateGhostPosition()
        {
            if (_ghostEntity == null || _playerCamera == null)
                return;

            if (_cachedRigidbody != null && !_ghostEntity.IsKinematic)
            {
                _ghostEntity.SetKinematicState(true, true);
                _cachedRigidbody.useGravity = false;
            }

            Vector3 targetPosition = GetGhostPosition();
            if (targetPosition == Vector3.zero)
            {
                _ghostEntity.gameObject.SetActive(false);
                return;
            }

            _ghostEntity.gameObject.SetActive(true);

            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 toPosition = targetPosition - cameraPosition;
            float distanceToCamera = toPosition.magnitude;

            if (distanceToCamera < _minGhostDistance)
            {
                if (_hasLastValidPosition)
                {
                    Vector3 toLastPosition = _lastValidPosition - cameraPosition;
                    if (toLastPosition.magnitude >= _minGhostDistance)
                    {
                        targetPosition = _lastValidPosition;
                    }
                    else
                    {
                        Vector3 direction = toLastPosition.normalized;
                        if (direction.sqrMagnitude < 0.0001f)
                            direction = _playerCamera.transform.forward;
                        targetPosition = cameraPosition + direction * _minGhostDistance;
                        _lastValidPosition = targetPosition;
                    }
                }
                else
                {
                    Vector3 direction = toPosition.normalized;
                    if (direction.sqrMagnitude < 0.0001f)
                        direction = _playerCamera.transform.forward;
                    targetPosition = cameraPosition + direction * _minGhostDistance;
                    _lastValidPosition = targetPosition;
                    _hasLastValidPosition = true;
                }
            }
            else
            {
                _lastValidPosition = targetPosition;
                _hasLastValidPosition = true;
            }

            Vector3 toCamera = cameraPosition - targetPosition;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.000001f)
            {
                _ghostEntity.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            Vector3 centeringOffset = GetWorldCenterOffset();
            if (centeringOffset.sqrMagnitude > 0.000001f)
            {
                targetPosition -= new Vector3(centeringOffset.x, 0f, centeringOffset.z);
            }

            if (_minRaycastHitY != float.MinValue)
            {
                float entityBottomY =
                    targetPosition.y + (_cachedLowestLocalBottomY * _ghostEntity.transform.lossyScale.y);
                if (entityBottomY < _minRaycastHitY)
                {
                    float deltaY = _minRaycastHitY - entityBottomY;
                    targetPosition.y += deltaY;
                }
            }

            _ghostEntity.transform.position = targetPosition;

            _canPlace = _overlappingColliders.Count == 0;

            if (_materialColorizer != null)
            {
                _materialColorizer.UpdateColor(!_canPlace);
            }
        }

        private Vector3 GetGhostPosition()
        {
            if (_playerCamera == null)
                return Vector3.zero;

            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 cameraForward = _playerCamera.transform.forward;

            float pitchAngle = _playerCamera.transform.eulerAngles.x;
            if (pitchAngle > 180f) pitchAngle -= 360f;
            pitchAngle = -pitchAngle;

            float normalizedAngle = (pitchAngle + 90f) / 180f;
            float baseDistance = Mathf.Lerp(_minGhostDistance, _maxGhostDistance, normalizedAngle);

            float targetDistance = baseDistance;
            if (_cachedEntityMaxSize > 0f)
            {
                targetDistance = baseDistance + (_cachedEntityMaxSize * _distanceMultiplier);
            }

            targetDistance = Mathf.Max(targetDistance, _minGhostDistance);

            float raycastMaxDistance = Mathf.Max(targetDistance,
                _maxGhostDistance + (_cachedEntityMaxSize > 0f ? _cachedEntityMaxSize * _distanceMultiplier : 0f));
            Ray ray = new Ray(cameraPosition, cameraForward);
            RaycastHit[] hits =
                Physics.RaycastAll(ray, raycastMaxDistance, _surfaceLayerMask, QueryTriggerInteraction.Ignore);

            RaycastHit? validHit = null;
            float closestHitDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (_ghostEntity != null && hit.collider.transform.root == _ghostEntity.transform.root)
                    continue;

                if (hit.distance < closestHitDistance)
                {
                    closestHitDistance = hit.distance;
                    validHit = hit;
                }
            }

            if (validHit.HasValue)
            {
                var hit = validHit.Value;

                if (_ghostEntity != null && _cachedLowestLocalBottomY != float.MaxValue)
                {
                    float minBottomY = hit.point.y;
                    if (_minRaycastHitY == float.MinValue || minBottomY < _minRaycastHitY)
                    {
                        _minRaycastHitY = minBottomY;
                    }
                }

                return hit.point;
            }

            return cameraPosition + cameraForward * targetDistance;
        }

        private void CreateTriggerCollider()
        {
            if (_ghostEntity == null || _cachedEntityBounds.size.magnitude < 0.001f)
                return;

            DestroyTriggerCollider();

            GameObject triggerObj = new GameObject("GhostTriggerCollider");
            triggerObj.transform.SetParent(_ghostEntity.transform, false);
            triggerObj.layer = _ghostEntity.gameObject.layer;

            BoxCollider trigger = triggerObj.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = _cachedEntityBounds.center - _ghostEntity.transform.position;
            trigger.size = _cachedEntityBounds.size;

            GhostTriggerHelper helper = triggerObj.AddComponent<GhostTriggerHelper>();
            helper.Initialize(this);

            _triggerCollider = trigger;
        }

        private void DestroyTriggerCollider()
        {
            if (_triggerCollider != null)
            {
                Destroy(_triggerCollider.gameObject);
                _triggerCollider = null;
            }
        }

        private void InitializeMaterialColorizer()
        {
            if (_ghostEntity == null)
                return;

            _materialColorizer = new EntityMaterialColorizer(_ghostMaterialRed, _ghostTransparency);
            _materialColorizer.Initialize(_combinedRenderers);
        }

        private void CollectCombinedRenderers()
        {
            _combinedRenderers.Clear();
            if (_ghostEntity == null) return;

            var allRenderers = _ghostEntity.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null || r.GetComponent<Cube>() != null) continue;
                _combinedRenderers.Add(r);
            }
        }

        private void CacheLowestLocalBottomY()
        {
            _cachedLowestLocalBottomY = float.MaxValue;

            if (_ghostEntity == null || _cachedCombinedMeshRenderers == null)
                return;

            for (int i = 0; i < _cachedCombinedMeshRenderers.Length; i++)
            {
                var renderer = _cachedCombinedMeshRenderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                try
                {
                    Bounds rendererBounds = renderer.bounds;
                    Vector3 rendererBottomWorld = new Vector3(
                        rendererBounds.center.x,
                        rendererBounds.min.y,
                        rendererBounds.center.z
                    );

                    Vector3 rendererBottomLocal = _ghostEntity.transform.InverseTransformPoint(rendererBottomWorld);
                    float rendererBottomLocalY = rendererBottomLocal.y;

                    if (!float.IsNaN(rendererBottomLocalY) && !float.IsInfinity(rendererBottomLocalY) &&
                        Mathf.Abs(rendererBottomLocalY) <= 1000f && rendererBottomLocalY < _cachedLowestLocalBottomY)
                    {
                        _cachedLowestLocalBottomY = rendererBottomLocalY;
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
            }
        }

        private void CacheEntityBounds()
        {
            _cachedEntityMaxSize = 0f;
            _cachedEntityBounds = new Bounds();
            _boundsCenterOffset = Vector3.zero;

            if (_ghostEntity == null || _combinedRenderers.Count == 0)
                return;

            bool first = true;
            for (int i = 0; i < _combinedRenderers.Count; i++)
            {
                var r = _combinedRenderers[i];
                if (r == null) continue;

                try
                {
                    if (first)
                    {
                        _cachedEntityBounds = r.bounds;
                        first = false;
                    }
                    else
                    {
                        _cachedEntityBounds.Encapsulate(r.bounds);
                    }
                }
                catch (MissingReferenceException)
                {
                    continue;
                }
            }

            Vector3 size = _cachedEntityBounds.size;
            _cachedEntityMaxSize = Mathf.Max(size.x, size.y, size.z);

            Vector3 boundsCenterWorldOffset = _cachedEntityBounds.center - _ghostEntity.transform.position;
            _boundsCenterOffset = _ghostEntity.transform.InverseTransformVector(boundsCenterWorldOffset);
        }

        private Vector3 GetWorldCenterOffset()
        {
            if (_ghostEntity == null)
                return Vector3.zero;
            return _ghostEntity.transform.TransformVector(_boundsCenterOffset);
        }
    }

    public class GhostTriggerHelper : MonoBehaviour
    {
        private GhostEntityPlacer _placer;

        public void Initialize(GhostEntityPlacer placer)
        {
            _placer = placer;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_placer != null)
                _placer.OnTriggerEnterInternal(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_placer != null)
                _placer.OnTriggerExitInternal(other);
        }
    }
}
