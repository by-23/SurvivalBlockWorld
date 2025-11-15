using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Утилитный класс для управления покраской материалов Entity при пересечениях
    /// </summary>
    public class EntityMaterialColorizer
    {
        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();

        private readonly Dictionary<Renderer, MaterialPropertyBlock> _originalPropertyBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private MaterialPropertyBlock _mpb;
        private Material _redMaterial;
        private float _transparency;

        public EntityMaterialColorizer(Material redMaterial, float transparency)
        {
            _redMaterial = redMaterial;
            _transparency = transparency;
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Инициализирует colorizer для Entity, собирая все рендереры
        /// </summary>
        public void Initialize(Entity entity, bool excludeCubes = false)
        {
            Clear();
            if (entity == null) return;

            var allRenderers = entity.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null) continue;

                if (excludeCubes && r.GetComponent<Cube>() != null)
                    continue;

                _renderers.Add(r);
            }

            CacheOriginalMaterials();
        }

        /// <summary>
        /// Инициализирует colorizer с готовым списком рендереров
        /// </summary>
        public void Initialize(List<Renderer> renderers)
        {
            Clear();
            if (renderers == null) return;

            _renderers.AddRange(renderers);
            CacheOriginalMaterials();
        }

        private void CacheOriginalMaterials()
        {
            _originalMaterials.Clear();
            _originalPropertyBlocks.Clear();

            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                _originalMaterials[r] = new Material[r.sharedMaterials.Length];
                for (int j = 0; j < r.sharedMaterials.Length; j++)
                {
                    _originalMaterials[r][j] = r.sharedMaterials[j];
                }

                var savedBlock = new MaterialPropertyBlock();
                r.GetPropertyBlock(savedBlock);
                _originalPropertyBlocks[r] = savedBlock;
            }
        }

        /// <summary>
        /// Обновляет цвет материалов в зависимости от состояния пересечений
        /// </summary>
        public void UpdateColor(bool hasCollision)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                if (!hasCollision)
                {
                    RestoreRenderer(r);
                }
                else
                {
                    ApplyRedColor(r);
                }
            }
        }

        private void RestoreRenderer(Renderer r)
        {
            if (_originalMaterials.TryGetValue(r, out var originalMats))
            {
                r.sharedMaterials = originalMats;
            }

            if (_originalPropertyBlocks.TryGetValue(r, out var originalBlock))
            {
                r.SetPropertyBlock(originalBlock);
            }
            else
            {
                _mpb.Clear();
                r.SetPropertyBlock(_mpb);
            }
        }

        private void ApplyRedColor(Renderer r)
        {
            Material targetMaterial = _redMaterial;

            if (targetMaterial == null)
            {
                targetMaterial = CreateRedMaterial();
            }

            Material[] ghostMats = new Material[r.sharedMaterials.Length];
            for (int j = 0; j < ghostMats.Length; j++)
            {
                ghostMats[j] = targetMaterial;
            }

            r.sharedMaterials = ghostMats;

            Color c = new Color(1f, 0f, 0f, _transparency);
            _mpb.Clear();
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }

        private Material CreateRedMaterial()
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            Color ghostColor = new Color(1f, 0f, 0f, _transparency);
            mat.color = ghostColor;

            return mat;
        }

        /// <summary>
        /// Восстанавливает оригинальные материалы
        /// </summary>
        public void Restore()
        {
            foreach (var kvp in _originalMaterials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                    if (_originalPropertyBlocks.TryGetValue(kvp.Key, out var block))
                    {
                        kvp.Key.SetPropertyBlock(block);
                    }
                    else
                    {
                        _mpb.Clear();
                        kvp.Key.SetPropertyBlock(_mpb);
                    }
                }
            }
        }

        /// <summary>
        /// Очищает все данные
        /// </summary>
        public void Clear()
        {
            _renderers.Clear();
            _originalMaterials.Clear();
            _originalPropertyBlocks.Clear();
        }
    }
}

