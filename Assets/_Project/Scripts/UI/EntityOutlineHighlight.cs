using UnityEngine;
using System.Collections.Generic;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Manages outline rendering for entities using scaled mesh for outline effect
    /// Creates child objects with larger scale and transparent materials
    /// </summary>
    [RequireComponent(typeof(Entity))]
    public class EntityOutlineHighlight : MonoBehaviour
    {
        [Header("Default Outline Settings")] [SerializeField]
        private Color _defaultOutlineColor = Color.yellow;

        [SerializeField] private float _defaultOutlineWidth = 5f;

        private bool _isOutlineActive;

        // Holder for unified combined mesh with outline
        private GameObject _unifiedOutlineObject;
        private Outline _outlineComponent;
        private Mesh _combinedMesh;
        private List<CombineInstance> _combineListCache = new List<CombineInstance>();

        /// <summary>
        /// Shows the outline effect on the entity
        /// </summary>
        public void ShowOutline(Color? color = null, float? width = null, Outline.Mode mode = Outline.Mode.OutlineAll)
        {
            if (_isOutlineActive)
            {
                // Обновляем параметры существующего outline без пересоздания
                if (_outlineComponent != null)
                {
                    _outlineComponent.OutlineMode = mode;
                    _outlineComponent.OutlineColor = color.HasValue ? color.Value : _defaultOutlineColor;
                    _outlineComponent.OutlineWidth = width.HasValue ? width.Value : _defaultOutlineWidth;
                }

                return;
            }

            _isOutlineActive = true;

            // Собираем все mesh filters и объединяем в один mesh
            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters == null || meshFilters.Length == 0)
            {
                _isOutlineActive = false;
                return;
            }

            _combineListCache.Clear();
            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;

                var ci = new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix
                };
                _combineListCache.Add(ci);
            }

            if (_combineListCache.Count == 0)
            {
                _isOutlineActive = false;
                return;
            }

            // Переиспользуем существующий mesh или создаем новый
            if (_combinedMesh == null)
            {
                _combinedMesh = new Mesh { name = "UnifiedOutlineMesh" };
            }

            _combinedMesh.CombineMeshes(_combineListCache.ToArray(), true, true);

            // Переиспользуем существующий GameObject или создаем новый
            if (_unifiedOutlineObject == null)
            {
                _unifiedOutlineObject = new GameObject("UnifiedOutline");
                _unifiedOutlineObject.transform.SetParent(transform, false);
                _unifiedOutlineObject.transform.localPosition = Vector3.zero;
                _unifiedOutlineObject.transform.localRotation = Quaternion.identity;
                _unifiedOutlineObject.transform.localScale = Vector3.one;

                var outlineMf = _unifiedOutlineObject.AddComponent<MeshFilter>();
                outlineMf.sharedMesh = _combinedMesh;

                var outlineMr = _unifiedOutlineObject.AddComponent<MeshRenderer>();
                outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineMr.receiveShadows = false;
                outlineMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                outlineMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                outlineMr.sharedMaterials = new Material[0];

                // Создаем Outline компонент только один раз
                _outlineComponent = _unifiedOutlineObject.AddComponent<Outline>();
            }
            else
            {
                // Активируем объект и обновляем mesh
                _unifiedOutlineObject.SetActive(true);
                var outlineMf = _unifiedOutlineObject.GetComponent<MeshFilter>();
                if (outlineMf != null)
                {
                    outlineMf.sharedMesh = _combinedMesh;
                }

                // Получаем компонент если он еще не кэширован
                if (_outlineComponent == null)
                {
                    _outlineComponent = _unifiedOutlineObject.GetComponent<Outline>();
                }
            }

            // Обновляем параметры outline
            if (_outlineComponent != null)
            {
                _outlineComponent.OutlineMode = mode;
                _outlineComponent.OutlineColor = color.HasValue ? color.Value : _defaultOutlineColor;
                _outlineComponent.OutlineWidth = width.HasValue ? width.Value : _defaultOutlineWidth;
            }
        }

        // No custom material needed; QuickOutline handles mask/fill passes internally

        /// <summary>
        /// Hides the outline effect
        /// </summary>
        public void HideOutline()
        {
            if (!_isOutlineActive)
                return;

            _isOutlineActive = false;

            // Отключаем объект вместо уничтожения для переиспользования
            if (_unifiedOutlineObject != null)
            {
                _unifiedOutlineObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            // Освобождаем ресурсы при уничтожении компонента
            if (_unifiedOutlineObject != null)
            {
                Destroy(_unifiedOutlineObject);
                _unifiedOutlineObject = null;
            }

            if (_combinedMesh != null)
            {
                Destroy(_combinedMesh);
                _combinedMesh = null;
            }

            _outlineComponent = null;
        }
    }
}

