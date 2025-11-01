using UnityEngine;

public class Laser : MonoBehaviour
{
    public bool _Press;

    [SerializeField] Camera _camera;
    [SerializeField] LayerMask _cubeLayerMask = -1; // Маска слоев для проверки кубов


    private void LateUpdate()
    {
        if (_Press)
        {
            Raycast();
        }
    }

    public void Press(bool _press)
    {
        if (_Press != _press)
        {
            _Press = _press;
        }
    }

    public void Raycast()
    {
        if (_camera == null)
        {
            return;
        }

        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, 200, _cubeLayerMask))
        {
            if (hit.collider.TryGetComponent(out Cube cube))
            {
                cube.Destroy();
                return;
            }

            Entity entity = hit.collider.GetComponentInParent<Entity>();
            if (entity != null)
            {
                EntityMeshCombiner meshCombiner = entity.GetComponent<EntityMeshCombiner>();
                if (meshCombiner != null && meshCombiner.IsCombined)
                {
                    meshCombiner.ShowCubes();
                }

                Cube[] cubes = entity.GetComponentsInChildren<Cube>();
                if (cubes != null && cubes.Length > 0)
                {
                    Cube closestCube = null;
                    float closestDistance = float.MaxValue;

                    foreach (Cube c in cubes)
                    {
                        if (c == null || c.Detouched) continue;

                        float distance = Vector3.Distance(hit.point, c.transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestCube = c;
                        }
                    }

                    if (closestCube != null)
                    {
                        closestCube.Destroy();
                    }
                }
            }
        }
    }
}