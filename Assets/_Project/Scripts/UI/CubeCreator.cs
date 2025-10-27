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
                    mat.SetFloat("_Mode", 3);
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
            if (_ghostCube == null) return;

            Vector3 targetPosition = GetGhostCubePosition();

            Vector3 magnetizedPosition = ApplyMagneticSnapping(targetPosition);

            if (IsPositionOccupied(magnetizedPosition))
            {
                magnetizedPosition = FindAlternativePositionNearPlayer(magnetizedPosition);
            }

            _ghostCube.transform.position = magnetizedPosition;

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

            Vector3 lookDirection = playerCamera.transform.forward;
            Ray ray = new Ray(playerCamera.transform.position, lookDirection);

            RaycastHit[] hits = Physics.RaycastAll(ray, _magneticDistance * 2f);
            System.Array.Sort(hits, (hit1, hit2) => hit1.distance.CompareTo(hit2.distance));

            Vector3 snappedPosition = position;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.gameObject == _ghostCube)
                    continue;

                Cube cube = hit.collider.GetComponent<Cube>();
                if (cube != null)
                {
                    Vector3 cubePosition = hit.collider.transform.position;
                    Vector3 directionToCube = (cubePosition - playerCamera.transform.position).normalized;
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

            Ray ray = new Ray(playerCamera.transform.position, lookDirection);
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

        private Vector3 EnsureAboveGround(Vector3 position)
        {
            // Поднимаем куб чтобы он не утонул в земле, но не трогаем если он уже высоко (для построек на высоте)
            Vector3 groundCheckPos = position;
            groundCheckPos.y -= (_cubeSize * 0.5f);

            if (Physics.Raycast(groundCheckPos, Vector3.down, out RaycastHit hit, _cubeSize, _groundLayerMask))
            {
                float groundLevel = hit.point.y + (_cubeSize * 0.5f);

                if (position.y > groundLevel + _cubeSize)
                {
                    return position;
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

            return Vector3.zero;
        }

        private Vector3 FindNearestCubeWithSpherecast(Vector3 hitPoint, Vector3 cameraPosition)
        {
            Vector3 directionToCamera = (cameraPosition - hitPoint).normalized;
            RaycastHit[] sphereHits =
                Physics.SphereCastAll(hitPoint, _spherecastRadius, directionToCamera, _spherecastDistance);

            List<RaycastHit> cubeHits = new List<RaycastHit>();

            foreach (RaycastHit hit in sphereHits)
            {
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

        private bool IsPositionOccupied(Vector3 position)
        {
            Collider[] colliders = Physics.OverlapBox(position, Vector3.one * (_cubeSize * 0.49f));

            foreach (Collider cubeCollider in colliders)
            {
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

            Collider[] nearbyColliders = Physics.OverlapSphere(occupiedPosition, _magneticDistance * 2f);

            Vector3 bestPosition = occupiedPosition;
            float closestDistance = float.MaxValue;

            foreach (Collider nearbyCollider in nearbyColliders)
            {
                if (nearbyCollider.gameObject == _ghostCube)
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
            Vector3 basePosition = GetGhostCubePosition();
            Vector3 spawnPosition = ApplyMagneticSnapping(basePosition);

            if (spawnPosition == Vector3.zero)
            {
                Debug.Log("Cannot create cube: no valid surface found!");
                return;
            }

            if (IsPositionOccupied(spawnPosition))
            {
                spawnPosition = FindAlternativePositionNearPlayer(spawnPosition);

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
