using UnityEngine;

public class EntityHookManager : MonoBehaviour
{
    public void DetachAllHooks(Cube[] cubes)
    {
        if (cubes == null) return;

        foreach (var cube in cubes)
        {
            if (cube != null)
            {
                DetachHookFromCube(cube);
            }
        }
    }

    public void DetachHookFromCube(Cube cube)
    {
        if (cube == null) return;

        Hook hook = cube.GetComponentInChildren<Hook>();
        if (hook)
            hook.Detach();
    }
}

