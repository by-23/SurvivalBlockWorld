using UnityEngine;
using System.Collections.Generic;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Спавнит сохраненные сущности с помощью ghost-превью и логики привязки (как в CubeCreator).
    /// UI кнопки вызывают SpawnSavedIndex через EntityManager.
    /// </summary>
    public class EntityCreator : GhostPlacementBase
    {
        [Header("Collision Check"), SerializeField]
        private LayerMask _collisionCheckLayerMask = ~0;

        [SerializeField] private float
            _collisionCheckTolerance = 0.01f; // Допуск при проверке пересечений (уменьшает extents для проверки)

        [Header("Snapping Targets"), SerializeField]
        private LayerMask _snapLayerMask = ~0;

        [SerializeField] private bool _includeGroundInSnap = true;

        [Header("Ghost Placement"), SerializeField]
        private float _maxGhostDistance = 8f;

        [Header("References"), SerializeField] private EntityManager _entitySaveManager;

        private GameObject _combinedMeshObject;

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

        private void OnDisable()
        {
            // При переключении инструмента удаляем ghost
            DestroyGhost();
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

            // Обновляем позицию ghost если он существует
            if (_ghostRoot != null)
            {
                UpdateGhostCubePosition();
            }
            else
            {
                Debug.LogError("Ghost root is null, cannot spawn!");
                return;
            }

            Vector3 spawnPosition = _ghostRoot.transform.position;

            if (spawnPosition == Vector3.zero)
            {
                Debug.Log("Cannot spawn entity: no valid surface found!");
                return;
            }

            // Проверяем занятость позиции - если занято, ничего не делаем (ghost остается на месте)
            if (IsPositionOccupied(spawnPosition))
            {
                Debug.Log("Cannot spawn entity: position is occupied!");
                return;
            }

            // Превращаем ghost в настоящий Entity
            ConvertGhostToEntity(index);

            // Очищаем ссылки на ghost - теперь это реальный Entity
            _ghostRoot = null;
            _combinedMeshObject = null;
            _isGhostActive = false;
        }

        /// <summary>
        /// Превращает ghost объект в настоящий Entity - восстанавливает оригинальные цвета из ColorCube
        /// </summary>
        private void ConvertGhostToEntity(int index)
        {
            if (_ghostRoot == null) return;

            Entity entity = _ghostRoot.GetComponent<Entity>();
            if (entity == null) return;

            Cube[] allCubes = entity.GetComponentsInChildren<Cube>();

            // Восстанавливаем оригинальные материалы кубов (убрав ghost прозрачность)
            // Нужно вернуть оригинальные материалы из sharedMaterial, а не использовать ghost материалы
            CubeSpawner spawner = SaveSystem.Instance?.CubeSpawner;
            if (spawner == null)
            {
                spawner = FindAnyObjectByType<CubeSpawner>();
            }

            Material originalCubeMaterial = null;
            if (spawner != null)
            {
                // Получаем оригинальный материал из префаба
                var prefabField = typeof(CubeSpawner).GetField("defaultCubePrefab",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prefabField != null)
                {
                    GameObject defaultPrefab = prefabField.GetValue(spawner) as GameObject;
                    if (defaultPrefab != null)
                    {
                        MeshRenderer prefabRenderer = defaultPrefab.GetComponent<MeshRenderer>();
                        if (prefabRenderer != null && prefabRenderer.sharedMaterial != null)
                        {
                            originalCubeMaterial = prefabRenderer.sharedMaterial;
                        }
                    }
                }
            }

            // Если не нашли материал из префаба, берем из первого куба в сцене
            if (originalCubeMaterial == null)
            {
                Cube sampleCube = FindAnyObjectByType<Cube>();
                if (sampleCube != null)
                {
                    MeshRenderer cubeRenderer = sampleCube.GetComponent<MeshRenderer>();
                    if (cubeRenderer != null && cubeRenderer.sharedMaterial != null)
                    {
                        originalCubeMaterial = cubeRenderer.sharedMaterial;
                    }
                }
            }

            // Восстанавливаем материалы кубов и коллайдеры
            for (int i = 0; i < allCubes.Length; i++)
            {
                Cube cube = allCubes[i];
                MeshRenderer mr = cube.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (originalCubeMaterial != null)
                    {
                        // Используем оригинальный материал
                        mr.sharedMaterial = originalCubeMaterial;
                    }

                    // Убираем прозрачность из материала
                    Material mat = mr.material;
                    Color matColor = mat.color;
                    matColor.a = 1f;
                    mat.color = matColor;
                    mat.SetFloat("_Mode", 0);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.EnableKeyword("_ALPHATEST_ON");
                }

                // Восстанавливаем нормальные коллайдеры (убираем trigger для ghost)
                Collider col = cube.GetComponent<Collider>();
                if (col != null)
                {
                    col.isTrigger = false; // Возвращаем нормальный коллайдер
                    col.enabled = true; // Убеждаемся что коллайдер включен
                }

                // Убираем Rigidbody у кубов, если они есть (физика должна быть только у Entity)
                Rigidbody cubeRb = cube.GetComponent<Rigidbody>();
                if (cubeRb != null)
                {
                    Destroy(cubeRb);
                }
            }

            // Пере-объединяем меши чтобы восстановить правильные цвета из ColorCube
            EntityMeshCombiner meshCombiner = entity.GetComponent<EntityMeshCombiner>();
            if (meshCombiner != null && meshCombiner.IsCombined)
            {
                // Показываем кубы временно чтобы EntityMeshCombiner мог их прочитать
                meshCombiner.ShowCubes();
                // Объединяем заново - теперь EntityMeshCombiner применит правильные цвета из ColorCube
                meshCombiner.CombineMeshes();
            }

            // Обновляем ссылку на объединенный меш
            _combinedMeshObject = entity.transform.Find("CombinedMesh")?.gameObject;

            // Переименовываем ghost в Entity
            _ghostRoot.name = $"Entity_{System.DateTime.Now.Ticks}";

            // Устанавливаем слой "Entity" для entity
            int entityLayer = LayerMask.NameToLayer("Entity");
            if (entityLayer >= 0)
            {
                _ghostRoot.layer = entityLayer;
            }

            // Устанавливаем слой "Cube" для всех дочерних кубов
            int cubeLayer = LayerMask.NameToLayer("Cube");
            if (cubeLayer >= 0)
            {
                SetCubeLayerRecursively(_ghostRoot, cubeLayer);
            }

            // Убеждаемся что Rigidbody Entity настроен правильно для взаимодействия
            Rigidbody entityRb = entity.GetComponent<Rigidbody>();
            if (entityRb != null)
            {
                // Если нужно не kinematic для взаимодействия, можно установить false
                // Но по умолчанию оставляем kinematic = true как было при создании
                // entityRb.isKinematic = false; // Раскомментировать если нужна физика
                entityRb.collisionDetectionMode =
                    CollisionDetectionMode.Continuous; // Для точного обнаружения столкновений
            }
        }

        /// <summary>
        /// Рекурсивно устанавливает слой "Cube" для всех кубов, оставляя остальные объекты (например, CombinedMesh) на слое Entity
        /// </summary>
        private void SetCubeLayerRecursively(GameObject obj, int cubeLayer)
        {
            if (obj == null) return;

            // Если это куб, устанавливаем слой Cube
            Cube cube = obj.GetComponent<Cube>();
            if (cube != null)
            {
                obj.layer = cubeLayer;
            }

            // Рекурсивно обрабатываем дочерние объекты
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                SetCubeLayerRecursively(obj.transform.GetChild(i).gameObject, cubeLayer);
            }
        }

        /// <summary>
        /// Выбирает сохраненную сущность по индексу и пересобирает ghost.
        /// </summary>
        public void SelectSavedIndex(int index)
        {
            // Если выбираем другой объект, удаляем текущий ghost
            if (_lastSelectedIndex != index && _ghostRoot != null)
            {
                DestroyGhost();
            }

            _lastSelectedIndex = index;
            _ghostManualRotation = 0f; // Сброс поворота при выборе нового объекта
            RebuildGhostForIndex(index);
            ShowGhost();
        }

        /// <summary>
        /// Удаляет ghost объект
        /// </summary>
        private void DestroyGhost()
        {
            if (_ghostRoot != null)
            {
                Destroy(_ghostRoot);
                _ghostRoot = null;
            }

            if (_combinedMeshObject != null)
            {
                Destroy(_combinedMeshObject);
                _combinedMeshObject = null;
            }

            _isGhostActive = false;
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

            // EntityMeshCombiner уже применил правильные цвета через MaterialPropertyBlock
            // Нужно только добавить ghost эффект (зеленый/красный оттенок без прозрачности)
            if (_combinedMeshObject != null)
            {
                MeshRenderer[] renderers = _combinedMeshObject.GetComponentsInChildren<MeshRenderer>();
                Color ghostTint = isOccupied ? new Color(1f, 0f, 0f) : new Color(0f, 1f, 0f);

                for (int i = 0; i < renderers.Length; i++)
                {
                    MeshRenderer meshRenderer = renderers[i];
                    if (meshRenderer == null) continue;

                    MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                    meshRenderer.GetPropertyBlock(propertyBlock);

                    // Берем оригинальный цвет из PropertyBlock (установлен EntityMeshCombiner из ColorCube)
                    Color baseColor = propertyBlock.HasColor("_BaseColor")
                        ? propertyBlock.GetColor("_BaseColor")
                        : Color.white;

                    // Применяем ghost оттенок: смешиваем оригинальный цвет с зеленым/красным (без прозрачности)
                    Color tintedColor = baseColor * ghostTint;
                    tintedColor.a = 1f; // Полная непрозрачность

                    propertyBlock.SetColor("_BaseColor", tintedColor);
                    meshRenderer.SetPropertyBlock(propertyBlock);
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
                    return hit.point;
                }
            }

            return Vector3.zero;
        }

        protected override bool IsPositionOccupied(Vector3 candidatePosition)
        {
            if (_ghostRoot == null) return false;

            // Получаем bounds объединенного меша или всех renderers
            Bounds combined;
            if (_combinedMeshObject != null)
            {
                Renderer combinedRenderer = _combinedMeshObject.GetComponent<Renderer>();
                if (combinedRenderer != null)
                {
                    combined = combinedRenderer.bounds;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                var renderers = _ghostRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                    return false;

                combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    combined.Encapsulate(renderers[i].bounds);
                }
            }

            Vector3 currentCenter = combined.center;
            Vector3 centerOffset = currentCenter - _ghostRoot.transform.position;
            Vector3 halfExtents = combined.extents;

            // Уменьшаем extents на допуск, чтобы избежать ложных срабатываний когда ghost стоит на объекте
            halfExtents.x = Mathf.Max(0f, halfExtents.x - _collisionCheckTolerance);
            halfExtents.y = Mathf.Max(0f, halfExtents.y - _collisionCheckTolerance);
            halfExtents.z = Mathf.Max(0f, halfExtents.z - _collisionCheckTolerance);

            Vector3 testCenter = candidatePosition + centerOffset;
            Collider[] hits = Physics.OverlapBox(testCenter, halfExtents, _ghostRoot.transform.rotation,
                _collisionCheckLayerMask, QueryTriggerInteraction.Ignore);
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
            float offset = 0.5f;
            Vector3[] offsets =
            {
                Vector3.zero,
                new Vector3(offset, 0f, 0f),
                new Vector3(-offset, 0f, 0f),
                new Vector3(0f, 0f, offset),
                new Vector3(0f, 0f, -offset),
                new Vector3(offset, 0f, offset),
                new Vector3(-offset, 0f, offset),
                new Vector3(offset, 0f, -offset),
                new Vector3(-offset, 0f, -offset)
            };

            float bestDist = float.MaxValue;
            Vector3 best = occupiedPosition;
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 candidatePos = occupiedPosition + offsets[i];
                candidatePos = AdjustGhostPositionToGround(candidatePos);
                if (!IsPositionOccupied(candidatePos))
                {
                    float d = Vector3.Distance(playerPosition, candidatePos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = candidatePos;
                    }
                }
            }

            return best;
        }

        private void RebuildGhostForIndex(int index)
        {
            // Удаляем старый ghost и объединенный меш
            if (_ghostRoot != null)
            {
                Destroy(_ghostRoot);
                _ghostRoot = null;
            }

            if (_combinedMeshObject != null)
            {
                Destroy(_combinedMeshObject);
                _combinedMeshObject = null;
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

            // Используем существующую логику создания Entity из EntityManager
            _ghostRoot = EntityFactory.CreateEntity(
                Vector3.zero,
                Quaternion.identity,
                save.scale,
                isKinematic: true,
                entityName: "GhostEntitySpawn"
            ).gameObject;
            _ghostRoot.SetActive(false);

            Entity ghostEntity = _ghostRoot.GetComponent<Entity>();
            if (ghostEntity == null)
            {
                Debug.LogError("Failed to create ghost Entity!");
                Destroy(_ghostRoot);
                _ghostRoot = null;
                return;
            }

            // Отключаем автоматический StartSetup чтобы контролировать когда объединять меши
            ghostEntity._StartCheck = false;

            // Загружаем кубы используя Entity.LoadFromDataAsync (как в EntityManager)
            StartCoroutine(LoadGhostEntityAsync(ghostEntity, save, Quaternion.identity));
        }

        /// <summary>
        /// Загружает кубы в ghost Entity используя логику из EntityManager
        /// </summary>
        private System.Collections.IEnumerator LoadGhostEntityAsync(Entity entity, EntitySaveData saveData,
            Quaternion rotation)
        {
            if (saveData.cubesData == null || saveData.cubesData.Length == 0)
            {
                yield break;
            }

            CubeSpawner spawner = SaveSystem.Instance?.CubeSpawner;
            if (spawner == null)
            {
                spawner = FindAnyObjectByType<CubeSpawner>();
            }

            if (spawner == null)
            {
                Debug.LogError("CubeSpawner not found!");
                yield break;
            }

            // Используем ту же логику что и EntityManager.LoadEntityAsync
            float newYRotation = rotation.eulerAngles.y;
            float originalYRotation = saveData.rotation.eulerAngles.y;
            float yRotationDelta = newYRotation - originalYRotation;
            Quaternion yRotationDeltaQuat = Quaternion.Euler(0f, yRotationDelta, 0f);

            Vector3 originalEntityCenter = saveData.position;
            Vector3 originalEntityScale = saveData.scale;
            Vector3 newEntityPosition = entity.transform.position;
            float newEntityScale = entity.transform.localScale.x;

            float finalYRotation = rotation.eulerAngles.y;
            Quaternion finalRotationQuat = Quaternion.Euler(0f, finalYRotation, 0f);

            entity.transform.rotation = finalRotationQuat;

            CubeData[] adjustedCubeData = new CubeData[saveData.cubesData.Length];
            for (int i = 0; i < saveData.cubesData.Length; i++)
            {
                CubeData original = saveData.cubesData[i];
                Vector3 originalCubePos = original.Position;
                Vector3 relativeToEntity = originalCubePos - originalEntityCenter;
                Vector3 rotatedRelative = yRotationDeltaQuat * relativeToEntity;
                Vector3 scaledRelative = rotatedRelative * (newEntityScale / originalEntityScale.x);
                Vector3 newWorldPos = newEntityPosition + scaledRelative;

                Quaternion originalCubeRot = original.Rotation;
                Quaternion adjustedCubeRotation = finalRotationQuat *
                                                  (Quaternion.Inverse(Quaternion.Euler(0f, originalYRotation, 0f)) *
                                                   originalCubeRot);
                adjustedCubeRotation = Quaternion.Euler(
                    adjustedCubeRotation.eulerAngles.x,
                    adjustedCubeRotation.eulerAngles.y,
                    originalCubeRot.eulerAngles.z
                );

                adjustedCubeData[i] = new CubeData(
                    newWorldPos,
                    original.Color,
                    original.blockTypeId,
                    original.entityId,
                    adjustedCubeRotation
                );
            }

            // Загружаем кубы через Entity.LoadFromDataAsync (создает настоящие Cube объекты)
            yield return entity.LoadFromDataAsync(adjustedCubeData, spawner, deferredSetup: true);

            // Применяем ghost материалы к кубам перед объединением
            ApplyGhostMaterialsToCubes(entity);

            // Используем EntityMeshCombiner для объединения мешей (как в обычных Entity)
            // EntityMeshCombiner правильно применит цвета через MaterialPropertyBlock
            EntityMeshCombiner meshCombiner = entity.GetComponent<EntityMeshCombiner>();
            if (meshCombiner != null)
            {
                meshCombiner.CombineMeshes();
            }

            // Сохраняем ссылку на объединенный меш для изменения цвета
            _combinedMeshObject = entity.transform.Find("CombinedMesh")?.gameObject;

            // Применяем зеленый цвет по умолчанию
            UpdateGhostMaterial(false);
        }

        /// <summary>
        /// Применяет ghost материалы к кубам перед объединением
        /// EntityMeshCombiner будет использовать эти материалы и правильно применит цвета из ColorCube
        /// </summary>
        private void ApplyGhostMaterialsToCubes(Entity entity)
        {
            Cube[] cubes = entity.GetComponentsInChildren<Cube>();

            for (int i = 0; i < cubes.Length; i++)
            {
                GameObject cubeObj = cubes[i].gameObject;
                MeshRenderer mr = cubeObj.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    // Используем существующий материал из куба без прозрачности
                    Material ghostMat = new Material(mr.sharedMaterial);
                    ghostMat.color = new Color(ghostMat.color.r, ghostMat.color.g, ghostMat.color.b, 1f);

                    // Материал остается непрозрачным (режим по умолчанию)
                    ghostMat.SetFloat("_Mode", 0);
                    ghostMat.SetInt("_ZWrite", 1);
                    ghostMat.DisableKeyword("_ALPHABLEND_ON");
                    ghostMat.EnableKeyword("_ALPHATEST_ON");
                    ghostMat.renderQueue = -1; // Дефолтный render queue

                    mr.material = ghostMat;
                }

                SetupGhostCollider(cubeObj);
            }
        }

        /// <summary>
        /// Корректирует позицию ghost так, чтобы нижняя часть объекта была впритык к поверхности снизу
        /// Использует ту же логику что и EntityManager.AdjustPositionToGround
        /// </summary>
        private Vector3 AdjustGhostPositionToGround(Vector3 ghostPosition)
        {
            if (_ghostRoot == null || _entitySaveManager == null) return ghostPosition;

            // Получаем данные сохранения для текущего выбранного объекта
            EntitySaveData saveData = null;
            if (_lastSelectedIndex >= 0)
            {
                bool success = _entitySaveManager.TryGetSavedEntityData(_lastSelectedIndex, out saveData);
                if (!success || saveData == null) return ghostPosition;
            }
            else
            {
                return ghostPosition;
            }

            if (saveData.cubesData == null || saveData.cubesData.Length == 0)
            {
                return ghostPosition;
            }

            // Размер куба (стандартный размер в игре)
            const float cubeSize = 1f;
            const float cubeHalfSize = cubeSize * 0.5f;

            // Получаем текущий поворот и масштаб ghost
            Quaternion currentRotation = _ghostRoot.transform.rotation;
            float currentScale = _ghostRoot.transform.localScale.x;

            // Вычисляем нижнюю границу объекта из исходных данных (как в EntityManager)
            // Находим минимальную Y координату нижней границы всех кубов
            float minBottomWorldY = float.MaxValue;

            for (int i = 0; i < saveData.cubesData.Length; i++)
            {
                var cubeData = saveData.cubesData[i];
                // Позиция куба в мировых координатах оригинального объекта
                Vector3 cubeWorldPos = cubeData.Position;

                // Вычисляем локальную позицию относительно центра Entity
                Vector3 localPos = (cubeWorldPos - saveData.position) / Mathf.Max(0.0001f, saveData.scale.x);

                // Применяем текущий поворот и масштаб
                Vector3 rotatedLocalPos = currentRotation * localPos;
                Vector3 scaledLocalPos = rotatedLocalPos * currentScale;

                // Мировая позиция куба с учетом текущего положения ghost
                Vector3 worldCubePos = ghostPosition + scaledLocalPos;

                // Нижняя граница куба (куб размером 1, центр в позиции куба, нижняя точка на 0.5 ниже)
                float cubeBottomY = worldCubePos.y - cubeHalfSize;

                if (cubeBottomY < minBottomWorldY)
                {
                    minBottomWorldY = cubeBottomY;
                }
            }

            if (minBottomWorldY >= float.MaxValue - 1f)
            {
                return ghostPosition;
            }

            // Выполняем Raycast вниз от нижней точки для поиска поверхности под ghost
            Vector3 rayStart = new Vector3(ghostPosition.x, minBottomWorldY + 2f, ghostPosition.z);

            // Используем layer mask для исключения самого ghost
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 100f, _snapLayerMask,
                QueryTriggerInteraction.Ignore);

            // Ищем самую верхнюю поверхность, исключая сам ghost
            // Это поверхность с максимальной Y координатой, на которую должен встать ghost
            float highestSurfaceY = float.MinValue;
            bool foundSurface = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                // Исключаем сам ghost из проверки
                if (_ghostRoot != null && (hit.collider.transform.root == _ghostRoot.transform ||
                                           hit.collider.transform.IsChildOf(_ghostRoot.transform)))
                {
                    continue;
                }

                float surfaceY = hit.point.y;
                // Ищем самую верхнюю поверхность (максимальную Y)
                if (surfaceY > highestSurfaceY)
                {
                    highestSurfaceY = surfaceY;
                    foundSurface = true;
                }
            }

            if (foundSurface)
            {
                // Корректируем позицию так, чтобы нижняя точка была точно на верхней поверхности
                float heightAdjustment = highestSurfaceY - minBottomWorldY;
                Vector3 correctedPosition = ghostPosition;
                correctedPosition.y += heightAdjustment;
                return correctedPosition;
            }

            // Если не нашли поверхность, возвращаем исходную позицию
            return ghostPosition;
        }
    }
}




