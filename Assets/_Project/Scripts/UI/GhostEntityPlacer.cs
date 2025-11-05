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

        private MaterialPropertyBlock _mpb;

        // Память о последней валидной поверхности под прицелом, чтобы не дёргаться, когда луч уходит в небо
        private bool _hasLastSurface;
        private Vector3 _lastSurfacePoint;
        private Vector3 _lastSurfaceNormal;
        private bool _isActive;
        private bool _canPlace;
        private Bounds _cachedEntityBounds;
        private float _cachedEntityMaxSize;
        private Vector3 _lastValidPosition;
        private bool _hasLastValidPosition;

        private float
            _entityBottomExtent; // расстояние от центра bounds до нижней точки по оси Y в локальных координатах

        private Vector3 _boundsCenterOffset; // offset от transform.position до центра bounds в локальных координатах

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

            // Выключаем кубы, включаем комбинированный меш
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
                // Fallback: выключаем renderers кубов вручную
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

            // Собираем только комбинированные рендереры (исключаем Cube)
            CollectCombinedRenderers();

            // Кешируем размеры entity для правильного вычисления позиции
            CacheEntityBounds();

            // Кешируем оригинальные материалы и применяем ghost-материалы только к комбинированным мешам
            CacheAndApplyGhostMaterials(false);

            // Делаем kinematic и помечаем как ghost
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

            // Восстанавливаем оригинальные материалы
            RestoreOriginalMaterials();

            // Убираем ghost-режим
            _ghostEntity.SetGhostMode(false);
            // Включаем физику после фиксации
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
                _ghostEntity.SetGhostMode(false);
                Destroy(_ghostEntity.gameObject);
            }

            _ghostEntity = null;
            _isActive = false;
            _canPlace = false;
            _originalMaterials.Clear();
            _cachedEntityMaxSize = 0f;
            _cachedEntityBounds = new Bounds();
            _hasLastSurface = false;
            _hasLastValidPosition = false;
        }

        private void UpdateGhostPosition()
        {
            if (_ghostEntity == null || _playerCamera == null)
                return;

            // Гарантируем, что ghost остается kinematic на протяжении всего ghost-режима
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

            // Проверяем минимальное расстояние от камеры (используем только _minGhostDistance без учета размера)
            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 toPosition = targetPosition - cameraPosition;
            float distanceToCamera = toPosition.magnitude;

            // Если позиция слишком близко к камере, используем предыдущую валидную позицию или ограничиваем расстояние
            if (distanceToCamera < _minGhostDistance)
            {
                if (_hasLastValidPosition)
                {
                    // Проверяем, не слишком ли близко предыдущая позиция
                    Vector3 toLastPosition = _lastValidPosition - cameraPosition;
                    float distanceToLast = toLastPosition.magnitude;

                    if (distanceToLast >= _minGhostDistance)
                    {
                        // Предыдущая позиция валидна — используем её (останавливаем движение к игроку)
                        targetPosition = _lastValidPosition;
                    }
                    else
                    {
                        // Предыдущая позиция тоже слишком близко — отодвигаем на минимальное расстояние
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
                    // Нет предыдущей позиции — отодвигаем на минимальное расстояние
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
                // Позиция валидна — сохраняем её
                _lastValidPosition = targetPosition;
                _hasLastValidPosition = true;
            }

            _ghostEntity.gameObject.SetActive(true);
            _ghostEntity.transform.position = targetPosition;

            // Поворачиваем к камере (только по оси Y)
            Vector3 toCamera = cameraPosition - targetPosition;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.000001f)
            {
                _ghostEntity.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // Должен стоять на поверхности и не пересекаться
            _canPlace = IsGrounded(targetPosition) && !IsPositionOccupied(targetPosition);
            UpdateGhostMaterial(_canPlace);
        }

        private Vector3 GetGhostPosition()
        {
            if (_playerCamera == null)
                return Vector3.zero;

            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 cameraForward = _playerCamera.transform.forward;

            // Вычисляем расстояние на основе угла наклона камеры (pitch)
            float pitchAngle = _playerCamera.transform.eulerAngles.x;
            // Нормализуем угол от 0-360 к -90 до 90
            if (pitchAngle > 180f) pitchAngle -= 360f;
            pitchAngle = -pitchAngle; // Инвертируем: вверх = отрицательный угол

            // Нормализуем угол от -90 до 90 к 0-1
            float normalizedAngle = (pitchAngle + 90f) / 180f;

            // Интерполируем расстояние: вниз (0) = min, вверх (1) = max
            // Используем _minGhostDistance напрямую для интерполяции
            float targetDistance = Mathf.Lerp(_minGhostDistance, _maxGhostDistance, normalizedAngle);
            targetDistance *= _distanceMultiplier;

            // Добавляем минимальное расстояние для больших объектов, чтобы они не перекрывали луч
            // Но это влияет только на дальность raycast, а не на минимальное расстояние от игрока
            if (_cachedEntityMaxSize > 0f)
            {
                float sizeOffset = _cachedEntityMaxSize * 0.5f;
                targetDistance = Mathf.Max(targetDistance, _minGhostDistance + sizeOffset);
            }

            Ray ray = new Ray(cameraPosition, cameraForward);

            // Ищем поверхность, исключая сам ghost entity
            RaycastHit[] hits =
                Physics.RaycastAll(ray, targetDistance, _surfaceLayerMask, QueryTriggerInteraction.Ignore);

            // Фильтруем попадания: исключаем коллайдеры самого ghost entity
            RaycastHit? validHit = null;
            float closestHitDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                // Пропускаем попадания в сам ghost entity
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

            // Луч ушёл в небо или попал только в ghost — используем последнюю валидную поверхность для плавного скольжения
            if (_hasLastSurface)
            {
                Plane plane = new Plane(_lastSurfaceNormal, _lastSurfacePoint);
                if (plane.Raycast(ray, out float planeDist))
                {
                    // Убеждаемся, что расстояние не меньше минимального
                    float d = Mathf.Clamp(planeDist, _minGhostDistance, _maxGhostDistance);
                    Vector3 onPlane = ray.GetPoint(d);
                    return GetSurfacePosition(onPlane, _lastSurfaceNormal);
                }

                // Почти параллельно плоскости — двигаемся по касательной
                Vector3 tangent = Vector3.ProjectOnPlane(cameraForward, _lastSurfaceNormal).normalized;
                Vector3 slide = _lastSurfacePoint + tangent * targetDistance;
                return GetSurfacePosition(slide, _lastSurfaceNormal);
            }

            // Нет предыдущей поверхности — ставим просто перед игроком на рассчитанном расстоянии
            return cameraPosition + cameraForward.normalized * targetDistance;
        }

        // Вычисляет позицию entity на поверхности так, чтобы нижняя точка bounds касалась поверхности
        private Vector3 GetSurfacePosition(Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            if (_ghostEntity == null)
                return surfacePoint;

            // Вычисляем центр bounds в мировых координатах относительно transform.position
            Vector3 boundsCenterWorldOffset = _ghostEntity.transform.TransformVector(_boundsCenterOffset);

            // Вычисляем направление вниз от центра bounds
            Vector3 downVector = _ghostEntity.transform.TransformDirection(Vector3.down);

            // Вычисляем offset от центра bounds до нижней точки bounds
            Vector3 bottomPointOffsetFromCenter = downVector * _entityBottomExtent;

            // Полный offset от transform.position до нижней точки bounds
            Vector3 bottomPointOffset = boundsCenterWorldOffset + bottomPointOffsetFromCenter;

            // Вычисляем, насколько нужно поднять/опустить entity, чтобы нижняя точка коснулась поверхности
            float offsetOnNormal = Vector3.Dot(bottomPointOffset, surfaceNormal);
            return surfacePoint - surfaceNormal * offsetOnNormal;
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
            // луч немного выше точки касания, вниз на небольшой диапазон
            float dist = 2;
            Vector3 origin = position + Vector3.up * 0.01f;
            return Physics.Raycast(origin, Vector3.down, dist, _surfaceLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void CacheAndApplyGhostMaterials(bool isBlocked)
        {
            if (_ghostEntity == null)
                return;

            Material targetMaterial = isBlocked ? _ghostMaterialRed : _ghostMaterialGreen;

            // Если материалов нет, создаём временные
            if (targetMaterial == null)
            {
                targetMaterial = CreateGhostMaterial(isBlocked);
            }

            _originalMaterials.Clear();
            _originalPropertyBlocks.Clear();

            for (int i = 0; i < _combinedRenderers.Count; i++)
            {
                var r = _combinedRenderers[i];
                if (r == null) continue;

                // Кешируем оригинальные материалы
                _originalMaterials[r] = new Material[r.sharedMaterials.Length];
                for (int j = 0; j < r.sharedMaterials.Length; j++)
                {
                    _originalMaterials[r][j] = r.sharedMaterials[j];
                }

                // Сохраняем исходный property block (если использовался оригинальным комбинированным мешем)
                var savedBlock = new MaterialPropertyBlock();
                r.GetPropertyBlock(savedBlock);
                _originalPropertyBlocks[r] = savedBlock;

                // Применяем ghost-материал ко всем слотам
                Material[] ghostMats = new Material[r.sharedMaterials.Length];
                for (int j = 0; j < ghostMats.Length; j++)
                {
                    ghostMats[j] = targetMaterial;
                }

                r.sharedMaterials = ghostMats;

                // Сбрасываем PropertyBlock, чтобы цвет из материала применился
                _mpb.Clear();
                r.SetPropertyBlock(_mpb);
            }
        }

        private void UpdateGhostMaterial(bool canPlace)
        {
            Material targetMaterial = canPlace ? _ghostMaterialGreen : _ghostMaterialRed;

            if (targetMaterial == null)
            {
                targetMaterial = CreateGhostMaterial(!canPlace);
            }

            for (int i = 0; i < _combinedRenderers.Count; i++)
            {
                var r = _combinedRenderers[i];
                if (r == null) continue;

                Material[] ghostMats = new Material[r.sharedMaterials.Length];
                for (int j = 0; j < ghostMats.Length; j++)
                {
                    ghostMats[j] = targetMaterial;
                }

                r.sharedMaterials = ghostMats;

                // Учитываем URP: некоторые рендеры используют MaterialPropertyBlock
                Color c = canPlace
                    ? new Color(0f, 1f, 0f, _ghostTransparency)
                    : new Color(1f, 0f, 0f, _ghostTransparency);
                _mpb.Clear();
                _mpb.SetColor("_BaseColor", c); // URP Lit/Unlit
                _mpb.SetColor("_Color", c); // Fallback для Standard
                r.SetPropertyBlock(_mpb);
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
