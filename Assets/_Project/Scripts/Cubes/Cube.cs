using UnityEngine;

public class Cube : MonoBehaviour
{
    private bool _detouched;

    public int Id { get; set; }
    public bool Detouched => _detouched;
    public byte BlockTypeID = 0;

    private Entity _entity;
    private ColorCube _colorCube;
    private Rigidbody _rigidbody;
    private Collider _collider;

    private void Awake()
    {
        _colorCube = GetComponent<ColorCube>();
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
    }

    public void SetEntity(Entity entity)
    {
        _entity = entity;
    }

    [ContextMenu("Detouch cube")]
    public void Detouch()
    {
        if (_detouched)
            return;

        _detouched = true;

        if (_entity)
            _entity.DetouchCube(this);

        if (_colorCube)
            _colorCube.ApplyDetouchColor();
    }

    public void Destroy()
    {
        Detouch();

        if (_rigidbody)
            _rigidbody.isKinematic = true;

        if (_collider)
            _collider.enabled = false;

        Destroy(gameObject);
    }

    public CubeData GetSaveData(Vector3 entityWorldPosition)
    {
        Vector3 worldPos = transform.position;
        Color32 color = _colorCube ? _colorCube.GetColor32() : new Color32(255, 255, 255, 255);
        return new CubeData(worldPos, color, BlockTypeID);
    }
}