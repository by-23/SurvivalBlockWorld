using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Entity))]
public class EntityMeshCombiner : MonoBehaviour
{
    private GameObject _combinedMeshObject;
    private Cube[] _cubes;
    private Rigidbody _rb;
    private bool _isKinematicOriginalState;
    private bool _isCombined = false;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void CombineMeshes()
    {
        if (_isCombined) return;

        _cubes = GetComponentsInChildren<Cube>();
        if (_cubes.Length == 0) return;

        if (_rb != null)
        {
            _isKinematicOriginalState = _rb.isKinematic;
            _rb.isKinematic = true;
        }

        var cubesByColor = new Dictionary<Color, List<CombineInstance>>();
        Material sourceMaterial = null;

        foreach (var cube in _cubes)
        {
            var meshFilter = cube.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;

            var renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (sourceMaterial == null)
                {
                    sourceMaterial = renderer.sharedMaterial;
                }
                renderer.enabled = false;
            }

            var colorCube = cube.GetComponent<ColorCube>();
            Color cubeColor = colorCube != null ? colorCube.GetColor32() : Color.white;

            if (!cubesByColor.ContainsKey(cubeColor))
            {
                cubesByColor[cubeColor] = new List<CombineInstance>();
            }

            var ci = new CombineInstance
            {
                mesh = meshFilter.sharedMesh,
                transform = transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix
            };
            cubesByColor[cubeColor].Add(ci);
        }

        if (sourceMaterial == null)
        {
            ShowCubes();
            if (_rb != null) _rb.isKinematic = _isKinematicOriginalState;
            _isCombined = false;
            return;
        }

        _combinedMeshObject = new GameObject("CombinedMesh");
        _combinedMeshObject.transform.SetParent(transform, false);

        var finalMesh = new Mesh();
        var finalCombine = new List<CombineInstance>();
        var finalMaterials = new List<Material>();

        foreach (var colorGroup in cubesByColor)
        {
            var color = colorGroup.Key;
            var instances = colorGroup.Value;

            var subMesh = new Mesh();
            subMesh.CombineMeshes(instances.ToArray(), true, true);

            var combine = new CombineInstance
            {
                mesh = subMesh,
                transform = Matrix4x4.identity
            };
            finalCombine.Add(combine);

            var material = new Material(sourceMaterial);
            material.SetColor("_BaseColor", color);
            finalMaterials.Add(material);
        }

        finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        finalMesh.CombineMeshes(finalCombine.ToArray(), false, false);

        var meshFilterFinal = _combinedMeshObject.AddComponent<MeshFilter>();
        meshFilterFinal.sharedMesh = finalMesh;

        var meshRendererFinal = _combinedMeshObject.AddComponent<MeshRenderer>();
        meshRendererFinal.sharedMaterials = finalMaterials.ToArray();

        _isCombined = true;
    }

    public void ShowCubes()
    {
        if (!_isCombined) return;

        if (_cubes != null)
        {
            foreach (var cube in _cubes)
            {
                if (cube != null)
                {
                    var renderer = cube.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }
            }
        }

        if (_combinedMeshObject != null)
        {
            Destroy(_combinedMeshObject);
        }

        if (_rb != null)
        {
            _rb.isKinematic = _isKinematicOriginalState;
        }

        _isCombined = false;
    }
}