using UnityEngine;

public class Laser : MonoBehaviour
{
    public bool _Press;

    [SerializeField] Camera _camera;


    private void LateUpdate()
    {
        if (_Press)
        {
            Raycast();
        }
    }

    public void Press(bool _press)
    { _Press = _press; }

    public void Raycast()
    {
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 200))
        {
            if (hit.collider.tag == "Ground")
                Destroy(hit.collider.gameObject);

            if (hit.collider.TryGetComponent(out Cube cube))
            {
                cube.Destroy();
            }
        }
    }
}