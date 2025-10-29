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

        /// <summary>
        /// Shows the outline effect on the entity
        /// </summary>
        public void ShowOutline(Color? color = null, float? width = null, Outline.Mode mode = Outline.Mode.OutlineAll)
        {
            if (_isOutlineActive)
                return;

            _isOutlineActive = true;

            // Collect all child mesh filters and combine into one mesh (originals untouched)
            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters == null || meshFilters.Length == 0)
            {
                _isOutlineActive = false;
                return;
            }

            var combine = new List<CombineInstance>();
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
                combine.Add(ci);
            }

            if (combine.Count == 0)
            {
                _isOutlineActive = false;
                return;
            }

            var combinedMesh = new Mesh { name = "UnifiedOutlineMesh" };
            combinedMesh.CombineMeshes(combine.ToArray(), true, true);

            _unifiedOutlineObject = new GameObject("UnifiedOutline");
            _unifiedOutlineObject.transform.SetParent(transform, false);
            _unifiedOutlineObject.transform.localPosition = Vector3.zero;
            _unifiedOutlineObject.transform.localRotation = Quaternion.identity;
            _unifiedOutlineObject.transform.localScale = Vector3.one;

            var outlineMf = _unifiedOutlineObject.AddComponent<MeshFilter>();
            outlineMf.sharedMesh = combinedMesh;

            var outlineMr = _unifiedOutlineObject.AddComponent<MeshRenderer>();
            outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineMr.receiveShadows = false;
            outlineMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            outlineMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Add QuickOutline Outline component with parameters
            var outline = _unifiedOutlineObject.AddComponent<Outline>();
            outline.OutlineMode = mode;
            outline.OutlineColor = color.HasValue ? color.Value : _defaultOutlineColor;
            outline.OutlineWidth = width.HasValue ? width.Value : _defaultOutlineWidth;
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

            if (_unifiedOutlineObject != null)
            {
                Destroy(_unifiedOutlineObject);
                _unifiedOutlineObject = null;
            }
        }

        private void OnDestroy()
        {
            HideOutline();
        }
    }
}

