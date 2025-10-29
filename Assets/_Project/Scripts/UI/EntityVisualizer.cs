using UnityEngine;
using AYellowpaper.SerializedCollections;
using UnityEngine.UI;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Handles visual feedback for tool interactions
    /// Manages raycasting and entity highlighting when specific tools are active
    /// </summary>
    public class EntityVisualizer : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        private Camera _playerCamera;

        [SerializeField] private float _raycastDistance = 200f;

        [Header("Tool Configuration")] [SerializeField]
        private string _activeToolName = "SaveSpawn";

        [Header("Outline Settings")] [SerializeField]
        private Color _outlineColor = Color.blue;

        [SerializeField] private float _outlineWidth = 5f;
        [SerializeField] private Outline.Mode _outlineMode = Outline.Mode.OutlineAll;

        private Entity _currentlyHighlightedEntity;
        private bool _isVisualizerActive = false;

        private void Awake()
        {
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!_isVisualizerActive)
                return;

            PerformVisualizationRaycast();
        }

        private void PerformVisualizationRaycast()
        {
            if (_playerCamera == null) return;

            Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Ray ray = _playerCamera.ScreenPointToRay(screenCenterPoint);

            if (Physics.Raycast(ray, out RaycastHit hit, _raycastDistance))
            {
                Entity hitEntity = hit.collider.GetComponentInParent<Entity>();

                if (hitEntity != null)
                {
                    if (_currentlyHighlightedEntity != hitEntity)
                    {
                        OnEntityHit(hitEntity);
                    }

                    return;
                }
            }

            // Ray didn't hit an entity or hit something else
            if (_currentlyHighlightedEntity != null)
            {
                OnEntityUnhit();
            }
        }

        private void OnEntityHit(Entity entity)
        {
            // Remove highlight from previous entity
            if (_currentlyHighlightedEntity != null && _currentlyHighlightedEntity != entity)
            {
                RemoveHighlight(_currentlyHighlightedEntity);
            }

            _currentlyHighlightedEntity = entity;
            ApplyHighlight(entity);
        }

        private void OnEntityUnhit()
        {
            if (_currentlyHighlightedEntity != null)
            {
                RemoveHighlight(_currentlyHighlightedEntity);
                _currentlyHighlightedEntity = null;
            }
        }

        private void ApplyHighlight(Entity entity)
        {
            if (entity == null) return;

            EntityOutlineHighlight outline = entity.GetComponent<EntityOutlineHighlight>();

            if (outline == null)
            {
                outline = entity.gameObject.AddComponent<EntityOutlineHighlight>();
            }

            outline.ShowOutline(_outlineColor, _outlineWidth, _outlineMode);
        }

        private void RemoveHighlight(Entity entity)
        {
            if (entity == null) return;

            EntityOutlineHighlight outline = entity.GetComponent<EntityOutlineHighlight>();
            if (outline != null)
            {
                outline.HideOutline();
            }
        }

        /// <summary>
        /// Activates the visualizer for a specific tool
        /// </summary>
        /// <param name="toolName">Name of the tool to activate for</param>
        public void ActivateForTool(string toolName)
        {
            _activeToolName = toolName;
            _isVisualizerActive = true;
        }

        /// <summary>
        /// Deactivates the visualizer
        /// </summary>
        public void Deactivate()
        {
            _isVisualizerActive = false;

            // Remove any active highlights
            if (_currentlyHighlightedEntity != null)
            {
                RemoveHighlight(_currentlyHighlightedEntity);
                _currentlyHighlightedEntity = null;
            }
        }

        /// <summary>
        /// Sets the camera reference for raycasting
        /// </summary>
        public void SetCamera(Camera camera)
        {
            _playerCamera = camera;
        }
    }
}

