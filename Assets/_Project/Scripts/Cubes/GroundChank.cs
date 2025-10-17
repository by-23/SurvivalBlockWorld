using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GroundChank : MonoBehaviour
{

    public GameObject[] _ChunkObjs = null;
    public Vector2 _NoiseScale = Vector2.one;
    public Vector2 _NoiseOffset = Vector2.zero;

    [Space]
    public int _HinghtOffset = 60;
    public float _HinghtIntensity = 5f;
    private int[,,] _TempData;

    private void Start()
    {
        _ChunkObjs = GetComponentsInChildren<GameObject>();

        //_TempData = new int[_ChunkObjs.]


    }
}
