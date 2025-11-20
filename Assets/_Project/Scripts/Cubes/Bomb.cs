using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Bomb : MonoBehaviour
{
    [SerializeField] private float _explosionRadius = 1.5f;
    [SerializeField] private int _maxCubesPerFrame = 10;
    [SerializeField] Camera _camera;


    private void Update()
    {
        if (InputManager.Instance._TOUCH) return;

        if (Input.GetKeyDown(KeyCode.T))
        {
            Raycast();
        }
    }

    public void Raycast()
    {
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 200))
        {
            if (hit.collider.TryGetComponent(out Cube cube))
            {
                cube.Destroy();
            }

            StartCoroutine(ExplosionCoroutine(hit.point));
        }
    }

    private IEnumerator ExplosionCoroutine(Vector3 point)
    {
        var colliders = Physics.OverlapSphere(point, _explosionRadius);
        List<Cube> cubesToProcess = new List<Cube>();

        foreach (Collider hitCollider in colliders)
        {
            if (hitCollider.TryGetComponent(out Cube cube))
            {
                if (!cube.Detouched)
                {
                    cubesToProcess.Add(cube);
                }
                else
                {
                    cube.Destroy();
                }
            }
        }

        int processed = 0;
        while (processed < cubesToProcess.Count)
        {
            int batchSize = Mathf.Min(_maxCubesPerFrame, cubesToProcess.Count - processed);

            for (int i = 0; i < batchSize; i++)
            {
                Cube cube = cubesToProcess[processed + i];
                if (cube == null || cube.Detouched) continue;

                cube.Detouch();

                Rigidbody rb = cube.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = cube.gameObject.AddComponent<Rigidbody>();
                    rb.mass = 1f;
                    rb.drag = 0.5f;
                    rb.angularDrag = 0.5f;
                }

                rb.AddExplosionForce(1000f, point, _explosionRadius);
            }

            processed += batchSize;

            yield return new WaitForFixedUpdate();
        }
    }
}
