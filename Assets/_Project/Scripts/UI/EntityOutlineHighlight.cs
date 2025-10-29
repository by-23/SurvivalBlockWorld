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
        [Header("Outline Settings")] [SerializeField]
        private Color _outlineColor = Color.yellow;

        [SerializeField] private float _outlineWidth = 0.02f;

        private bool _isOutlineActive;

        // Store created outline objects
        private List<GameObject> _outlineObjects = new List<GameObject>();

        /// <summary>
        /// Shows the outline effect on the entity
        /// </summary>
        public void ShowOutline()
        {
            if (_isOutlineActive)
                return;

            _isOutlineActive = true;

            // Find all mesh renderers in the entity
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();

            foreach (MeshRenderer meshRenderer in renderers)
            {
                if (meshRenderer == null) continue;

                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;

                // Create outline object
                GameObject outlineObject = new GameObject("Outline");
                outlineObject.transform.SetParent(meshRenderer.transform);
                outlineObject.transform.localPosition = Vector3.zero;
                outlineObject.transform.localRotation = Quaternion.identity;

                // Scale up slightly for outline effect
                outlineObject.transform.localScale = Vector3.one * (1f + _outlineWidth);

                // Add mesh filter with same mesh
                MeshFilter outlineMeshFilter = outlineObject.AddComponent<MeshFilter>();
                outlineMeshFilter.sharedMesh = meshFilter.sharedMesh;

                // Add mesh renderer with transparent material
                MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();

                // Create outline material with Cull Front
                Material outlineMat = CreateOutlineMaterial();
                if (outlineMat != null)
                {
                    outlineMat.color = _outlineColor;
                    outlineRenderer.material = outlineMat;
                }
                else
                {
                    Debug.LogWarning("Failed to create outline material");
                    continue;
                }

                // Configure renderer
                outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineRenderer.receiveShadows = false;
                outlineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                outlineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                _outlineObjects.Add(outlineObject);
            }
        }

        /// <summary>
        /// Creates an outline material with Cull Front for outline effect
        /// </summary>
        private Material CreateOutlineMaterial()
        {
            // Use URP Unlit shader for better outline effect
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                Debug.LogError("Could not find Unlit shader for outline material!");
                return null;
            }

            Material mat = new Material(shader);

            // Cull front faces to show only the outline (the part not visible normally)
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);

            // ZTest greater to render only when behind original geometry
            // This creates the outline effect
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

            return mat;
        }

        /// <summary>
        /// Hides the outline effect
        /// </summary>
        public void HideOutline()
        {
            if (!_isOutlineActive)
                return;

            _isOutlineActive = false;

            // Destroy all outline objects
            foreach (GameObject outlineObj in _outlineObjects)
            {
                if (outlineObj != null)
                {
                    Destroy(outlineObj);
                }
            }

            _outlineObjects.Clear();
        }

        private void OnDestroy()
        {
            HideOutline();
        }
    }
}

