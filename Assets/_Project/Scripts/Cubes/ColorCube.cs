using UnityEngine;

public class ColorCube : MonoBehaviour
{
    public bool _gradient, _remove;
    public Color _color = Color.white;

    Color _gradColor;

    private void Awake()
    {
        Setup(_color);
    }

    public void Setup(Color color)
    {
        _color = color;

        if (_gradient)
        {
            _gradColor = _color * Random.Range(0.8f, 0.9f);

            ApplyColor(_gradColor);
        }
        else
        {
            ApplyColor(_color);
        }

        if (_remove)
            Destroy(this);
    }

    private void OnValidate()
    {
        if (_gradient)
        {
            _gradColor = _color * Random.Range(0.8f, 0.9f);

            ApplyColor(_gradColor);
        }
        else
        {
            ApplyColor(_color);
        }
    }

    private void ApplyColor(Color color)
    {
        var renderer = GetComponent<MeshRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    public void ApplyDetouchColor()
    {
        ApplyColor(_color * 0.8f);
    }

    public Color32 GetColor32()
    {
        if (_gradient)
            return _gradColor;
        return _color;
    }
}