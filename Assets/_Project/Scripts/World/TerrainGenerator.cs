using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class TerrainGenerator : MonoBehaviour
{
    [Header("Настройки")]
    [Tooltip("Включить для автоматической генерации при изменении параметров. Может быть ресурсоемко.")]
    [SerializeField]
    private bool _realtimeUpdate = false;

    public bool RealtimeUpdate => _realtimeUpdate;

    [Header("Размеры")] [SerializeField] private int _width = 50;
    [SerializeField] private int _depth = 50;

    [Header("Шум для Цвета")] [SerializeField]
    private float _colorNoiseScale = 0.1f;

    [SerializeField] private float _colorOffsetX = 100f;
    [SerializeField] private float _colorOffsetZ = 100f;

    [Header("Цвета")] [SerializeField] private ColorData[] _colors;

    [Header("Материал")] [SerializeField] private Material _baseCubeMaterial;

    private GameObject _generatedTerrain;


#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_realtimeUpdate && Application.isEditor)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null && gameObject != null)
                {
                    Generate();
                }
            };
        }
    }
#endif

    [System.Serializable]
    public class ColorData
    {
        public Color Color;
        [Range(0, 100)] public float Percentage;
    }

    public void Generate()
    {
        Clear();

        if (_baseCubeMaterial == null)
        {
            Debug.LogError("Не назначен базовый материал для куба (_baseCubeMaterial)!");
            return;
        }

        GameObject terrainObject = new GameObject("GeneratedTerrain");
        _generatedTerrain = terrainObject;

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer != -1)
        {
            terrainObject.layer = groundLayer;
        }
        else
        {
            Debug.LogWarning(
                "Слой 'Ground' не найден. Пожалуйста, создайте его в Project Settings -> Tags and Layers.");
        }

        Rigidbody rb = terrainObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        GameObject cubesHolder = new GameObject("~CubesHolder");
        cubesHolder.transform.SetParent(terrainObject.transform, false);

        var colorMaterials = new Dictionary<Color, Material>();
        var colorThresholds = PrepareColorThresholds();

        for (int x = 0; x < _width; x++)
        {
            for (int z = 0; z < _depth; z++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(cubesHolder.transform);
                cube.transform.localPosition = new Vector3(x, 0, z);

                DestroyImmediate(cube.GetComponent<BoxCollider>());

                Color selectedColor = GetColorFromNoise(x, z, colorThresholds);

                Material materialInstance;
                if (!colorMaterials.TryGetValue(selectedColor, out materialInstance))
                {
                    materialInstance = new Material(_baseCubeMaterial) { color = selectedColor };
                    colorMaterials.Add(selectedColor, materialInstance);
                }

                cube.GetComponent<MeshRenderer>().sharedMaterial = materialInstance;
            }
        }

        CombineMeshes(terrainObject, cubesHolder, groundLayer);

        DestroyImmediate(cubesHolder);

        BoxCollider boxCollider = terrainObject.AddComponent<BoxCollider>();
        boxCollider.size = new Vector3(_width, 1, _depth);
        boxCollider.center = new Vector3((_width - 1) / 2f, 0, (_depth - 1) / 2f);
    }

    private List<KeyValuePair<float, Color>> PrepareColorThresholds()
    {
        var thresholds = new List<KeyValuePair<float, Color>>();
        if (_colors == null || _colors.Length == 0) return thresholds;

        float totalPercentage = _colors.Sum(c => c.Percentage);
        if (totalPercentage <= 0) return thresholds;

        float cumulativePercentage = 0f;
        foreach (var colorData in _colors.OrderBy(c => c.Percentage))
        {
            if (colorData.Percentage > 0)
            {
                cumulativePercentage += colorData.Percentage / totalPercentage;
                thresholds.Add(new KeyValuePair<float, Color>(cumulativePercentage, colorData.Color));
            }
        }

        return thresholds;
    }

    private Color GetColorFromNoise(int x, int z, List<KeyValuePair<float, Color>> thresholds)
    {
        if (thresholds == null || thresholds.Count == 0) return Color.white;

        float noiseValue =
            Mathf.PerlinNoise((x + _colorOffsetX) * _colorNoiseScale, (z + _colorOffsetZ) * _colorNoiseScale);

        foreach (var threshold in thresholds)
        {
            if (noiseValue <= threshold.Key)
            {
                return threshold.Value;
            }
        }

        return thresholds.Last().Value;
    }

    private void CombineMeshes(GameObject terrainObject, GameObject cubesHolder, int layer)
    {
        var materialGroups = cubesHolder.GetComponentsInChildren<MeshFilter>()
            .GroupBy(mf => mf.GetComponent<MeshRenderer>().sharedMaterial);

        foreach (var group in materialGroups)
        {
            Material material = group.Key;
            List<MeshFilter> filters = group.ToList();

            var combineInstances = new List<CombineInstance>(filters.Count);
            foreach (var filter in filters)
            {
                combineInstances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = filter.transform.localToWorldMatrix
                });
            }

            GameObject combinedObject = new GameObject("CombinedMesh_" + material.color.ToString());
            combinedObject.transform.SetParent(terrainObject.transform, false);
            if (layer != -1) combinedObject.layer = layer;

            var combinedMeshFilter = combinedObject.AddComponent<MeshFilter>();
            var newMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            newMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMeshFilter.sharedMesh = newMesh;

            var combinedRenderer = combinedObject.AddComponent<MeshRenderer>();
            combinedRenderer.sharedMaterial = material;
        }
    }

    public void Clear()
    {
        if (_generatedTerrain != null)
        {
            DestroyImmediate(_generatedTerrain);
            return;
        }

        GameObject oldTerrain = GameObject.Find("GeneratedTerrain");
        if (oldTerrain != null)
        {
            DestroyImmediate(oldTerrain);
        }

        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
    }
}
