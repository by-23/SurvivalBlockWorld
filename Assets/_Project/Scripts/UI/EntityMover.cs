using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Управляет перемещением Entity без использования ghost-режима.
    /// Поднимает объект, держит его на заданном расстоянии впереди игрока,
    /// проверяет пересечения и размещает при отпускании.
    /// </summary>
    public class EntityMover : MonoBehaviour
    {
        [Header("Movement Settings")] [SerializeField]
        private float _holdDistance = 3f; // Максимальное расстояние для поднятия объекта

        [SerializeField] private float _minPickupDistance = 0.5f; // Минимальное расстояние для поднятия объекта

        [SerializeField] private float _dropForce = 5f;

        [Header("Button Text")] [SerializeField]
        private string _takeText = "Take";

        [SerializeField] private string _throwText = "Throw";

        [Header("Collision Detection")] [SerializeField]
        private LayerMask _collisionLayerMask = ~0;

        [Header("References")] [SerializeField]
        private Button _moveButton;

        [SerializeField] private TextMeshProUGUI _buttonTextTmp;
        [SerializeField] private Text _buttonText;

        [SerializeField] private EntityManager _entityManager;

        [SerializeField] private Camera _playerCamera;
        private Entity _heldEntity;
        private Rigidbody _heldRigidbody;
        private bool _isHolding;
        private float _currentHoldDistance; // Расстояние на котором объект был взят
        private HashSet<Collider> _collidingColliders = new HashSet<Collider>();
        private Dictionary<Collider, bool> _originalTriggerStates = new Dictionary<Collider, bool>();
        private List<CollisionDetector> _detectors = new List<CollisionDetector>();

        public bool IsHolding => _isHolding;
        public bool CanPlace => _isHolding && _collidingColliders.Count == 0;

        // Внутренний класс для отслеживания коллизий через OnTriggerStay
        private class CollisionDetector : MonoBehaviour
        {
            private EntityMover _mover;

            public void Initialize(EntityMover mover)
            {
                _mover = mover;
            }

            private void OnTriggerStay(Collider other)
            {
                if (_mover != null)
                {
                    _mover.OnTriggerStay(other);
                }
            }

            private void OnTriggerExit(Collider other)
            {
                if (_mover != null)
                {
                    _mover.OnTriggerExit(other);
                }
            }
        }

        private void Awake()
        {
            if (_playerCamera == null && Player.Instance != null && Player.Instance._playerCamera != null)
            {
                _playerCamera = Player.Instance._playerCamera.GetComponent<Camera>();
            }

            if (_entityManager == null)
            {
                _entityManager = FindFirstObjectByType<EntityManager>();
            }
        }

        private void OnEnable()
        {
            if (_moveButton != null)
            {
                _moveButton.onClick.RemoveAllListeners();
                _moveButton.onClick.AddListener(OnMoveButtonPressed);
            }

            UpdateButtonText();
        }

        private void OnDisable()
        {
            if (_moveButton != null)
            {
                _moveButton.onClick.RemoveAllListeners();
            }
        }

        private void OnMoveButtonPressed()
        {
            if (!gameObject.activeInHierarchy)
                return;

            // Если объект уже удерживается - проверяем возможность размещения
            if (_isHolding)
            {
                // Размещаем только если нет пересечений
                if (CanPlace)
                {
                    Drop();
                }

                return;
            }

            // Если объект не удерживается - начинаем перемещение (берем entity)
            TryPickupEntity();
        }

        private void TryPickupEntity()
        {
            if (_entityManager == null || _playerCamera == null)
            {
                Debug.LogWarning("EntityMover: не найдены необходимые компоненты");
                return;
            }

            // Получаем entity на который наведен игрок
            Entity targetEntity = _entityManager.GetTargetEntity();
            if (targetEntity == null)
            {
                return;
            }

            // Проверяем, что entity не является уже ghost
            if (targetEntity.IsGhost)
            {
                return;
            }

            // Проверяем расстояние до объекта
            Vector3 cameraPosition = _playerCamera.transform.position;
            Bounds entityBounds = GetEntityBounds(targetEntity);
            Vector3 closestPoint = entityBounds.ClosestPoint(cameraPosition);
            float distanceToObject = Vector3.Distance(cameraPosition, closestPoint);

            if (distanceToObject < _minPickupDistance || distanceToObject > _holdDistance)
            {
                return;
            }

            // Отменяем предыдущее удержание если есть
            if (_isHolding)
            {
                Cancel();
            }

            // Начинаем удержание entity
            Begin(targetEntity, _playerCamera);
        }

        private void Update()
        {
            if (_isHolding && _heldEntity != null)
            {
                UpdateHeldEntityPosition();
            }
        }

        /// <summary>
        /// Начинает удержание Entity
        /// </summary>
        public void Begin(Entity entity, Camera playerCamera = null)
        {
            if (entity == null)
            {
                Debug.LogError("EntityMover.Begin: entity is null");
                return;
            }

            if (playerCamera != null)
                _playerCamera = playerCamera;
            else if (_playerCamera == null)
                _playerCamera = Camera.main;

            if (_playerCamera == null)
            {
                Debug.LogError("EntityMover.Begin: Camera не найдена");
                return;
            }

            _heldEntity = entity;
            _heldRigidbody = _heldEntity.GetComponent<Rigidbody>();

            if (_heldRigidbody == null)
            {
                Debug.LogError("EntityMover.Begin: Entity не имеет Rigidbody");
                return;
            }

            // Вычисляем и сохраняем расстояние до объекта в момент взятия
            Vector3 cameraPosition = _playerCamera.transform.position;
            Bounds entityBounds = GetEntityBounds(_heldEntity);
            Vector3 closestPoint = entityBounds.ClosestPoint(cameraPosition);
            _currentHoldDistance = Vector3.Distance(cameraPosition, closestPoint);

            // Выключаем физику
            _heldEntity.SetKinematicState(true, true);
            _heldRigidbody.useGravity = false;

            // Делаем коллайдеры триггерами для проверки пересечений
            SetCollidersAsTriggers(true);
            AddCollisionDetectors();

            _isHolding = true;
            _collidingColliders.Clear();

            UpdateButtonText();
            UpdateHeldEntityPosition();
        }

        /// <summary>
        /// Отпускает Entity и включает физику
        /// </summary>
        public void Drop()
        {
            if (!_isHolding || _heldEntity == null)
                return;

            RemoveCollisionDetectors();
            // Восстанавливаем коллайдеры
            SetCollidersAsTriggers(false);

            // Включаем физику обратно
            if (_heldRigidbody != null)
            {
                _heldEntity.SetKinematicState(false);
                _heldRigidbody.useGravity = true;

                // Применяем силу для броска
                if (_playerCamera != null)
                {
                    Vector3 throwDirection = _playerCamera.transform.forward;
                    _heldRigidbody.AddForce(throwDirection * _dropForce, ForceMode.VelocityChange);
                }
            }

            _heldEntity = null;
            _heldRigidbody = null;
            _isHolding = false;
            _collidingColliders.Clear();
            _originalTriggerStates.Clear();

            UpdateButtonText();
        }

        /// <summary>
        /// Отменяет удержание и возвращает Entity на место
        /// </summary>
        public void Cancel()
        {
            if (!_isHolding || _heldEntity == null)
                return;

            RemoveCollisionDetectors();
            // Восстанавливаем коллайдеры
            SetCollidersAsTriggers(false);

            // Включаем физику обратно без броска
            if (_heldRigidbody != null)
            {
                _heldEntity.SetKinematicState(false);
                _heldRigidbody.useGravity = true;
            }

            _heldEntity = null;
            _heldRigidbody = null;
            _isHolding = false;
            _collidingColliders.Clear();
            _originalTriggerStates.Clear();

            UpdateButtonText();
        }

        private void UpdateButtonText()
        {
            string text = _isHolding ? _throwText : _takeText;

            if (_buttonTextTmp != null)
            {
                _buttonTextTmp.text = text;
            }
            else if (_buttonText != null)
            {
                _buttonText.text = text;
            }
        }

        private void UpdateHeldEntityPosition()
        {
            if (_heldEntity == null || _playerCamera == null)
                return;

            Vector3 cameraPosition = _playerCamera.transform.position;
            Vector3 cameraForward = _playerCamera.transform.forward;

            Bounds entityBounds = GetEntityBounds(_heldEntity);
            Vector3 closestPoint = entityBounds.ClosestPoint(cameraPosition);
            Vector3 offsetFromEntityCenter = closestPoint - _heldEntity.transform.position;
            float distanceToClosestPoint = Vector3.Dot(offsetFromEntityCenter, cameraForward);

            Vector3 targetPosition = cameraPosition + cameraForward * (_currentHoldDistance - distanceToClosestPoint);
            _heldEntity.transform.position = targetPosition;
        }

        private Bounds GetEntityBounds(Entity entity)
        {
            if (entity == null)
                return new Bounds();

            Renderer[] renderers = entity.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }

                return bounds;
            }

            return new Bounds(entity.transform.position, Vector3.one);
        }

        private void SetCollidersAsTriggers(bool isTrigger)
        {
            if (_heldEntity == null)
                return;

            Cube[] cubes = _heldEntity.Cubes;
            if (cubes == null || cubes.Length == 0)
                return;

            if (isTrigger)
            {
                _originalTriggerStates.Clear();
            }

            foreach (Cube cube in cubes)
            {
                if (cube == null) continue;

                Collider cubeCollider = cube.GetComponent<Collider>();
                if (cubeCollider != null)
                {
                    if (isTrigger)
                    {
                        _originalTriggerStates[cubeCollider] = cubeCollider.isTrigger;
                        cubeCollider.isTrigger = true;
                    }
                    else
                    {
                        if (_originalTriggerStates.TryGetValue(cubeCollider, out bool originalState))
                        {
                            cubeCollider.isTrigger = originalState;
                        }
                    }
                }
            }
        }

        private void AddCollisionDetectors()
        {
            if (_heldEntity == null)
                return;

            Cube[] cubes = _heldEntity.Cubes;
            if (cubes == null || cubes.Length == 0)
                return;

            _detectors.Clear();

            foreach (Cube cube in cubes)
            {
                if (cube == null) continue;

                Collider cubeCollider = cube.GetComponent<Collider>();
                if (cubeCollider != null)
                {
                    CollisionDetector detector = cube.GetComponent<CollisionDetector>();
                    if (detector == null)
                    {
                        detector = cube.gameObject.AddComponent<CollisionDetector>();
                    }

                    detector.Initialize(this);
                    _detectors.Add(detector);
                }
            }
        }

        private void RemoveCollisionDetectors()
        {
            foreach (CollisionDetector detector in _detectors)
            {
                if (detector != null)
                {
                    Destroy(detector);
                }
            }

            _detectors.Clear();
        }

        private void OnTriggerStay(Collider other)
        {
            if (!_isHolding || _heldEntity == null)
                return;

            // Игнорируем коллайдеры самого Entity
            Cube otherCube = other.GetComponent<Cube>();
            if (otherCube != null)
            {
                Entity otherEntity = otherCube.GetComponentInParent<Entity>();
                if (otherEntity == _heldEntity)
                    return;
            }

            // Также проверяем через transform.parent на случай, если это не куб
            if (other.transform.parent != null)
            {
                Entity parentEntity = other.transform.parent.GetComponent<Entity>();
                if (parentEntity == _heldEntity)
                    return;
            }

            // Проверяем, что объект на нужном слое
            int otherLayer = other.gameObject.layer;
            if ((_collisionLayerMask & (1 << otherLayer)) == 0)
                return;

            _collidingColliders.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_isHolding)
                return;

            _collidingColliders.Remove(other);
        }
    }
}

