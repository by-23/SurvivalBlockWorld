using UnityEngine;

[RequireComponent(typeof(MeshFilter)), RequireComponent(typeof(MeshRenderer))]
public class Cube : MonoBehaviour
{
    private bool _detouched;

    public int Id { get; set; }
    public bool Detouched => _detouched;
    public byte BlockTypeID = 0;

    private Entity _entity;
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private ColorCube _colorCube;
    private Rigidbody _rigidbody;
    private Collider _collider;

    private Mesh _cachedMesh;
    private Color32 _cachedColor32;
    private bool _cacheInitialized;

    public MeshFilter MeshFilter => _meshFilter;
    public MeshRenderer MeshRenderer => _meshRenderer;
    public ColorCube ColorCube => _colorCube;
    public Color Color => _colorCube ? _colorCube.GetColor32() : Color.white;

    public Mesh CachedMesh
    {
        get
        {
            if (!_cacheInitialized)
                InitializeCache();
            return _cachedMesh;
        }
    }

    public Color32 CachedColor32
    {
        get
        {
            if (!_cacheInitialized)
                InitializeCache();
            return _cachedColor32;
        }
    }

    public MeshFilter DirectMeshFilter => _meshFilter;
    public MeshRenderer DirectMeshRenderer => _meshRenderer;

    private void Awake()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        if (_colorCube == null) _colorCube = GetComponent<ColorCube>();
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        InitializeCache();
    }

    private void InitializeCache()
    {
        if (_cacheInitialized) return;

        _cachedMesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
        _cachedColor32 = _colorCube != null ? _colorCube.GetColor32() : new Color32(255, 255, 255, 255);
        _cacheInitialized = true;
    }

    public void RefreshCache()
    {
        _cacheInitialized = false;
        InitializeCache();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        if (_colorCube == null) _colorCube = GetComponent<ColorCube>();

        if (Application.isPlaying)
            RefreshCache();
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
            _entity.DetachCube(this);

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