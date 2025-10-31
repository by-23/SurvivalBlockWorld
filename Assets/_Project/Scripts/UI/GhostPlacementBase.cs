using UnityEngine;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Базовый класс для логики размещения объектов через ghost-превью.
    /// Содержит общую функциональность для CubeCreator и EntityCreator.
    /// </summary>
    public abstract class GhostPlacementBase : MonoBehaviour
    {
        [Header("Common Ghost Settings")] [SerializeField]
        protected LayerMask _groundLayerMask = 1;

        [SerializeField] protected float _ghostTransparency = 0.5f;
        [SerializeField] protected Material _ghostMaterial;

        protected GameObject _ghostRoot;
        protected bool _isGhostActive;
        private Camera _cachedCamera;

        /// <summary>
        /// Получает главную камеру игрока с fallback на любую доступную камеру.
        /// Кэширует результат для оптимизации.
        /// </summary>
        protected Camera GetPlayerCamera()
        {
            // Проверяем валидность кэша
            if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
            {
                _cachedCamera = Camera.main;
                if (_cachedCamera == null)
                {
                    _cachedCamera = FindAnyObjectByType<Camera>();
                }
            }

            return _cachedCamera;
        }

        /// <summary>
        /// Создает прозрачный материал для ghost объектов.
        /// </summary>
        protected Material CreateGhostMaterial(Color baseColor)
        {
            Material mat = _ghostMaterial != null
                ? new Material(_ghostMaterial)
                : new Material(Shader.Find("Standard"));

            if (_ghostMaterial == null)
            {
                mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, _ghostTransparency);
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

            return mat;
        }

        /// <summary>
        /// Устанавливает коллайдер как trigger для ghost объекта.
        /// </summary>
        protected void SetupGhostCollider(GameObject ghostObject)
        {
            Collider col = ghostObject.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        /// <summary>
        /// Базовый метод для проверки, находится ли позиция над землей.
        /// Может быть переопределен в наследниках для специфичной логики.
        /// </summary>
        protected virtual Vector3 EnsureAboveGround(Vector3 position, float offset = 0.5f)
        {
            Vector3 groundCheckPos = position;
            groundCheckPos.y -= offset;

            if (Physics.Raycast(groundCheckPos, Vector3.down, out RaycastHit hit, offset * 2f, _groundLayerMask))
            {
                float groundLevel = hit.point.y + offset;

                // Не изменяем позицию, если она уже достаточно высоко
                if (position.y > groundLevel + offset)
                {
                    return position;
                }

                position.y = groundLevel;
            }

            return position;
        }

        /// <summary>
        /// Базовые методы управления видимостью ghost.
        /// </summary>
        public virtual void HideGhost()
        {
            if (_ghostRoot != null)
            {
                _isGhostActive = false;
                _ghostRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Автоматически скрывает ghost при деактивации компонента (например, при переключении инструментов).
        /// </summary>
        private void OnDisable()
        {
            HideGhost();
        }

        public virtual void ShowGhost()
        {
            if (_ghostRoot != null)
            {
                _isGhostActive = true;
                _ghostRoot.SetActive(true);
                UpdateGhostMaterial(false);
            }
        }

        /// <summary>
        /// Обновляет материал ghost в зависимости от того, занята ли позиция.
        /// Должен быть реализован в наследниках.
        /// </summary>
        protected abstract void UpdateGhostMaterial(bool isOccupied);

        /// <summary>
        /// Получает позицию для размещения ghost объекта.
        /// Должен быть реализован в наследниках.
        /// </summary>
        protected abstract Vector3 GetGhostPosition();

        /// <summary>
        /// Проверяет, занята ли позиция другим объектом.
        /// Должен быть реализован в наследниках.
        /// </summary>
        protected abstract bool IsPositionOccupied(Vector3 position);

        /// <summary>
        /// Находит альтернативную позицию рядом с игроком, если текущая занята.
        /// Должен быть реализован в наследниках.
        /// </summary>
        protected abstract Vector3 FindAlternativePosition(Vector3 occupiedPosition);
    }
}

