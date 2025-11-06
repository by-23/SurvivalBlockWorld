using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Assets._Project.Scripts.UI
{
    public class CubeCreator : GhostPlacementBase
    {
        // Статический массив смещений для проверки соседей
        private static readonly Vector3Int[] NeighborOffsets = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0), // right
            new Vector3Int(-1, 0, 0), // left
            new Vector3Int(0, 1, 0), // up
            new Vector3Int(0, -1, 0), // down
            new Vector3Int(0, 0, 1), // forward
            new Vector3Int(0, 0, -1) // back
        };

        [Header("UI References")] [SerializeField]
        private Button createButton;

        [SerializeField] private Transform colorButtonsParent;

        [SerializeField] private Button addColorButton;
        [SerializeField] private Button colorButtonPrefab;

        [Header("Color Picker")] [SerializeField]
        private GameObject colorPickerPanel;

        [SerializeField] private Image paletteImage; // изображение-палитра для выбора цвета
        [SerializeField] private Image previewImage; // превью текущего цвета

        // внутреннее состояние выбора цвета с удержанием
        private bool _isColorPicking;
        private bool _hasHoverColor;
        private Color _hoverPickedColor;

        [Header("Spawn Settings")] [SerializeField]
        private float _cubeSize = 1f;

        [SerializeField] private float _magneticDistance = 0.3f;
        [SerializeField] private bool _allowVerticalSnapping = true;
        [SerializeField] private float _spherecastRadius = 0.6f;
        [SerializeField] private float _spherecastDistance = 1.2f;

        [Header("Cube Prefab")] [SerializeField]
        private GameObject _cubePrefab;

        [Header("Ghost Cube")] [SerializeField]
        private GameObject _ghostCubePrefab;

        [Header("Auto Grouping")] [SerializeField]
        private float _groupingTimerDuration = 2f;

        private Color _selectedColor = Color.white;
        private Button _selectedColorButton;

        private List<Cube> _placedCubes = new List<Cube>();
        private Coroutine _groupingCoroutine;
        private Camera _cachedCamera;

        private void Start()
        {
            if (createButton != null)
            {
                createButton.onClick.AddListener(OnCreateButtonClicked);
            }

            if (addColorButton != null)
            {
                addColorButton.onClick.AddListener(OpenColorPicker);
            }

            if (colorPickerPanel != null)
            {
                colorPickerPanel.SetActive(false);
            }

            SetupColorButtons();
            CreateGhostCube();
            CacheCamera();
        }

        private void CacheCamera()
        {
            _cachedCamera = GetPlayerCamera();
        }

        private void Update()
        {
            if (_isGhostActive && _ghostRoot != null)
            {
                UpdateGhostCubePosition();
            }

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
        }

        private void OpenColorPicker()
        {
            if (colorPickerPanel == null) return;

            // обновляем превью текущего цвета
            UpdatePickerPreview(_selectedColor);

            _isColorPicking = false;
            _hasHoverColor = false;

            colorPickerPanel.SetActive(true);
        }

        private void CancelColorPick()
        {
            if (colorPickerPanel != null)
            {
                colorPickerPanel.SetActive(false);
            }
        }

        private void UpdatePickerPreview(Color color)
        {
            if (previewImage != null)
            {
                previewImage.color = color;
            }
        }

        // Начало удержания на палитре — начинаем выбор
        public void OnPalettePointerDown(BaseEventData eventData)
        {
            if (paletteImage == null || colorPickerPanel == null) return;
            var ped = eventData as PointerEventData;
            if (ped == null) return;

            _isColorPicking = true;
            _hasHoverColor = false;

            if (TrySampleColorFromImage(paletteImage, ped.position, ped.pressEventCamera, out var picked))
            {
                _hoverPickedColor = picked;
                _hasHoverColor = true;
                UpdatePickerPreview(_hoverPickedColor);
            }
        }

        // Движение при удержании — обновляем превью
        public void OnPaletteDrag(BaseEventData eventData)
        {
            if (!_isColorPicking || paletteImage == null) return;
            var ped = eventData as PointerEventData;
            if (ped == null) return;

            if (TrySampleColorFromImage(paletteImage, ped.position, ped.pressEventCamera, out var picked))
            {
                _hoverPickedColor = picked;
                _hasHoverColor = true;
                UpdatePickerPreview(_hoverPickedColor);
            }
        }

        // Завершение удержания — применяем последний цвет и закрываем панель
        public void OnPalettePointerUp(BaseEventData eventData)
        {
            if (paletteImage == null || colorPickerPanel == null) return;
            var ped = eventData as PointerEventData;
            if (ped == null) return;

            // Применяем только если отпускание произошло над палитрой
            if (!RectTransformUtility.RectangleContainsScreenPoint(paletteImage.rectTransform, ped.position,
                    ped.pressEventCamera))
            {
                _isColorPicking = false;
                _hasHoverColor = false;
                colorPickerPanel.SetActive(false);
                return;
            }

            Color picked;
            if (TrySampleColorFromImage(paletteImage, ped.position, ped.pressEventCamera, out picked))
            {
                CreateColorButton(picked);
            }

            _isColorPicking = false;
            _hasHoverColor = false;
            colorPickerPanel.SetActive(false);
        }

        // Обработчик клика по фону панели (клик вне изображения закрывает панель)
        public void OnPickerBackgroundClicked()
        {
            CancelColorPick();
        }

        private static bool TrySampleColorFromImage(Image image, Vector2 screenPos, Camera eventCamera, out Color color)
        {
            color = Color.white;
            if (image == null || image.sprite == null || image.sprite.texture == null)
                return false;

            RectTransform rectTransform = image.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPos, eventCamera,
                    out var local))
                return false;

            Rect r = rectTransform.rect;
            float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
            float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);

            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            var sprite = image.sprite;
            var tex = sprite.texture;
            if (tex == null) return false;

            // преобразуем UV на спрайте в UV текстуры с учетом вырезанного прямоугольника
            Rect tr = sprite.textureRect;
            float texU = Mathf.Lerp(tr.xMin / tex.width, tr.xMax / tex.width, u);
            float texV = Mathf.Lerp(tr.yMin / tex.height, tr.yMax / tex.height, v);

            // Пытаемся прочитать цвет из текстуры, учитывая, что она может быть не readable
            int px = Mathf.Clamp(Mathf.RoundToInt(texU * tex.width), 0, tex.width - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(texV * tex.height), 0, tex.height - 1);

            if (tex.isReadable)
            {
                color = tex.GetPixel(px, py);
                return true;
            }

            // Фоллбэк: копируем в временный RenderTexture и читаем 1 пиксель
            RenderTexture rt = null;
            Texture2D onePx = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                onePx = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
                // координаты в ReadPixels считаются от левого нижнего угла активного RT
                onePx.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
                onePx.Apply(false, false);
                color = onePx.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (onePx != null) Object.Destroy(onePx);
            }
        }

        private void CreateColorButton(Color c)
        {
            if (colorButtonsParent == null || colorButtonPrefab == null) return;

            var btn = Instantiate(colorButtonPrefab, colorButtonsParent);

            // окрашиваем Image
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = c;
            }

            // настраиваем ColorBlock для корректного выбора
            var cb = btn.colors;
            cb.normalColor = c;
            // слегка ярче при наведении
            Color highlighted = new Color(
                Mathf.Clamp01(c.r * 1.1f),
                Mathf.Clamp01(c.g * 1.1f),
                Mathf.Clamp01(c.b * 1.1f),
                c.a);
            cb.highlightedColor = highlighted;
            // слегка темнее при нажатии
            Color pressed = new Color(
                Mathf.Clamp01(c.r * 0.9f),
                Mathf.Clamp01(c.g * 0.9f),
                Mathf.Clamp01(c.b * 0.9f),
                c.a);
            cb.pressedColor = pressed;
            btn.colors = cb;

            btn.onClick.AddListener(() => OnColorButtonClicked(btn));

            // сразу выбрать новый цвет
            OnColorButtonClicked(btn);
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
                ColorBlock colorBlock = clickedButton.colors;
                _selectedColor = colorBlock.normalColor;
            }

            ShowGhostCube();
        }

        private void OnCreateButtonClicked()
        {
            CreateCube();
        }

        private void CreateGhostCube()
        {
            if (_ghostCubePrefab != null)
            {
                _ghostRoot = Instantiate(_ghostCubePrefab);
            }
            else
            {
                _ghostRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }

            _ghostRoot.name = "GhostCube";
            _ghostRoot.SetActive(false);

            SetupGhostCubeMaterial();
        }

        private void SetupGhostCubeMaterial()
        {
            if (_ghostRoot == null) return;

            MeshRenderer cubeRenderer = _ghostRoot.GetComponent<MeshRenderer>();
            if (cubeRenderer != null)
            {
                Material mat = CreateGhostMaterial(_selectedColor);
                cubeRenderer.material = mat;
            }

            SetupGhostCollider(_ghostRoot);
        }


        public void HideGhostCube()
        {
            HideGhost();
        }

        public void ShowGhostCube()
        {
            ShowGhost();
        }

        protected override void UpdateGhostMaterial(bool isOccupied)
        {
            if (_ghostRoot == null) return;

            MeshRenderer cubeRenderer = _ghostRoot.GetComponent<MeshRenderer>();
            if (cubeRenderer != null && cubeRenderer.material != null)
            {
                Color ghostColor;
                if (isOccupied)
                {
                    ghostColor = new Color(1f, 0f, 0f, _ghostTransparency);
                }
                else
                {
                    ghostColor = new Color(_selectedColor.r, _selectedColor.g, _selectedColor.b, _ghostTransparency);
                }

                cubeRenderer.material.color = ghostColor;
            }
        }

        private void UpdateGhostCubePosition()
        {
            if (_ghostRoot == null) return;

            Vector3 targetPosition = GetGhostPosition();

            Vector3 magnetizedPosition = ApplyMagneticSnapping(targetPosition);

            if (IsPositionOccupied(magnetizedPosition))
            {
                magnetizedPosition = FindAlternativePosition(magnetizedPosition);
            }

            _ghostRoot.transform.position = magnetizedPosition;

            bool isOccupied = IsPositionOccupied(magnetizedPosition);
            UpdateGhostMaterial(isOccupied);
        }

        private Vector3 ApplyMagneticSnapping(Vector3 position)
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return position;
            }

            Vector3 lookDirection = _cachedCamera.transform.forward;
            Ray ray = new Ray(_cachedCamera.transform.position, lookDirection);

            RaycastHit[] hits = Physics.RaycastAll(ray, _magneticDistance * 2f);
            System.Array.Sort(hits, (hit1, hit2) => hit1.distance.CompareTo(hit2.distance));

            Vector3 snappedPosition = position;

            foreach (RaycastHit hit in hits)
            {
                if (_ghostRoot != null && hit.collider.gameObject == _ghostRoot)
                    continue;

                Cube cube = hit.collider.GetComponent<Cube>();
                if (cube != null)
                {
                    Vector3 cubePosition = hit.collider.transform.position;
                    Vector3 directionToCube = (cubePosition - _cachedCamera.transform.position).normalized;
                    float dotProduct = Vector3.Dot(lookDirection.normalized, directionToCube);

                    if (dotProduct > 0.5f)
                    {
                        snappedPosition = SnapToNearestSide(position, cubePosition);
                        break;
                    }
                }
            }

            return snappedPosition;
        }

        private Vector3 SnapToNearestSide(Vector3 ghostPosition, Vector3 cubePosition)
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return cubePosition;
            }

            // Используем направление взгляда игрока для определения стороны куба
            Vector3 cameraForward = _cachedCamera.transform.forward;
            Vector3 snappedPosition = SnapToFacingSide(cameraForward, cubePosition);

            return snappedPosition;
        }

        private Vector3 SnapToNearestSideFromPlayer(Vector3 playerPosition, Vector3 cubePosition)
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return cubePosition;
            }

            Vector3 cameraForward = _cachedCamera.transform.forward;
            Vector3 snappedPosition = SnapToFacingSide(cameraForward, cubePosition);

            return snappedPosition;
        }

        private Vector3 SnapToFacingSide(Vector3 lookDirection, Vector3 cubePosition)
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return cubePosition;
            }

            Ray ray = new Ray(_cachedCamera.transform.position, lookDirection);
            Bounds cubeBounds = new Bounds(cubePosition, Vector3.one * _cubeSize);

            if (cubeBounds.IntersectRay(ray, out float distance))
            {
                Vector3 intersectionPoint = ray.origin + ray.direction * distance;
                Vector3 faceNormal = GetIntersectedFaceNormal(intersectionPoint, cubePosition);
                Vector3 snappedPosition = cubePosition + faceNormal * _cubeSize;

                if (Mathf.Abs(faceNormal.y) < 0.9f &&
                    snappedPosition.y < _cubeSize * 2f)
                {
                    snappedPosition = EnsureAboveGround(snappedPosition);
                }

                return snappedPosition;
            }

            Vector3 normalizedLookDirection = lookDirection.normalized;
            float absX = Mathf.Abs(normalizedLookDirection.x);
            float absY = Mathf.Abs(normalizedLookDirection.y);
            float absZ = Mathf.Abs(normalizedLookDirection.z);

            Vector3 fallbackPosition = cubePosition;

            if (absX >= absY && absX >= absZ)
            {
                if (normalizedLookDirection.x > 0)
                {
                    fallbackPosition = cubePosition + Vector3.right * _cubeSize;
                }
                else
                {
                    fallbackPosition = cubePosition + Vector3.left * _cubeSize;
                }
            }
            else if (_allowVerticalSnapping && absY >= absX && absY >= absZ)
            {
                if (normalizedLookDirection.y > 0)
                {
                    fallbackPosition = cubePosition + Vector3.up * _cubeSize;
                }
                else
                {
                    fallbackPosition = cubePosition + Vector3.down * _cubeSize;
                }
            }
            else
            {
                if (normalizedLookDirection.z > 0)
                {
                    fallbackPosition = cubePosition + Vector3.forward * _cubeSize;
                }
                else
                {
                    fallbackPosition = cubePosition + Vector3.back * _cubeSize;
                }
            }

            if ((absX >= absY && absX >= absZ || absZ >= absX && absZ >= absY) && fallbackPosition.y < _cubeSize * 2f)
            {
                fallbackPosition = EnsureAboveGround(fallbackPosition);
            }

            return fallbackPosition;
        }

        private Vector3 GetIntersectedFaceNormal(Vector3 intersectionPoint, Vector3 cubePosition)
        {
            Vector3 relativePoint = intersectionPoint - cubePosition;
            float halfSize = _cubeSize * 0.5f;

            float[] distances =
            {
                Mathf.Abs(relativePoint.x - halfSize),
                Mathf.Abs(relativePoint.x + halfSize),
                Mathf.Abs(relativePoint.y - halfSize),
                Mathf.Abs(relativePoint.y + halfSize),
                Mathf.Abs(relativePoint.z - halfSize),
                Mathf.Abs(relativePoint.z + halfSize)
            };

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

            Vector3[] faceNormals =
            {
                Vector3.right,
                Vector3.left,
                Vector3.up,
                Vector3.down,
                Vector3.forward,
                Vector3.back
            };

            return faceNormals[closestFace];
        }

        protected override Vector3 EnsureAboveGround(Vector3 position, float offset = 0.5f)
        {
            // Поднимаем куб чтобы он не утонул в земле, но не трогаем если он уже высоко (для построек на высоте)
            float cubeHalfSize = _cubeSize * 0.5f;
            Vector3 groundCheckPos = position;
            groundCheckPos.y -= cubeHalfSize;

            if (Physics.Raycast(groundCheckPos, Vector3.down, out RaycastHit hit, _cubeSize, _groundLayerMask))
            {
                float groundLevel = hit.point.y + cubeHalfSize;

                if (position.y > groundLevel + _cubeSize)
                {
                    return position;
                }

                position.y = groundLevel;
            }

            return position;
        }

        protected override Vector3 GetGhostPosition()
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return Vector3.zero;
            }

            Vector3 cameraPosition = _cachedCamera.transform.position;
            Vector3 cameraForward = _cachedCamera.transform.forward;

            Ray ray = new Ray(cameraPosition, cameraForward);

            if (Physics.Raycast(ray, out RaycastHit cubeHit, 50f))
            {
                GameObject hitObject = cubeHit.collider.gameObject;
                Cube hitCube = hitObject.GetComponent<Cube>();

                if (hitCube != null)
                {
                    return SnapToNearestSideFromPlayer(cameraPosition, hitObject.transform.position);
                }
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                Vector3 spherecastResult = FindNearestCubeWithSpherecast(hit.point, cameraPosition);

                if (spherecastResult == Vector3.zero &&
                    ((1 << hit.collider.gameObject.layer) & _groundLayerMask) != 0)
                {
                    Vector3 surfacePosition = hit.point;
                    surfacePosition.y = hit.point.y + (_cubeSize * 0.5f);
                    return surfacePosition;
                }

                return spherecastResult;
            }
            else
            {
                return Vector3.zero;
            }
        }

        private Vector3 FindNearestCubeWithSpherecast(Vector3 hitPoint, Vector3 cameraPosition)
        {
            Vector3 directionToCamera = (cameraPosition - hitPoint).normalized;
            RaycastHit[] sphereHits =
                Physics.SphereCastAll(hitPoint, _spherecastRadius, directionToCamera, _spherecastDistance);

            List<RaycastHit> cubeHits = new List<RaycastHit>();

            foreach (RaycastHit hit in sphereHits)
            {
                if (_ghostRoot != null && hit.collider.gameObject == _ghostRoot)
                    continue;

                Cube cube = hit.collider.GetComponent<Cube>();
                if (cube != null)
                {
                    cubeHits.Add(hit);
                }
            }

            if (cubeHits.Count == 0)
            {
                return Vector3.zero;
            }

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

            Vector3 cubePosition = closestHit.collider.transform.position;
            Vector3 directionToHitPoint = (hitPoint - cubePosition).normalized;
            Vector3 targetSide = GetClosestSideToDirection(directionToHitPoint);
            Vector3 ghostPosition = cubePosition + targetSide * _cubeSize;

            if (Mathf.Abs(targetSide.y) < 0.9f && ghostPosition.y < _cubeSize * 2f)
            {
                ghostPosition = EnsureAboveGround(ghostPosition);
            }

            return ghostPosition;
        }

        private Vector3 GetClosestSideToDirection(Vector3 direction)
        {
            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);
            float absZ = Mathf.Abs(direction.z);

            if (absX >= absY && absX >= absZ)
            {
                return direction.x > 0 ? Vector3.right : Vector3.left;
            }
            else if (_allowVerticalSnapping && absY >= absX && absY >= absZ)
            {
                return direction.y > 0 ? Vector3.up : Vector3.down;
            }
            else
            {
                return direction.z > 0 ? Vector3.forward : Vector3.back;
            }
        }

        private Vector3 GetSnappedPosition(RaycastHit hit)
        {
            Vector3 normal = hit.normal;
            Vector3 hitPoint = hit.point;
            Vector3 snappedPosition = hitPoint + normal * (_cubeSize * 0.5f);

            snappedPosition.x = Mathf.Round(snappedPosition.x);
            snappedPosition.y = Mathf.Round(snappedPosition.y);
            snappedPosition.z = Mathf.Round(snappedPosition.z);

            if (!IsPositionOccupied(snappedPosition))
            {
                return snappedPosition;
            }

            Vector3 alternativePosition = FindNearestFreePosition(snappedPosition, hitPoint, normal);
            return alternativePosition;
        }

        private Vector3 FindNearestFreePosition(Vector3 targetPosition, Vector3 hitPoint, Vector3 _)
        {
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

            return hitPoint;
        }

        protected override bool IsPositionOccupied(Vector3 position)
        {
            Collider[] colliders = Physics.OverlapBox(position, Vector3.one * (_cubeSize * 0.49f));

            foreach (Collider cubeCollider in colliders)
            {
                if (_ghostRoot != null && cubeCollider.gameObject == _ghostRoot)
                    continue;

                if (cubeCollider.GetComponent<Cube>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        protected override Vector3 FindAlternativePosition(Vector3 occupiedPosition)
        {
            if (_cachedCamera == null)
            {
                CacheCamera();
                if (_cachedCamera == null)
                    return occupiedPosition;
            }

            Vector3 playerPosition = _cachedCamera.transform.position;
            Vector3 cameraForward = _cachedCamera.transform.forward;

            Collider[] nearbyColliders = Physics.OverlapSphere(occupiedPosition, _magneticDistance * 2f);

            Vector3 bestPosition = occupiedPosition;
            float closestDistance = float.MaxValue;

            foreach (Collider nearbyCollider in nearbyColliders)
            {
                if (_ghostRoot != null && nearbyCollider.gameObject == _ghostRoot)
                    continue;

                Cube cube = nearbyCollider.GetComponent<Cube>();
                if (cube != null)
                {
                    Vector3 cubePosition = nearbyCollider.transform.position;
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
                        Vector3[] sideOffsets =
                        {
                            Vector3.right * _cubeSize,
                            Vector3.left * _cubeSize,
                            Vector3.up * _cubeSize,
                            Vector3.down * _cubeSize,
                            Vector3.forward * _cubeSize,
                            Vector3.back * _cubeSize
                        };

                        foreach (Vector3 offset in sideOffsets)
                        {
                            Vector3 testPosition = cubePosition + offset;
                            testPosition = EnsureAboveGround(testPosition);

                            if (!IsPositionOccupied(testPosition))
                            {
                                float distanceToPlayer = Vector3.Distance(playerPosition, testPosition);

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
            Vector3 basePosition = GetGhostPosition();
            Vector3 spawnPosition = ApplyMagneticSnapping(basePosition);

            if (spawnPosition == Vector3.zero)
            {
                Debug.Log("Cannot create cube: no valid surface found!");
                return;
            }

            if (IsPositionOccupied(spawnPosition))
            {
                spawnPosition = FindAlternativePosition(spawnPosition);

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

            // Настраиваем цвет и тип куба через утилиту
            CubeSetupHelper.SetupCube(newCube, _selectedColor, 0);

            Cube cubeComponent = newCube.GetComponent<Cube>();
            if (cubeComponent != null)
            {
                // Добавляем куб в список для группировки
                _placedCubes.Add(cubeComponent);

                // Перезапускаем таймер группировки
                RestartGroupingTimer();
            }
        }

        /// <summary>
        /// Перезапускает таймер группировки кубов
        /// </summary>
        private void RestartGroupingTimer()
        {
            if (_groupingCoroutine != null)
            {
                StopCoroutine(_groupingCoroutine);
            }

            _groupingCoroutine = StartCoroutine(GroupingTimerCoroutine());
        }

        /// <summary>
        /// Корутина таймера, которая группирует кубы после истечения времени
        /// </summary>
        private IEnumerator GroupingTimerCoroutine()
        {
            yield return new WaitForSeconds(_groupingTimerDuration);

            // Группируем кубы, если они есть
            if (_placedCubes.Count > 0)
            {
                GroupPlacedCubesIntoEntities();
            }

            _groupingCoroutine = null;
        }

        /// <summary>
        /// Группирует размещенные кубы в сущности по принципу соприкосновения
        /// </summary>
        private void GroupPlacedCubesIntoEntities()
        {
            // Фильтруем кубы, которые еще не принадлежат Entity
            List<Cube> ungroupedCubes = _placedCubes
                .Where(cube => cube != null && cube.transform.parent == null)
                .ToList();

            if (ungroupedCubes.Count == 0)
            {
                _placedCubes.Clear();
                return;
            }

            // Получаем все существующие Entity и их кубы
            Dictionary<Vector3Int, (Cube cube, Entity entity)> existingEntityCubes = GetExistingEntityCubesGrid();

            // Сначала находим группы среди новых кубов
            List<List<Cube>> groups = FindConnectedCubeGroups(ungroupedCubes);

            // Разделяем группы на те, что соприкасаются с существующими Entity, и изолированные
            Dictionary<Entity, List<Cube>> cubesToAddToExisting = new Dictionary<Entity, List<Cube>>();
            List<List<Cube>> isolatedGroups = new List<List<Cube>>();

            foreach (var group in groups)
            {
                if (group.Count == 0) continue;

                // Проверяем, соприкасается ли группа с существующим Entity
                Entity foundEntity = FindTouchingEntity(group, existingEntityCubes);

                if (foundEntity != null)
                {
                    // Добавляем всю группу к существующему Entity
                    if (!cubesToAddToExisting.ContainsKey(foundEntity))
                    {
                        cubesToAddToExisting[foundEntity] = new List<Cube>();
                    }

                    cubesToAddToExisting[foundEntity].AddRange(group);
                }
                else
                {
                    // Группа изолирована - создадим для неё новое Entity
                    isolatedGroups.Add(group);
                }
            }

            // Добавляем кубы к существующим Entity
            foreach (var kvp in cubesToAddToExisting)
            {
                AddCubesToExistingEntity(kvp.Key, kvp.Value);
            }

            // Создаем новые Entity для изолированных групп
            foreach (var group in isolatedGroups)
            {
                if (group.Count == 0) continue;
                CreateEntityFromCubes(group);
            }

            // Очищаем список размещенных кубов
            _placedCubes.Clear();
        }

        /// <summary>
        /// Получает сетку всех кубов из существующих Entity для быстрого поиска
        /// </summary>
        private Dictionary<Vector3Int, (Cube cube, Entity entity)> GetExistingEntityCubesGrid()
        {
            Dictionary<Vector3Int, (Cube, Entity)> grid = new Dictionary<Vector3Int, (Cube, Entity)>();

            // Находим все Entity в сцене
            Entity[] existingEntities = FindObjectsByType<Entity>(FindObjectsSortMode.None);

            foreach (var entity in existingEntities)
            {
                if (entity == null) continue;

                // Получаем все кубы из этого Entity
                Cube[] entityCubes = entity.GetComponentsInChildren<Cube>();
                foreach (var cube in entityCubes)
                {
                    if (cube == null) continue;
                    Vector3Int gridPos = WorldToGridPosition(cube.transform.position);
                    grid[gridPos] = (cube, entity);
                }
            }

            return grid;
        }

        /// <summary>
        /// Находит Entity, с которым соприкасается группа кубов
        /// </summary>
        private Entity FindTouchingEntity(List<Cube> cubeGroup,
            Dictionary<Vector3Int, (Cube cube, Entity entity)> existingEntityCubes)
        {
            if (cubeGroup == null || cubeGroup.Count == 0 || existingEntityCubes.Count == 0)
                return null;

            HashSet<Entity> touchingEntities = new HashSet<Entity>();

            // Проверяем каждый куб в группе
            foreach (var cube in cubeGroup)
            {
                if (cube == null) continue;

                Vector3Int cubePos = WorldToGridPosition(cube.transform.position);

                // Проверяем все 6 направлений на наличие кубов из существующих Entity
                foreach (var offset in NeighborOffsets)
                {
                    Vector3Int neighborPos = cubePos + offset;
                    if (existingEntityCubes.TryGetValue(neighborPos, out var neighborData))
                    {
                        // Проверяем, действительно ли кубы соприкасаются
                        if (AreCubesAdjacent(cube.transform.position, neighborData.cube.transform.position))
                        {
                            touchingEntities.Add(neighborData.entity);
                        }
                    }
                }
            }

            // Если группа соприкасается с несколькими Entity, возвращаем первый найденный
            // (в будущем можно улучшить логику - выбрать Entity с большим количеством точек соприкосновения)
            if (touchingEntities.Count > 0)
            {
                return touchingEntities.First();
            }

            return null;
        }

        /// <summary>
        /// Находит группы соприкасающихся кубов используя BFS
        /// </summary>
        private List<List<Cube>> FindConnectedCubeGroups(List<Cube> cubes)
        {
            List<List<Cube>> groups = new List<List<Cube>>();
            HashSet<Cube> visited = new HashSet<Cube>();

            // Создаем словарь для быстрого поиска кубов по позиции
            Dictionary<Vector3Int, Cube> cubeGrid = new Dictionary<Vector3Int, Cube>();
            foreach (var cube in cubes)
            {
                Vector3Int gridPos = WorldToGridPosition(cube.transform.position);
                cubeGrid[gridPos] = cube;
            }

            foreach (var cube in cubes)
            {
                if (visited.Contains(cube))
                    continue;

                // Находим все кубы в текущей группе используя BFS
                List<Cube> currentGroup = new List<Cube>();
                Queue<Cube> queue = new Queue<Cube>();
                queue.Enqueue(cube);
                visited.Add(cube);

                while (queue.Count > 0)
                {
                    Cube currentCube = queue.Dequeue();
                    currentGroup.Add(currentCube);

                    // Проверяем всех соседей (6 направлений)
                    Vector3Int currentPos = WorldToGridPosition(currentCube.transform.position);

                    foreach (var offset in NeighborOffsets)
                    {
                        Vector3Int neighborPos = currentPos + offset;
                        if (cubeGrid.TryGetValue(neighborPos, out Cube neighbor) && !visited.Contains(neighbor))
                        {
                            // Дополнительная проверка: убеждаемся, что кубы действительно соприкасаются
                            if (AreCubesAdjacent(currentCube.transform.position, neighbor.transform.position))
                            {
                                visited.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }

                if (currentGroup.Count > 0)
                {
                    groups.Add(currentGroup);
                }
            }

            return groups;
        }

        /// <summary>
        /// Конвертирует мировую позицию в сетку с учетом размера куба
        /// </summary>
        private Vector3Int WorldToGridPosition(Vector3 worldPos)
        {
            // Используем точное деление и округление для совместимости с системой размещения кубов
            return new Vector3Int(
                Mathf.RoundToInt(worldPos.x / _cubeSize),
                Mathf.RoundToInt(worldPos.y / _cubeSize),
                Mathf.RoundToInt(worldPos.z / _cubeSize)
            );
        }

        /// <summary>
        /// Проверяет, являются ли два куба соседями (соприкасаются)
        /// </summary>
        private bool AreCubesAdjacent(Vector3 pos1, Vector3 pos2)
        {
            Vector3 diff = pos1 - pos2;
            float distance = diff.magnitude;
            // Кубы соприкасаются, если расстояние примерно равно размеру куба (с небольшой погрешностью)
            float tolerance = _cubeSize * 0.1f;
            return Mathf.Abs(distance - _cubeSize) < tolerance;
        }

        /// <summary>
        /// Добавляет кубы к существующему Entity
        /// </summary>
        private void AddCubesToExistingEntity(Entity entity, List<Cube> cubes)
        {
            EntityCubeAttacher.AttachCubesToEntity(cubes, entity, updateEntity: true);
        }

        /// <summary>
        /// Создает Entity из группы кубов
        /// </summary>
        private void CreateEntityFromCubes(List<Cube> cubes)
        {
            EntityFactory.CreateEntityFromCubes(cubes, centerPosition: null, isKinematic: true);
        }
    }
}
