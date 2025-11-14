using UnityEngine;

public class ColorCube : MonoBehaviour
{
    public bool _gradient, _remove;
    public Color _color = Color.white;

    Color _gradColor;
    private Color32 _quantizedColor;
    private MaterialPropertyBlock _propertyBlock;

    private const int ColorQuantizationStep = 8;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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

            _quantizedColor = QuantizeColor(_gradColor);
            ApplyColor(_quantizedColor);
        }
        else
        {
            _quantizedColor = QuantizeColor(_color);
            ApplyColor(_quantizedColor);
        }

        // Обновляем кэш в Cube после установки цвета
        var cube = GetComponent<Cube>();
        if (cube != null)
        {
            cube.RefreshCache();
        }

        if (_remove)
            Destroy(this);
    }

    private void OnValidate()
    {
        if (_gradient)
        {
            _gradColor = _color * Random.Range(0.8f, 0.9f);

            _quantizedColor = QuantizeColor(_gradColor);
            ApplyColor(_quantizedColor);
        }
        else
        {
            _quantizedColor = QuantizeColor(_color);
            ApplyColor(_quantizedColor);
        }
    }

    private void ApplyColor(Color32 color)
    {
        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        // Снижаем количество уникальных draw call через квантизацию цвета
        _propertyBlock.Clear();
        Color asColor = color;
        _propertyBlock.SetColor(BaseColorId, asColor);
        _propertyBlock.SetColor(ColorId, asColor);
        meshRenderer.SetPropertyBlock(_propertyBlock);
    }

    public void ApplyDetouchColor()
    {
        Color detouchColor = ((Color)_quantizedColor) * 0.8f;
        _quantizedColor = QuantizeColor(detouchColor);
        ApplyColor(_quantizedColor);

        var cube = GetComponent<Cube>();
        if (cube != null)
        {
            cube.RefreshCache();
        }
    }

    public Color32 GetColor32()
    {
        if (_quantizedColor.a == 0)
        {
            var source = _gradient ? _gradColor : _color;
            _quantizedColor = QuantizeColor(source);
        }

        return _quantizedColor;
    }

    private static Color32 QuantizeColor(Color color)
    {
        return new Color32(
            QuantizeChannel(color.r),
            QuantizeChannel(color.g),
            QuantizeChannel(color.b),
            255);
    }

    private static byte QuantizeChannel(float value)
    {
        int channel = Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        int quantized = ((channel + ColorQuantizationStep / 2) / ColorQuantizationStep) * ColorQuantizationStep;
        if (quantized > 255) quantized = 255;
        return (byte)quantized;
    }
}