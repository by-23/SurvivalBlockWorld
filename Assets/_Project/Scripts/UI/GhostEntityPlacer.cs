using System.Collections.Generic;
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

        [SerializeField] private LayerMask _blockedLayerMask = ~0;

        [SerializeField] private float _maxGhostDistance = 8f;
        [SerializeField] private float _minGhostDistance = 2f;

        [SerializeField]
        private float _distanceMultiplier = 1f; // Множитель для изменения скорости изменения расстояния

        [Header("Ghost Materials")] [SerializeField]
        private Material _ghostMaterialGreen;

        [SerializeField] private Material _ghostMaterialRed;

        [SerializeField] private float _ghostTransparency = 0.5f;

        private Entity _ghostEntity;
        private Camera _playerCamera;
        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();

        private readonly Dictionary<Renderer, MaterialPropertyBlock> _originalPropertyBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

        private readonly List<Renderer> _combinedRenderers = new List<Renderer>();
        private readonly Dictionary<Collider, bool> _originalColliderTriggers = new Dictionary<Collider, bool>();

        private MaterialPropertyBlock _mpb;
        private bool _hasLastSurface;
        private Vector3 _lastSurfacePoint;
        private Vector3 _lastSurfaceNormal;
        private bool _isActive;
        private bool _canPlace;
        private Bounds _cachedEntityBounds;
        private float _cachedEntityMaxSize;
        private Vector3 _lastValidPosition;
        private bool _hasLastValidPosition;
        private float _entityBottomExtent;
        private Vector3 _boundsCenterOffset;
        private float _cachedLowestCubeLocalBottomY = float.MaxValue;

        public bool IsActive => _isActive;

        public bool CanPlace => _isActive && _canPlace;

        private void Awake()
        {
            _playerCamera = Camera.main;
            _mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (_isActive && _ghostEntity != null)
            {
                UpdateGhostPosition();
            }
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

            var meshCombiner = _ghostEntity.GetComponent<EntityMeshCombiner>();
            if (meshCombiner != null)
            {
                if (!meshCombiner.IsCombined)
                {
                    meshCombiner.CombineMeshes();
                }
            }
            else
            {
                var cubes = _ghostEntity.Cubes;
                if (cubes != null)
                {
                    for (int i = 0; i < cubes.Length; i++)
                    {
                        if (cubes[i] != null)
                        {
                            var renderer = cubes[i].GetComponent<Renderer>();
                            if (renderer != null) renderer.enabled = false;
                        }
                    }
                }
            }

            CollectCombinedRenderers();
            CacheEntityBounds();
            CacheLowestCubeLocalBottomY();
            SetCubesTriggers(true);
            CacheAndApplyGhostMaterials();

            var rb = _ghostEntity.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            _ghostEntity.SetGhostMode(true);

            _isActive = true;
            UpdateGhostPosition();
        }

        public bool TryConfirm()
        {
            if (!_isActive || !_canPlace || _ghostEntity == null)
                return false;

            RestoreOriginalMaterials();
            RestoreCubesTriggers();
            _ghostEntity.SetGhostMode(false);

            var rb = _ghostEntity.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            _ghostEntity = null;
            _isActive = false;
            _canPlace = false;

            return true;
        }

        public void Cancel()
        {
            if (_ghostEntity != null)
            {
                RestoreOriginalMaterials();
                RestoreCubesTriggers();
                _ghostEntity.SetGhostMode(false);
                Destroy(_ghostEntity.gameObject);
            }

            _ghostEntity = null;
            _isActive = false;
            _canPlace = false;
            _originalMaterials.Clear();
            _originalColliderTriggers.Clear();
            _cachedEntityMaxSize = 0f;
            _cachedEntityBounds = new Bounds();
            _hasLastSurface = false;
            _hasLastValidPosition = false;
            _cachedLowestCubeLocalBottomY = float.MaxValue;
        }

        private void UpdateGhostPosition()
        {
            if (_ghostEntity == null || _playerCamera == null)
                return;

            var rb = _ghostEntity.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            Vector3 targetPosition = GetGhostPosition();
            if (targetPosition == Vector3.zero)
            {
                _ghostEntity.gameObject.SetActive(false);
                return;
            }

            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 toPosition = targetPosition - cameraPosition;
            float distanceToCamera = toPosition.magnitude;

            if (distanceToCamera < _minGhostDistance)
            {
                if (_hasLastValidPosition)
                {
                    Vector3 toLastPosition = _lastValidPosition - cameraPosition;
                    float distanceToLast = toLastPosition.magnitude;

                    if (distanceToLast >= _minGhostDistance)
                    {
                        targetPosition = _lastValidPosition;
                    }
                    else
                    {
                        Vector3 direction = toLastPosition.normalized;
                        if (direction.sqrMagnitude < 0.0001f)
                        {
                            direction = _playerCamera.transform.forward;
                        }

                        targetPosition = cameraPosition + direction * _minGhostDistance;
                        _lastValidPosition = targetPosition;
                    }
                }
                else
                {
                    Vector3 direction = toPosition.normalized;
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = _playerCamera.transform.forward;
                    }

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

            _ghostEntity.gameObject.SetActive(true);

            Vector3 toCamera = cameraPosition - targetPosition;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.000001f)
            {
                _ghostEntity.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            if (_hasLastSurface)
            {
                targetPosition = GetSurfacePosition(_lastSurfacePoint, _lastSurfaceNormal);
            }

            _ghostEntity.transform.position = targetPosition;
            _canPlace = IsGrounded(targetPosition) && !IsPositionOccupied(targetPosition) && !HasCollisionWithObjects();
            UpdateGhostMaterial(_canPlace);
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
            float targetDistance = Mathf.Lerp(_minGhostDistance, _maxGhostDistance, normalizedAngle);
            targetDistance *= _distanceMultiplier;

            if (_cachedEntityMaxSize > 0f)
            {
                float sizeOffset = _cachedEntityMaxSize * 0.5f;
                targetDistance = Mathf.Max(targetDistance, _minGhostDistance + sizeOffset);
            }

            Ray ray = new Ray(cameraPosition, cameraForward);
            RaycastHit[] hits =
                Physics.RaycastAll(ray, targetDistance, _surfaceLayerMask, QueryTriggerInteraction.Ignore);

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
                _hasLastSurface = true;
                _lastSurfacePoint = hit.point;
                _lastSurfaceNormal = hit.normal;

                return GetSurfacePosition(hit.point, hit.normal);
            }

            if (_hasLastSurface)
            {
                Plane plane = new Plane(_lastSurfaceNormal, _lastSurfacePoint);
                if (plane.Raycast(ray, out float planeDist))
                {
                    float d = Mathf.Clamp(planeDist, _minGhostDistance, _maxGhostDistance);
                    Vector3 onPlane = ray.GetPoint(d);
                    return GetSurfacePosition(onPlane, _lastSurfaceNormal);
                }

                Vector3 tangent = Vector3.ProjectOnPlane(cameraForward, _lastSurfaceNormal).normalized;
                Vector3 slide = _lastSurfacePoint + tangent * targetDistance;
                return GetSurfacePosition(slide, _lastSurfaceNormal);
            }

            return cameraPosition + cameraForward.normalized * targetDistance;
        }

        private Vector3 GetSurfacePosition(Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            if (_ghostEntity == null)
                return surfacePoint;

            if (_cachedLowestCubeLocalBottomY == float.MaxValue)
            {
                CacheLowestCubeLocalBottomY();
            }

            float lowestCubeLocalBottomY = _cachedLowestCubeLocalBottomY;
            if (lowestCubeLocalBottomY == float.MaxValue)
            {
                return surfacePoint;
            }

            float raycastDistance = 10f;
            Vector3 rayOrigin = surfacePoint + Vector3.up * 0.1f;
            Vector3 rayDirection = Vector3.down;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, raycastDistance,
                    _surfaceLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (_ghostEntity != null && hit.collider.transform.root == _ghostEntity.transform.root)
                {
                    return surfacePoint;
                }

                float surfaceY = hit.point.y;
                if (float.IsNaN(surfaceY) || float.IsInfinity(surfaceY))
                {
                    return _ghostEntity.transform.position;
                }

                float entityScaleY = _ghostEntity.transform.lossyScale.y;
                if (float.IsNaN(entityScaleY) || float.IsInfinity(entityScaleY) || entityScaleY <= 0f)
                {
                    entityScaleY = 1f;
                }

                if (float.IsNaN(lowestCubeLocalBottomY) || float.IsInfinity(lowestCubeLocalBottomY))
                {
                    return _ghostEntity.transform.position;
                }

                float surfaceOffset = 0.1f;
                float entityY = surfaceY - (lowestCubeLocalBottomY * entityScaleY) + surfaceOffset;

                if (float.IsNaN(entityY) || float.IsInfinity(entityY))
                {
                    return _ghostEntity.transform.position;
                }

                const float maxCoordinate = 10000f;
                if (Mathf.Abs(entityY) > maxCoordinate || Mathf.Abs(surfacePoint.x) > maxCoordinate ||
                    Mathf.Abs(surfacePoint.z) > maxCoordinate)
                {
                    return _ghostEntity.transform.position;
                }

                Vector3 currentPosition = _ghostEntity.transform.position;
                float maxPositionChange = 10f;
                float positionDelta = entityY - currentPosition.y;

                if (Mathf.Abs(positionDelta) > maxPositionChange)
                {
                    entityY = currentPosition.y + Mathf.Sign(positionDelta) * maxPositionChange;
                }

                return new Vector3(surfacePoint.x, entityY, surfacePoint.z);
            }

            return surfacePoint;
        }


        private bool IsPositionOccupied(Vector3 position)
        {
            if (_ghostEntity == null)
                return false;

            var renderers = _combinedRenderers;
            if (renderers == null || renderers.Count == 0)
                return false;

            // Вычисляем bounds entity по комбинированным рендерам
            Renderer firstRenderer = null;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                {
                    firstRenderer = renderers[i];
                    break;
                }
            }

            if (firstRenderer == null)
                return false;

            Bounds combined;
            try
            {
                combined = firstRenderer.bounds;
            }
            catch (MissingReferenceException)
            {
                // Рендерер был уничтожен
                return false;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r != null && r != firstRenderer)
                {
                    try
                    {
                        combined.Encapsulate(r.bounds);
                    }
                    catch (MissingReferenceException)
                    {
                        // Рендерер был уничтожен - пропускаем
                        continue;
                    }
                }
            }

            Vector3 currentCenter = combined.center;
            Vector3 centerOffset = currentCenter - _ghostEntity.transform.position;
            Vector3 halfExtents = combined.extents;

            Vector3 testCenter = position + centerOffset;

            // Проверяем пересечения с объектами на заблокированных слоях
            Collider[] hits = Physics.OverlapBox(testCenter, halfExtents, _ghostEntity.transform.rotation,
                _blockedLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                if (_ghostEntity != null && c.transform.root == _ghostEntity.transform.root)
                    continue;
                return true;
            }

            return false;
        }

        private bool IsGrounded(Vector3 position)
        {
            float dist = 2;
            Vector3 origin = position + Vector3.up * 0.01f;
            return Physics.Raycast(origin, Vector3.down, dist, _surfaceLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void SetCubesTriggers(bool isTrigger)
        {
            _originalColliderTriggers.Clear();

            if (_ghostEntity == null)
                return;

            Cube[] cubes = _ghostEntity.Cubes;
            if (cubes == null || cubes.Length == 0)
                return;

            foreach (Cube cube in cubes)
            {
                if (cube == null) continue;

                Collider cubeCollider = cube.GetComponent<Collider>();
                if (cubeCollider != null)
                {
                    _originalColliderTriggers[cubeCollider] = cubeCollider.isTrigger;
                    cubeCollider.isTrigger = isTrigger;
                }
            }
        }

        private void RestoreCubesTriggers()
        {
            foreach (var kvp in _originalColliderTriggers)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.isTrigger = kvp.Value;
                }
            }

            _originalColliderTriggers.Clear();
        }

        private bool HasCollisionWithObjects()
        {
            if (_ghostEntity == null)
                return false;

            Cube[] cubes = _ghostEntity.Cubes;
            if (cubes == null || cubes.Length == 0)
                return false;

            foreach (Cube cube in cubes)
            {
                if (cube == null) continue;

                Collider cubeCollider = cube.GetComponent<Collider>();
                if (cubeCollider == null || !cubeCollider.enabled)
                    continue;

                bool wasTrigger = cubeCollider.isTrigger;
                cubeCollider.isTrigger = false;

                Bounds cubeBounds = cubeCollider.bounds;

                Collider[] nearbyColliders = Physics.OverlapBox(
                    cubeBounds.center,
                    cubeBounds.extents,
                    cube.transform.rotation,
                    ~0,
                    QueryTriggerInteraction.Ignore
                );

                for (int i = 0; i < nearbyColliders.Length; i++)
                {
                    Collider otherCollider = nearbyColliders[i];
                    if (otherCollider == null)
                        continue;

                    if (_ghostEntity != null && otherCollider.transform.root == _ghostEntity.transform.root)
                        continue;

                    int otherLayer = otherCollider.gameObject.layer;
                    if ((_surfaceLayerMask & (1 << otherLayer)) != 0)
                        continue;

                    if (cubeBounds.Intersects(otherCollider.bounds))
                    {
                        bool hasPenetration = Physics.ComputePenetration(
                            cubeCollider,
                            cubeCollider.transform.position,
                            cubeCollider.transform.rotation,
                            otherCollider,
                            otherCollider.transform.position,
                            otherCollider.transform.rotation,
                            out _,
                            out _);

                        cubeCollider.isTrigger = wasTrigger;

                        if (hasPenetration)
                            return true;
                    }
                }

                cubeCollider.isTrigger = wasTrigger;
            }

            return false;
        }

        private void CacheAndApplyGhostMaterials()
        {
            if (_ghostEntity == null)
                return;

            _originalMaterials.Clear();
            _originalPropertyBlocks.Clear();

            for (int i = 0; i < _combinedRenderers.Count; i++)
            {
                var r = _combinedRenderers[i];
                if (r == null) continue;

                _originalMaterials[r] = new Material[r.sharedMaterials.Length];
                for (int j = 0; j < r.sharedMaterials.Length; j++)
                {
                    _originalMaterials[r][j] = r.sharedMaterials[j];
                }

                var savedBlock = new MaterialPropertyBlock();
                r.GetPropertyBlock(savedBlock);
                _originalPropertyBlocks[r] = savedBlock;
            }
        }

        private void UpdateGhostMaterial(bool canPlace)
        {
            for (int i = 0; i < _combinedRenderers.Count; i++)
            {
                var r = _combinedRenderers[i];
                if (r == null) continue;

                if (canPlace)
                {
                    if (_originalMaterials.TryGetValue(r, out var originalMats))
                    {
                        r.sharedMaterials = originalMats;
                    }

                    if (_originalPropertyBlocks.TryGetValue(r, out var originalBlock))
                    {
                        r.SetPropertyBlock(originalBlock);
                    }
                    else
                    {
                        _mpb.Clear();
                        r.SetPropertyBlock(_mpb);
                    }
                }
                else
                {
                    Material targetMaterial = _ghostMaterialRed;

                    if (targetMaterial == null)
                    {
                        targetMaterial = CreateGhostMaterial(true);
                    }

                    Material[] ghostMats = new Material[r.sharedMaterials.Length];
                    for (int j = 0; j < ghostMats.Length; j++)
                    {
                        ghostMats[j] = targetMaterial;
                    }

                    r.sharedMaterials = ghostMats;

                    Color c = new Color(1f, 0f, 0f, _ghostTransparency);
                    _mpb.Clear();
                    _mpb.SetColor("_BaseColor", c);
                    _mpb.SetColor("_Color", c);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        private Material CreateGhostMaterial(bool isRed)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            Color ghostColor = isRed
                ? new Color(1f, 0f, 0f, _ghostTransparency)
                : new Color(0f, 1f, 0f, _ghostTransparency);
            mat.color = ghostColor;

            return mat;
        }

        private void RestoreOriginalMaterials()
        {
            foreach (var kvp in _originalMaterials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                    // Восстанавливаем исходный property block, если он был
                    if (_originalPropertyBlocks.TryGetValue(kvp.Key, out var block))
                    {
                        kvp.Key.SetPropertyBlock(block);
                    }
                    else
                    {
                        _mpb.Clear();
                        kvp.Key.SetPropertyBlock(_mpb);
                    }
                }
            }

            _originalMaterials.Clear();
            _combinedRenderers.Clear();
            _originalPropertyBlocks.Clear();
        }

        private void CollectCombinedRenderers()
        {
            _combinedRenderers.Clear();
            if (_ghostEntity == null) return;

            var allRenderers = _ghostEntity.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null) continue;

                // Пропускаем рендереры кубов (мы их скрываем) — меняем материалы только у комбинированных мешей
                if (r.GetComponent<Cube>() != null) continue;

                _combinedRenderers.Add(r);
            }
        }

        private void CacheLowestCubeLocalBottomY()
        {
            _cachedLowestCubeLocalBottomY = float.MaxValue;

            if (_ghostEntity == null)
                return;

            Cube[] cubes = _ghostEntity.Cubes;
            if (cubes == null || cubes.Length == 0)
                return;

            float lowestLocalY = float.MaxValue;

            foreach (Cube cube in cubes)
            {
                if (cube == null) continue;

                try
                {
                    if (cube.transform.parent != _ghostEntity.transform)
                        continue;

                    Vector3 localPos = cube.transform.localPosition;

                    if (float.IsNaN(localPos.y) || float.IsInfinity(localPos.y))
                        continue;

                    Collider cubeCollider = cube.GetComponent<Collider>();
                    if (cubeCollider != null && cubeCollider.enabled)
                    {
                        float colliderBottomLocalY;

                        BoxCollider boxCollider = cubeCollider as BoxCollider;
                        if (boxCollider != null)
                        {
                            Vector3 colliderCenterOffset = boxCollider.center;
                            colliderCenterOffset.Scale(cube.transform.localScale);
                            Vector3 colliderCenterLocal = localPos + colliderCenterOffset;

                            float colliderHalfHeight = boxCollider.size.y * 0.5f * cube.transform.localScale.y;
                            colliderBottomLocalY = colliderCenterLocal.y - colliderHalfHeight;
                        }
                        else
                        {
                            Vector3 colliderBottomWorld = new Vector3(
                                cubeCollider.bounds.center.x,
                                cubeCollider.bounds.min.y,
                                cubeCollider.bounds.center.z
                            );

                            Vector3 colliderBottomLocal =
                                _ghostEntity.transform.InverseTransformPoint(colliderBottomWorld);
                            colliderBottomLocalY = colliderBottomLocal.y;
                        }

                        if (float.IsNaN(colliderBottomLocalY) || float.IsInfinity(colliderBottomLocalY))
                            continue;

                        if (Mathf.Abs(colliderBottomLocalY) > 1000f)
                            continue;

                        if (colliderBottomLocalY < lowestLocalY)
                        {
                            lowestLocalY = colliderBottomLocalY;
                        }
                    }
                    else
                    {
                        if (Mathf.Abs(localPos.y) > 1000f)
                            continue;

                        if (localPos.y < lowestLocalY)
                        {
                            lowestLocalY = localPos.y;
                        }
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
            }

            if (lowestLocalY != float.MaxValue)
            {
                _cachedLowestCubeLocalBottomY = lowestLocalY;
            }
        }

        private void CacheEntityBounds()
        {
            _cachedEntityMaxSize = 0f;
            _cachedEntityBounds = new Bounds();
            _entityBottomExtent = 0f;
            _boundsCenterOffset = Vector3.zero;

            if (_ghostEntity == null || _combinedRenderers.Count == 0)
                return;

            // Вычисляем общие границы entity
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
                    // Рендерер был уничтожен - пропускаем
                    continue;
                }
            }

            // Вычисляем максимальный размер (диагональ или максимальная сторона)
            Vector3 size = _cachedEntityBounds.size;
            _cachedEntityMaxSize = Mathf.Max(size.x, size.y, size.z);

            // Вычисляем offset от transform.position до центра bounds в локальных координатах
            Vector3 boundsCenterWorldOffset = _cachedEntityBounds.center - _ghostEntity.transform.position;
            _boundsCenterOffset = _ghostEntity.transform.InverseTransformVector(boundsCenterWorldOffset);

            // Вычисляем расстояние от центра bounds до нижней точки bounds в локальных координатах
            Vector3 localSize = _ghostEntity.transform.InverseTransformVector(_cachedEntityBounds.size);
            _entityBottomExtent = localSize.y * 0.5f;
        }
    }
}
