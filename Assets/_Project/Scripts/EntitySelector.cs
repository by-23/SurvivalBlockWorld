using UnityEngine;

public class EntitySelector : MonoBehaviour
{
    [Header("References")] [SerializeField]
    private Camera _playerCamera;

    [SerializeField] private float _raycastDistance = 200f;

    private EntityManager _EntityManager;
    private Entity _hoveredEntity;
    private bool _isHovering;

    private void Awake()
    {
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
        }

        _EntityManager = GetComponent<EntityManager>();
    }

    private void Update()
    {
        PerformRaycast();
    }

    private void PerformRaycast()
    {
        if (_playerCamera == null) return;

        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _playerCamera.ScreenPointToRay(screenCenterPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, _raycastDistance))
        {
            Entity hitEntity = hit.collider.GetComponentInParent<Entity>();

            if (hitEntity != null)
            {
                if (_hoveredEntity != hitEntity)
                {
                    OnEntityHover(hitEntity);
                }

                _isHovering = true;
                return;
            }
        }

        if (_isHovering)
        {
            OnEntityUnhover();
        }
    }

    private void OnEntityHover(Entity entity)
    {
        _hoveredEntity = entity;
        _isHovering = true;

        if (_EntityManager) _EntityManager.SaveTakeButtonActive(true);
    }

    private void OnEntityUnhover()
    {
        _hoveredEntity = null;
        _isHovering = false;

        if (_EntityManager) _EntityManager.SaveTakeButtonActive(false);
    }

    public Entity GetHoveredEntity()
    {
        return _hoveredEntity;
    }

    public bool IsHovering()
    {
        return _isHovering;
    }
}
