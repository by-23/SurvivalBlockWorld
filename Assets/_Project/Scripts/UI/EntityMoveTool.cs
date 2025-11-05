using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Инструмент для перемещения entity. Превращает выбранный entity в ghost для перемещения.
    /// Использует UI кнопку для взятия и установки entity.
    /// </summary>
    public class EntityMoveTool : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        private EntityManager _entityManager;

        [SerializeField] private GhostEntityPlacer _ghostPlacer;
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Button _moveButton;
        [SerializeField] private EntityVisualizer _entityVisualizer;

        private void Awake()
        {
            if (_entityManager == null)
            {
                _entityManager = FindFirstObjectByType<EntityManager>();
            }

            if (_ghostPlacer == null)
            {
                _ghostPlacer = FindFirstObjectByType<GhostEntityPlacer>();
            }

            if (_playerCamera == null)
            {
                _playerCamera = Camera.main;
                if (_playerCamera == null && Player.Instance != null && Player.Instance._playerCamera != null)
                {
                    _playerCamera = Player.Instance._playerCamera.GetComponent<Camera>();
                }
            }

            if (_entityVisualizer == null)
            {
                _entityVisualizer = FindFirstObjectByType<EntityVisualizer>();
            }
        }

        private void OnEnable()
        {
            if (_moveButton != null)
            {
                _moveButton.onClick.RemoveAllListeners();
                _moveButton.onClick.AddListener(OnMoveButtonPressed);
            }

            // Визуализатор активируется через UIManager при переключении инструментов
            // Здесь не нужно активировать, чтобы избежать конфликтов
        }

        private void OnDisable()
        {
            if (_moveButton != null)
            {
                _moveButton.onClick.RemoveAllListeners();
            }

            // Визуализатор деактивируется через UIManager при переключении инструментов
        }

        private void OnMoveButtonPressed()
        {
            if (!gameObject.activeInHierarchy)
                return;

            if (_entityManager == null)
            {
                Debug.LogWarning("EntityMoveTool: EntityManager не найден");
                return;
            }

            // Если ghost активен - подтверждаем размещение
            if (_entityManager.IsGhostActive())
            {
                _entityManager.ConfirmGhost();
                return;
            }

            // Если ghost не активен - начинаем перемещение (берем entity)
            TryMoveEntity();
        }

        private void TryMoveEntity()
        {
            if (_entityManager == null || _ghostPlacer == null || _playerCamera == null)
            {
                Debug.LogWarning("EntityMoveTool: не найдены необходимые компоненты");
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

            // Отменяем предыдущий ghost если есть
            if (_entityManager.IsGhostActive())
            {
                _entityManager.CancelGhost();
            }

            // Превращаем entity в ghost для перемещения
            _ghostPlacer.Begin(targetEntity, _playerCamera);
            Debug.Log($"EntityMoveTool: начато перемещение entity {targetEntity.gameObject.name}");
        }
    }
}

