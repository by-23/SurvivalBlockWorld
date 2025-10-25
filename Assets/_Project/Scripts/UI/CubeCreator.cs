using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Assets._Project.Scripts.UI
{
    public class CubeCreator : MonoBehaviour
    {
        [Header("UI References")] [SerializeField]
        private Button createButton;

        [SerializeField] private Transform colorButtonsParent;

        [Header("Spawn Settings")] [SerializeField]
        private LayerMask _groundLayerMask = 1;

        [SerializeField] private float _ghostTransparency = 0.5f;
        [SerializeField] private float _cubeSize = 1f;
        [SerializeField] private float _magneticDistance = 0.3f;
        [SerializeField] private bool _allowVerticalSnapping = true;
        [SerializeField] private float _spherecastRadius = 0.6f;
        [SerializeField] private float _spherecastDistance = 1.2f;

        [Header("Cube Prefab")] [SerializeField]
        private GameObject _cubePrefab;

        [Header("Ghost Cube")] [SerializeField]
        private GameObject _ghostCubePrefab;

        [SerializeField] private Material _ghostMaterial;

        private Color _selectedColor = Color.white;
        private Button _selectedColorButton;
        private GameObject _ghostCube;
        private bool _isGhostActive = false;

        private void Start()
        {
            if (createButton != null)
            {
                createButton.onClick.AddListener(OnCreateButtonClicked);
            }

            SetupColorButtons();
            CreateGhostCube();
        }

        private void Update()
        {
            if (_isGhostActive && _ghostCube != null)
            {
                UpdateGhostCubePosition();
            }

            // Проверяем нажатие клавиши F для создания куба
            if (Input.GetKeyDown(KeyCode.F))
            {
                OnCreateButtonClicked();
            }
        }

        private void SetupColorButtons()
        {
            if (colorButtonsParent == null) return;

            Button[] colorButtons = colorButtonsParent.GetComponentsInChildren<Button>();

            foreach (Button button in colorButtons)
            {
                button.onClick.AddListener(() => OnColorButtonClicked(button));
            }

            if (colorButtons.Length > 0)
            {
                OnColorButtonClicked(colorButtons[0]);
            }
        }

        private void OnColorButtonClicked(Button clickedButton)
        {
            if (_selectedColorButton != null)
            {
                _selectedColorButton.interactable = true;
            }

            _selectedColorButton = clickedButton;
            _selectedColorButton.interactable = false;

            Image buttonImage = clickedButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                // Берем normalColor из Button, а не текущий цвет Image
                ColorBlock colorBlock = clickedButton.colors;
                _selectedColor = colorBlock.normalColor;
            }

            ShowGhostCube();
        }

        private void OnCreateButtonClicked()
        {
            CreateCube();
            // Убираем HideGhostCube() - призрак должен оставаться
        }

        private void CreateGhostCube()
        {
            if (_ghostCubePrefab != null)
            {
                _ghostCube = Instantiate(_ghostCubePrefab);
            }
            else
            {
                _ghostCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }

            _ghostCube.name = "GhostCube";
            _ghostCube.SetActive(false);

            SetupGhostCubeMaterial();
        }

        private void SetupGhostCubeMaterial()
        {
            if (_ghostCube == null) return;

            MeshRenderer cubeRenderer = _ghostCube.GetComponent<MeshRenderer>();
            if (cubeRenderer != null)
            {
                if (_ghostMaterial != null)
                {
                    cubeRenderer.material = _ghostMaterial;
                }
                else
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = new Color(_selectedColor.r, _selectedColor.g, _selectedColor.b, _ghostTransparency);
                    mat.SetFloat("_Mode", 3); // Transparent mode
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    cubeRenderer.material = mat;
                }
            }

            Collider cubeCollider = _ghostCube.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                cubeCollider.isTrigger = true;
            }
        }


        public void HideGhostCube()
        {
            if (_ghostCube != null)
            {
                _isGhostActive = false;
                _ghostCube.SetActive(false);
            }
        }

        public void ShowGhostCube()
        {
            if (_ghostCube != null)
            {
                _isGhostActive = true;
                _ghostCube.SetActive(true);
                UpdateGhostCubeMaterial();
            }
        }

        private void UpdateGhostCubeMaterial(bool isOccupied = false)
        {
            if (_ghostCube == null) return;

            MeshRenderer cubeRenderer = _ghostCube.GetComponent<MeshRenderer>();
            if (cubeRenderer != null && cubeRenderer.material != null)
            {
                Color ghostColor;
                if (isOccupied)
                {
                    // Красный цвет когда позиция занята
                    ghostColor = new Color(1f, 0f, 0f, _ghostTransparency);
                }
                else
                {
                    // Обычный цвет выбранной кнопки
                    ghostColor = new Color(_selectedColor.r, _selectedColor.g, _selectedColor.b, _ghostTransparency);
                }

                cubeRenderer.material.color = ghostColor;
            }
        }

        private void UpdateGhostCubePosition()
        {
            if (_ghostCube == null) return;

            Vector3 targetPosition = GetGhostCubePosition();

            // Применяем магнитное прилипание к существующим кубам
            Vector3 magnetizedPosition = ApplyMagneticSnapping(targetPosition);

            // Если позиция занята, ищем альтернативную позицию
            if (IsPositionOccupied(magnetizedPosition))
            {
                magnetizedPosition = FindAlternativePositionNearPlayer(magnetizedPosition);
            }

            _ghostCube.transform.position = magnetizedPosition;

            // Проверяем, занята ли финальная позиция и меняем цвет призрака
            bool isOccupied = IsPositionOccupied(magnetizedPosition);
            UpdateGhostCubeMaterial(isOccupied);
        }

        private Vector3 ApplyMagneticSnapping(Vector3 position)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                return position;
            }

            // Создаем луч из позиции камеры в направлении взгляда
            Vector3 lookDirection = playerCamera.transform.forward;
            Ray ray = new Ray(playerCamera.transform.position, lookDirection);

            // Используем raycast для поиска куба, на который попадает луч взгляда
            RaycastHit[] hits = Physics.RaycastAll(ray, _magneticDistance * 2f);

            // Сортируем результаты по расстоянию (ближайшие первыми)
            System.Array.Sort(hits, (hit1, hit2) => hit1.distance.CompareTo(hit2.distance));

            Vector3 snappedPosition = position;

            // Ищем первый подходящий куб с учетом направления взгляда
            foreach (RaycastHit hit in hits)
            {
                // Исключаем сам призрак
                if (hit.collider.gameObject == _ghostCube)
                    continue;

                Cube cube = hit.collider.GetComponent<Cube>();
                if (cube != null)
                {
                    Vector3 cubePosition = hit.collider.transform.position;

                    // Проверяем, что куб находится в направлении взгляда
                    Vector3 directionToCube = (cubePosition - playerCamera.transform.position).normalized;
                    float dotProduct = Vector3.Dot(lookDirection.normalized, directionToCube);

                    // Если куб находится в направлении взгляда (dot product > 0.5)
                    if (dotProduct > 0.5f)
                    {
                        // Нашли подходящий куб - используем его
                        snappedPosition = SnapToNearestSide(position, cubePosition);
                        break; // Используем только первый найденный куб
                    }
                }
            }

            return snappedPosition;
        }

        private Vector3 SnapToNearestSide(Vector3 ghostPosition, Vector3 cubePosition)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                return cubePosition;
            }

            // Используем направление взгляда игрока для определения стороны куба
            Vector3 cameraForward = playerCamera.transform.forward;
            Vector3 snappedPosition = SnapToFacingSide(cameraForward, cubePosition);

            return snappedPosition;
        }

        private Vector3 SnapToNearestSideFromPlayer(Vector3 playerPosition, Vector3 cubePosition)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                return cubePosition;
            }

            // Используем направление взгляда игрока для определения стороны куба
            Vector3 cameraForward = playerCamera.transform.forward;
            Vector3 snappedPosition = SnapToFacingSide(cameraForward, cubePosition);

            return snappedPosition;
        }

        private Vector3 SnapToFacingSide(Vector3 lookDirection, Vector3 cubePosition)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                return cubePosition;
            }

            // Создаем луч из позиции камеры в направлении взгляда
            Ray ray = new Ray(playerCamera.transform.position, lookDirection);

            // Вычисляем пересечение луча с кубом
            Bounds cubeBounds = new Bounds(cubePosition, Vector3.one * _cubeSize);

            if (cubeBounds.IntersectRay(ray, out float distance))
            {
                // Получаем точку пересечения луча с кубом
                Vector3 intersectionPoint = ray.origin + ray.direction * distance;

                // Определяем, какая грань куба пересекается первой
                Vector3 faceNormal = GetIntersectedFaceNormal(intersectionPoint, cubePosition);

                // Позиционируем призрак на той же стороне, на которую смотрит игрок
                Vector3 snappedPosition = cubePosition + faceNormal * _cubeSize;

                // Проверяем, что куб не пересекается с землей (только для боковых сторон)
                // И только если куб находится близко к земле
                if (Mathf.Abs(faceNormal.y) < 0.9f &&
                    snappedPosition.y < _cubeSize * 2f) // Не верхняя/нижняя грань и не высоко
                {
                    snappedPosition = EnsureAboveGround(snappedPosition);
                }

                return snappedPosition;
            }

            // Если луч не пересекает куб, используем направление взгляда как fallback
            Vector3 normalizedLookDirection = lookDirection.normalized;

            // Определяем доминирующую ось в направлении взгляда
            float absX = Mathf.Abs(normalizedLookDirection.x);
            float absY = Mathf.Abs(normalizedLookDirection.y);
            float absZ = Mathf.Abs(normalizedLookDirection.z);

            Vector3 fallbackPosition = cubePosition;

            // Прилипаем к стороне, на которую смотрит игрок
            if (absX >= absY && absX >= absZ)
            {
                // Игрок смотрит по X оси - левая или правая сторона
                if (normalizedLookDirection.x > 0)
                {
                    // Игрок смотрит вправо -> призрак справа от куба
                    fallbackPosition = cubePosition + Vector3.right * _cubeSize;
                }
                else
                {
                    // Игрок смотрит влево -> призрак слева от куба
                    fallbackPosition = cubePosition + Vector3.left * _cubeSize;
                }
            }
            else if (_allowVerticalSnapping && absY >= absX && absY >= absZ)
            {
                // Игрок смотрит по Y оси - верхняя или нижняя сторона
                if (normalizedLookDirection.y > 0)
                {
                    // Игрок смотрит вверх -> призрак выше куба
                    fallbackPosition = cubePosition + Vector3.up * _cubeSize;
                }
                else
                {
                    // Игрок смотрит вниз -> призрак ниже куба
                    fallbackPosition = cubePosition + Vector3.down * _cubeSize;
                }
            }
            else
            {
                // Игрок смотрит по Z оси - передняя или задняя сторона
                if (normalizedLookDirection.z > 0)
                {
                    // Игрок смотрит вперед -> призрак впереди куба
                    fallbackPosition = cubePosition + Vector3.forward * _cubeSize;
                }
                else
                {
                    // Игрок смотрит назад -> призрак сзади куба
                    fallbackPosition = cubePosition + Vector3.back * _cubeSize;
                }
            }

            // Проверяем, что куб не пересекается с землей (только для боковых сторон)
            // И только если куб находится близко к земле
            if ((absX >= absY && absX >= absZ || absZ >= absX && absZ >= absY) && fallbackPosition.y < _cubeSize * 2f)
            {
                fallbackPosition = EnsureAboveGround(fallbackPosition);
            }

            return fallbackPosition;
        }

        private Vector3 GetIntersectedFaceNormal(Vector3 intersectionPoint, Vector3 cubePosition)
        {
            // Вычисляем относительную позицию точки пересечения от центра куба
            Vector3 relativePoint = intersectionPoint - cubePosition;

            // Определяем, на какой грани находится точка пересечения
            float halfSize = _cubeSize * 0.5f;

            // Вычисляем расстояния до каждой грани
            float[] distances =
            {
                Mathf.Abs(relativePoint.x - halfSize), // Правая грань
                Mathf.Abs(relativePoint.x + halfSize), // Левая грань
                Mathf.Abs(relativePoint.y - halfSize), // Верхняя грань
                Mathf.Abs(relativePoint.y + halfSize), // Нижняя грань
                Mathf.Abs(relativePoint.z - halfSize), // Передняя грань
                Mathf.Abs(relativePoint.z + halfSize) // Задняя грань
            };

            // Находим ближайшую грань (ту, которую пересекает луч)
            int closestFace = 0;
            float minDistance = distances[0];

            for (int i = 1; i < 6; i++)
            {
                if (distances[i] < minDistance)
                {
                    minDistance = distances[i];
                    closestFace = i;
                }
            }

            // Возвращаем нормаль ближайшей грани
            Vector3[] faceNormals =
            {
                Vector3.right, // Правая грань
                Vector3.left, // Левая грань
                Vector3.up, // Верхняя грань
                Vector3.down, // Нижняя грань
                Vector3.forward, // Передняя грань
                Vector3.back // Задняя грань
            };

            return faceNormals[closestFace];
        }

        private Vector3 EnsureAboveGround(Vector3 position)
        {
            // Проверяем, что куб находится над поверхностью земли
            Vector3 groundCheckPos = position;
            groundCheckPos.y -= (_cubeSize * 0.5f); // Проверяем нижнюю часть куба

            if (Physics.Raycast(groundCheckPos, Vector3.down, out RaycastHit hit, _cubeSize, _groundLayerMask))
            {
                // Если нижняя часть куба пересекается с землей, поднимаем куб
                // Но только если куб не находится значительно выше земли (для многоуровневых структур)
                float groundLevel = hit.point.y + (_cubeSize * 0.5f);

                // Если текущая позиция значительно выше земли, не опускаем куб
                if (position.y > groundLevel + _cubeSize)
                {
                    return position; // Оставляем куб на текущей высоте
                }

                position.y = groundLevel;
            }

            return position;
        }

        private Vector3 GetGhostCubePosition()
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera != null)
            {
                Vector3 cameraPosition = playerCamera.transform.position;
                Vector3 cameraForward = playerCamera.transform.forward;

                // Создаем луч от камеры вперед
                Ray ray = new Ray(cameraPosition, cameraForward);

                // Сначала проверяем пересечение с существующими кубами
                if (Physics.Raycast(ray, out RaycastHit cubeHit, 50f))
                {
                    GameObject hitObject = cubeHit.collider.gameObject;
                    Cube hitCube = hitObject.GetComponent<Cube>();

                    // Если попали в куб, прилипаем к ближайшей к игроку стороне
                    if (hitCube != null)
                    {
                        return SnapToNearestSideFromPlayer(cameraPosition, hitObject.transform.position);
                    }
                }

                // Если не попали в куб, проверяем пересечение с любыми объектами
                if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                {
                    // Всегда используем сферный рейкаст для поиска ближайших кубов
                    Vector3 spherecastResult = FindNearestCubeWithSpherecast(hit.point, cameraPosition);

                    // Если сферный рейкаст не нашел кубы, проверяем что мы попали в землю
                    if (spherecastResult == Vector3.zero &&
                        ((1 << hit.collider.gameObject.layer) & _groundLayerMask) != 0)
                    {
                        // Размещаем куб на поверхности земли
                        Vector3 surfacePosition = hit.point;
                        surfacePosition.y = hit.point.y + (_cubeSize * 0.5f); // Половина куба над поверхностью
                        return surfacePosition;
                    }

                    return spherecastResult;
                }
                else
                {
                    // Если не попали ни во что, не создаем куб
                    return Vector3.zero;
                }
            }

            return Vector3.zero;
        }

        private Vector3 FindNearestCubeWithSpherecast(Vector3 hitPoint, Vector3 cameraPosition)
        {
            // Направление от точки попадания к камере (центр рейкаста)
            Vector3 directionToCamera = (cameraPosition - hitPoint).normalized;

            // Выполняем сферный рейкаст от точки попадания в направлении камеры
            RaycastHit[] sphereHits =
                Physics.SphereCastAll(hitPoint, _spherecastRadius, directionToCamera, _spherecastDistance);

            // Фильтруем только кубы и сортируем по расстоянию до центра рейкаста
            List<RaycastHit> cubeHits = new List<RaycastHit>();

            foreach (RaycastHit hit in sphereHits)
            {
                // Исключаем сам призрак
                if (hit.collider.gameObject == _ghostCube)
                    continue;

                Cube cube = hit.collider.GetComponent<Cube>();
                if (cube != null)
                {
                    cubeHits.Add(hit);
                }
            }

            if (cubeHits.Count == 0)
            {
                // Если не нашли кубы, возвращаем нулевую позицию
                return Vector3.zero;
            }

            // Находим ближайший к центру рейкаста куб
            RaycastHit closestHit = cubeHits[0];
            float closestDistance = Vector3.Distance(hitPoint, closestHit.collider.transform.position);

            foreach (RaycastHit hit in cubeHits)
            {
                float distance = Vector3.Distance(hitPoint, hit.collider.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestHit = hit;
                }
            }

            // Определяем сторону куба, направленную к центру рейкаста
            Vector3 cubePosition = closestHit.collider.transform.position;
            Vector3 directionToHitPoint = (hitPoint - cubePosition).normalized;

            // Находим ближайшую сторону куба к точке попадания
            Vector3 targetSide = GetClosestSideToDirection(directionToHitPoint);

            // Позиционируем призрачный куб на найденной стороне
            Vector3 ghostPosition = cubePosition + targetSide * _cubeSize;

            // Проверяем, что куб не пересекается с землей
            if (Mathf.Abs(targetSide.y) < 0.9f && ghostPosition.y < _cubeSize * 2f)
            {
                ghostPosition = EnsureAboveGround(ghostPosition);
            }

            return ghostPosition;
        }

        private Vector3 GetClosestSideToDirection(Vector3 direction)
        {
            // Определяем доминирующую ось в направлении
            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);
            float absZ = Mathf.Abs(direction.z);

            // Возвращаем нормаль ближайшей стороны
            if (absX >= absY && absX >= absZ)
            {
                // Доминирует X ось
                return direction.x > 0 ? Vector3.right : Vector3.left;
            }
            else if (_allowVerticalSnapping && absY >= absX && absY >= absZ)
            {
                // Доминирует Y ось
                return direction.y > 0 ? Vector3.up : Vector3.down;
            }
            else
            {
                // Доминирует Z ось
                return direction.z > 0 ? Vector3.forward : Vector3.back;
            }
        }

        private Vector3 GetSnappedPosition(RaycastHit hit)
        {
            // Получаем нормаль поверхности куба
            Vector3 normal = hit.normal;

            // Вычисляем позицию прилипания
            Vector3 hitPoint = hit.point;
            Vector3 snappedPosition = hitPoint + normal * (_cubeSize * 0.5f);

            // Округляем до целых чисел для точного позиционирования
            snappedPosition.x = Mathf.Round(snappedPosition.x);
            snappedPosition.y = Mathf.Round(snappedPosition.y);
            snappedPosition.z = Mathf.Round(snappedPosition.z);

            // Проверяем, что новая позиция не пересекается с существующими кубами
            if (!IsPositionOccupied(snappedPosition))
            {
                return snappedPosition;
            }

            // Если позиция занята, пытаемся найти ближайшую свободную позицию
            Vector3 alternativePosition = FindNearestFreePosition(snappedPosition, hitPoint, normal);
            return alternativePosition;
        }

        private Vector3 FindNearestFreePosition(Vector3 targetPosition, Vector3 hitPoint, Vector3 _)
        {
            // Пробуем позиции в разных направлениях от целевой позиции
            Vector3[] directions =
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
                Vector3.up + Vector3.left, Vector3.up + Vector3.right, Vector3.up + Vector3.forward,
                Vector3.up + Vector3.back,
                Vector3.down + Vector3.left, Vector3.down + Vector3.right, Vector3.down + Vector3.forward,
                Vector3.down + Vector3.back
            };

            foreach (Vector3 direction in directions)
            {
                Vector3 testPosition = targetPosition + direction * _cubeSize;
                testPosition.x = Mathf.Round(testPosition.x);
                testPosition.y = Mathf.Round(testPosition.y);
                testPosition.z = Mathf.Round(testPosition.z);

                if (!IsPositionOccupied(testPosition))
                {
                    return testPosition;
                }
            }

            // Если не нашли свободную позицию, возвращаем исходную точку пересечения
            return hitPoint;
        }

        private bool IsPositionOccupied(Vector3 position)
        {
            // Проверяем, есть ли куб в данной позиции с более точной проверкой
            Collider[] colliders = Physics.OverlapBox(position, Vector3.one * (_cubeSize * 0.49f));

            foreach (Collider cubeCollider in colliders)
            {
                // Исключаем сам призрак из проверки
                if (cubeCollider.gameObject == _ghostCube)
                    continue;

                if (cubeCollider.GetComponent<Cube>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 FindAlternativePositionNearPlayer(Vector3 occupiedPosition)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera == null)
            {
                playerCamera = FindAnyObjectByType<Camera>();
            }

            if (playerCamera == null)
            {
                return occupiedPosition;
            }

            Vector3 playerPosition = playerCamera.transform.position;
            Vector3 cameraForward = playerCamera.transform.forward;

            // Находим все кубы в радиусе магнитного прилипания
            Collider[] nearbyColliders = Physics.OverlapSphere(occupiedPosition, _magneticDistance * 2f);

            Vector3 bestPosition = occupiedPosition;
            float closestDistance = float.MaxValue;

            foreach (Collider nearbyCollider in nearbyColliders)
            {
                // Исключаем сам призрак
                if (nearbyCollider.gameObject == _ghostCube)
                    continue;

                Cube cube = nearbyCollider.GetComponent<Cube>();
                if (cube != null)
                {
                    Vector3 cubePosition = nearbyCollider.transform.position;

                    // Сначала пробуем сторону, на которую смотрит игрок
                    Vector3 facingSidePosition = SnapToFacingSide(cameraForward, cubePosition);

                    if (!IsPositionOccupied(facingSidePosition))
                    {
                        float distanceToPlayer = Vector3.Distance(playerPosition, facingSidePosition);
                        if (distanceToPlayer < closestDistance)
                        {
                            closestDistance = distanceToPlayer;
                            bestPosition = facingSidePosition;
                        }
                    }
                    else
                    {
                        // Если приоритетная сторона занята, пробуем остальные стороны
                        Vector3[] sideOffsets =
                        {
                            Vector3.right * _cubeSize, // Правая сторона
                            Vector3.left * _cubeSize, // Левая сторона
                            Vector3.up * _cubeSize, // Верхняя сторона
                            Vector3.down * _cubeSize, // Нижняя сторона
                            Vector3.forward * _cubeSize, // Передняя сторона
                            Vector3.back * _cubeSize // Задняя сторона
                        };

                        foreach (Vector3 offset in sideOffsets)
                        {
                            Vector3 testPosition = cubePosition + offset;
                            testPosition = EnsureAboveGround(testPosition);

                            // Проверяем, что позиция свободна
                            if (!IsPositionOccupied(testPosition))
                            {
                                // Вычисляем расстояние от игрока до этой позиции
                                float distanceToPlayer = Vector3.Distance(playerPosition, testPosition);

                                // Выбираем позицию ближайшую к игроку
                                if (distanceToPlayer < closestDistance)
                                {
                                    closestDistance = distanceToPlayer;
                                    bestPosition = testPosition;
                                }
                            }
                        }
                    }
                }
            }

            return bestPosition;
        }

        private void CreateCube()
        {
            Vector3 basePosition = GetGhostCubePosition();
            Vector3 spawnPosition = ApplyMagneticSnapping(basePosition);

            // Проверяем, что позиция не равна нулю (не попали в землю)
            if (spawnPosition == Vector3.zero)
            {
                Debug.Log("Cannot create cube: no valid surface found!");
                return;
            }

            // Если позиция занята, пытаемся найти альтернативную позицию
            if (IsPositionOccupied(spawnPosition))
            {
                spawnPosition = FindAlternativePositionNearPlayer(spawnPosition);

                // Если альтернативная позиция тоже занята, не создаем куб
                if (IsPositionOccupied(spawnPosition))
                {
                    Debug.Log("Cannot create cube: all nearby positions are occupied!");
                    return;
                }
            }

            if (_cubePrefab == null)
            {
                Debug.LogError("Cube prefab is not assigned!");
                return;
            }

            GameObject newCube = Instantiate(_cubePrefab, spawnPosition, Quaternion.identity);

            ColorCube colorCube = newCube.GetComponent<ColorCube>();
            if (colorCube != null)
            {
                colorCube.Setup(_selectedColor);
            }
            else
            {
                MeshRenderer cubeRenderer = newCube.GetComponent<MeshRenderer>();
                if (cubeRenderer != null)
                {
                    MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                    propertyBlock.SetColor("_BaseColor", _selectedColor);
                    cubeRenderer.SetPropertyBlock(propertyBlock);
                }
            }

            Cube cubeComponent = newCube.GetComponent<Cube>();
            if (cubeComponent != null)
            {
                cubeComponent.BlockTypeID = 0;
            }
        }
    }
}
