using UnityEngine;

public class Bomb : MonoBehaviour
{
    [SerializeField] private float _explosionRadius = 1.5f;
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

            Explosion(hit.point);
        }
    }

    private void Explosion(Vector3 point)
    {
        var colliders = Physics.OverlapSphere(point, _explosionRadius);
        foreach (Collider collider in colliders)
        {
            if (collider.TryGetComponent(out Cube cube))
            {
                if (!cube.Detouched)
                {
                    cube.Detouch();

                    // Получаем или добавляем Rigidbody, так как Detouch может обрабатываться асинхронно
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
                else
                {
                    cube.Destroy();
                }
            }
        }
    }
}
