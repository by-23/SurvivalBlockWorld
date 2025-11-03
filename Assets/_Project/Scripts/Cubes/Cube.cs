using UnityEngine;

[RequireComponent(typeof(MeshFilter)), RequireComponent(typeof(MeshRenderer))]
public class Cube : MonoBehaviour
{
    private bool _detouched;

    public int Id { get; set; }
    public bool Detouched => _detouched;
    public byte BlockTypeID = 0;

    private Entity _entity;
    [SerializeField] private MeshFilter _meshFilter; // Ссылка на MeshFilter
    [SerializeField] private MeshRenderer _meshRenderer; // Ссылка на MeshRenderer
    [SerializeField] private ColorCube _colorCube; // Ссылка на ColorCube
    private Rigidbody _rigidbody;
    private Collider _collider;

    public MeshFilter MeshFilter => _meshFilter;
    public MeshRenderer MeshRenderer => _meshRenderer;
    public ColorCube ColorCube => _colorCube;
    public Color Color => _colorCube ? _colorCube.GetColor32() : Color.white;

    private void Awake()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        if (_colorCube == null) _colorCube = GetComponent<ColorCube>();
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        if (_colorCube == null) _colorCube = GetComponent<ColorCube>();
    }
#endif

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

    public CubeData GetSaveData(Vector3 entityWorldPosition, int entityId)
    {
        Vector3 worldPos = transform.position;
        Color32 color = _colorCube ? _colorCube.GetColor32() : new Color32(255, 255, 255, 255);
        Quaternion rotation = transform.rotation;
        return new CubeData(worldPos, color, BlockTypeID, entityId, rotation);
    }
}