using UnityEngine;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Спавнит сохраненные сущности с помощью ghost-превью и логики привязки (как в CubeCreator).
    /// UI кнопки вызывают SpawnSavedIndex через EntityManager.
    /// </summary>
    public class EntityCreator : GhostPlacementBase
    {
        [Header("Snapping Targets"), SerializeField]
        private LayerMask _snapLayerMask = ~0;

        [SerializeField] private bool _includeGroundInSnap = true;

        [Header("Surface Offset"), SerializeField]
        private float _surfaceOffset = 0.5f;

        [Header("Ghost Placement"), SerializeField]
        private float _maxGhostDistance = 8f;

        [SerializeField] private float _groundOffset = 0.1f; // Отступ от земли для ghost, чтобы избежать коллизий

        [Header("References"), SerializeField] private EntityManager _entitySaveManager;

        private int _lastSelectedIndex = -1;
        private float _ghostManualRotation = 0f; // Дополнительный поворот ghost в градусах (по оси Y)

        private void Awake()
        {
            if (_entitySaveManager == null)
            {
                _entitySaveManager = FindAnyObjectByType<EntityManager>();
            }
        }

        private void Start()
        {
            // Ghost будет создан при выборе объекта
            HideGhost();
        }

        private void Update()
        {
            if (_isGhostActive && _ghostRoot != null)
            {
                UpdateGhostCubePosition();
            }

            if (_lastSelectedIndex >= 0 && Input.GetKeyDown(KeyCode.F))
            {
                SpawnSavedIndex(_lastSelectedIndex);
            }

            // Поворот ghost на 90 градусов по часовой стрелке при нажатии R
            if (_isGhostActive && _ghostRoot != null && Input.GetKeyDown(KeyCode.R))
            {
                _ghostManualRotation += 90f;
                UpdateGhostCubePosition();
            }
        }

        public void SpawnSavedIndex(int index)
        {
            _lastSelectedIndex = index;

            if (_entitySaveManager == null)
            {
                Debug.LogError("EntitySaveManager reference missing in EntityCreator.");
                return;
            }

            // Пересобираем ghost для правильного индекса
            RebuildGhostForIndex(index);
            // Обновляем позицию и поворот ghost перед чтением
            UpdateGhostCubePosition();

            Vector3 spawnPosition = _ghostRoot != null ? _ghostRoot.transform.position : GetGhostPosition();

            if (spawnPosition == Vector3.zero)
            {
                Debug.Log("Cannot spawn entity: no valid surface found!");
                return;
            }

            if (IsPositionOccupied(spawnPosition))
            {
                spawnPosition = FindAlternativePosition(spawnPosition);

                if (IsPositionOccupied(spawnPosition))
                {
                    Debug.Log("Cannot spawn entity: all nearby positions are occupied!");
                    return;
                }
            }

            // Используем поворот ghost для спавна (только по оси Y)
            Quaternion spawnRotation = Quaternion.identity;
            if (_ghostRoot != null)
            {
                float yRotation = _ghostRoot.transform.rotation.eulerAngles.y;
                spawnRotation = Quaternion.Euler(0f, yRotation, 0f);
            }

            _entitySaveManager.SpawnSavedEntityAt(index, spawnPosition, spawnRotation);

            // Удаляем ghost после создания объекта
            if (_ghostRoot != null)
            {
                Destroy(_ghostRoot);
                _ghostRoot = null;
            }

            _isGhostActive = false;
        }

        /// <summary>
        /// Выбирает сохраненную сущность по индексу и пересобирает ghost.
        /// </summary>
        public void SelectSavedIndex(int index)
        {
            _lastSelectedIndex = index;
            _ghostManualRotation = 0f; // Сброс поворота при выборе нового объекта
            RebuildGhostForIndex(index);
            ShowGhost();
        }

        /// <summary>
        /// Позволяет UI настроить, к каким слоям привязывается ghost и включать ли землю.
        /// </summary>
        public void SetSnapTargets(LayerMask snapLayers, bool includeGround)
        {
            _snapLayerMask = snapLayers;
            _includeGroundInSnap = includeGround;
        }


        protected override void UpdateGhostMaterial(bool isOccupied)
        {
            if (_ghostRoot == null) return;
            Color ghostColor = isOccupied
                ? new Color(1f, 0f, 0f, _ghostTransparency)
                : new Color(0f, 1f, 0f, _ghostTransparency);
            var renderers = _ghostRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.material != null)
                {
                    r.material.color = ghostColor;
                }
            }
        }

        private void UpdateGhostCubePosition()
        {
            if (_ghostRoot == null) return;

            Vector3 targetPosition = GetGhostPosition();

            if (IsPositionOccupied(targetPosition))
            {
                targetPosition = FindAlternativePosition(targetPosition);
            }

            // Автоматически корректируем позицию так, чтобы ghost стоял на земле
            targetPosition = AdjustGhostPositionToGround(targetPosition);

            _ghostRoot.transform.position = targetPosition;

            // Поворачиваем ghost к камере (только по оси Y)
            Camera playerCamera = GetPlayerCamera();

            Quaternion baseRotation = Quaternion.identity;
            if (playerCamera != null)
            {
                Vector3 toCamera = playerCamera.transform.position - targetPosition;
                toCamera.y = 0f; // Убираем наклон по вертикали
                if (toCamera.sqrMagnitude > 0.000001f)
                {
                    baseRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }

            // Применяем базовый поворот и дополнительный поворот от кнопки R
            _ghostRoot.transform.rotation = baseRotation * Quaternion.Euler(0f, _ghostManualRotation, 0f);

            bool isOccupied = IsPositionOccupied(targetPosition);
            UpdateGhostMaterial(isOccupied);
        }


        protected override Vector3 EnsureAboveGround(Vector3 position, float offset = 0.5f)
        {
            return base.EnsureAboveGround(position, _surfaceOffset);
        }

        protected override Vector3 GetGhostPosition()
        {
            Camera playerCamera = GetPlayerCamera();

            if (playerCamera != null)
            {
                Vector3 cameraPosition = playerCamera.transform.position;
                Vector3 cameraForward = playerCamera.transform.forward;
                Ray ray = new Ray(cameraPosition, cameraForward);

                if (Physics.Raycast(ray, out RaycastHit hit, 50f, _snapLayerMask, QueryTriggerInteraction.Ignore))
                {
                    if (_includeGroundInSnap && (((1 << hit.collider.gameObject.layer) & _groundLayerMask) != 0))
                    {
                        Vector3 surfacePosition = hit.point;
                        surfacePosition.y = hit.point.y + _surfaceOffset;
                        return surfacePosition;
                    }

                    return hit.point + hit.normal * _surfaceOffset;
                }
                else
                {
                    // Нет попадания (например, смотрим в небо). Размещаем ghost перед игроком
                    // на максимальном расстоянии
                    Vector3 forwardPosition =
                        cameraPosition + cameraForward.normalized * Mathf.Max(0.0f, _maxGhostDistance);
                    return EnsureAboveGround(forwardPosition);
                }
            }

            return Vector3.zero;
        }

        protected override bool IsPositionOccupied(Vector3 candidatePosition)
        {
            if (_ghostRoot == null) return false;

            var renderers = _ghostRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

            Vector3 currentCenter = combined.center;
            Vector3 centerOffset = currentCenter - _ghostRoot.transform.position;
            Vector3 halfExtents = combined.extents;

            Vector3 testCenter = candidatePosition + centerOffset;
            Collider[] hits = Physics.OverlapBox(testCenter, halfExtents, _ghostRoot.transform.rotation, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                if (_ghostRoot != null && c.transform.root == _ghostRoot.transform) continue;
                return true;
            }

            return false;
        }

        protected override Vector3 FindAlternativePosition(Vector3 occupiedPosition)
        {
            Camera playerCamera = GetPlayerCamera();

            if (playerCamera == null)
            {
                return occupiedPosition;
            }

            Vector3 playerPosition = playerCamera.transform.position;

            // Пробуем небольшой набор смещений вокруг занятой позиции на плоскости XZ
            Vector3[] offsets =
            {
                Vector3.zero,
                new Vector3(_surfaceOffset, 0f, 0f),
                new Vector3(-_surfaceOffset, 0f, 0f),
                new Vector3(0f, 0f, _surfaceOffset),
                new Vector3(0f, 0f, -_surfaceOffset),
                new Vector3(_surfaceOffset, 0f, _surfaceOffset),
                new Vector3(-_surfaceOffset, 0f, _surfaceOffset),
                new Vector3(_surfaceOffset, 0f, -_surfaceOffset),
                new Vector3(-_surfaceOffset, 0f, -_surfaceOffset)
            };

            float bestDist = float.MaxValue;
            Vector3 best = occupiedPosition;
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 candidate = EnsureAboveGround(occupiedPosition + offsets[i]);
                if (!IsPositionOccupied(candidate))
                {
                    float d = Vector3.Distance(playerPosition, candidate);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = candidate;
                    }
                }
            }

            return best;
        }

        private void RebuildGhostForIndex(int index)
        {
            // Удаляем старый ghost
            if (_ghostRoot != null)
            {
                Destroy(_ghostRoot);
                _ghostRoot = null;
            }

            // Получаем данные сохранения через менеджер
            var savedCount = _entitySaveManager != null ? _entitySaveManager.GetSavedEntityCount() : 0;
            if (_entitySaveManager == null || index < 0 || index >= savedCount)
            {
                Debug.LogWarning($"Cannot rebuild ghost: invalid index {index} (total: {savedCount})");
                return;
            }

            // Получаем данные сохранения через публичный метод менеджера
            EntitySaveData save = null;
            if (_entitySaveManager != null)
            {
                bool success = _entitySaveManager.TryGetSavedEntityData(index, out save);
                if (!success || save == null)
                {
                    Debug.LogError($"Failed to get save data for index {index}");
                    return;
                }
            }

            _ghostRoot = new GameObject("GhostEntitySpawn");
            _ghostRoot.SetActive(false);

            if (save != null && save.cubesData != null && save.cubesData.Length > 0)
            {
                // Создаем дочерние кубы, повторяющие форму сущности
                for (int i = 0; i < save.cubesData.Length; i++)
                {
                    var cd = save.cubesData[i];
                    var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    child.transform.SetParent(_ghostRoot.transform, false);
                    // Локальная позиция = мировая позиция относительно центра сущности
                    Vector3 localPos = (cd.Position - save.position) / Mathf.Max(0.0001f, save.scale.x);
                    child.transform.localPosition = localPos;
                    child.transform.localRotation = cd.Rotation;

                    SetupGhostCollider(child);

                    var mr = child.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        Material mat = CreateGhostMaterial(Color.green);
                        mr.material = mat;
                    }
                }
            }
            else
            {
                // Запасной вариант: один куб
                var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                child.transform.SetParent(_ghostRoot.transform, false);
                SetupGhostCollider(child);
                var mr = child.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Material mat = CreateGhostMaterial(Color.green);
                    mr.material = mat;
                }
            }

            // Применяем зеленый цвет по умолчанию
            UpdateGhostMaterial(false);
        }

        /// <summary>
        /// Корректирует позицию ghost так, чтобы нижняя часть объекта находилась на уровне земли
        /// </summary>
        private Vector3 AdjustGhostPositionToGround(Vector3 ghostPosition)
        {
            if (_ghostRoot == null) return ghostPosition;

            // Временно устанавливаем позицию ghost для вычисления bounds
            Vector3 originalPosition = _ghostRoot.transform.position;
            _ghostRoot.transform.position = ghostPosition;

            // Получаем bounds всех renderers в ghost
            var renderers = _ghostRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                _ghostRoot.transform.position = originalPosition;
                return ghostPosition;
            }

            // Вычисляем общие bounds ghost объекта в новой позиции
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }

            // Нижняя точка bounds
            float bottomY = combinedBounds.min.y;

            // Восстанавливаем исходную позицию
            _ghostRoot.transform.position = originalPosition;

            // Выполняем Raycast вниз от нижней точки для поиска земли
            Vector3 rayStart = ghostPosition;
            rayStart.y = bottomY + 2f; // Начинаем raycast немного выше нижней точки

            RaycastHit hit;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f, _groundLayerMask))
            {
                // Нашли землю - корректируем позицию так, чтобы нижняя точка была чуть выше земли
                float groundLevel = hit.point.y;
                float heightAdjustment = groundLevel - bottomY + _groundOffset; // Добавляем отступ от земли

                // Корректируем позицию ghost
                Vector3 correctedPosition = ghostPosition;
                correctedPosition.y += heightAdjustment;

                return correctedPosition;
            }

            // Если не нашли землю, возвращаем исходную позицию
            return ghostPosition;
        }
    }
}




