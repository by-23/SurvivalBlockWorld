using UnityEngine;
using System.Collections.Generic;

public class EntityHookManager : MonoBehaviour
{
    // Кэшируем найденные Hook по инстансу Cube, чтобы не вызывать поиск в иерархии каждый раз
    private readonly Dictionary<int, Hook> _cubeIdToHook = new Dictionary<int, Hook>();

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

        int cubeId = cube.GetInstanceID();
        Hook hook;

        // Сначала пытаемся использовать кэш; если его нет или он уничтожен — переопределяем
        if (!_cubeIdToHook.TryGetValue(cubeId, out hook) || hook == null)
        {
            // Проверяем компонент на самом объекте
            if (!cube.TryGetComponent(out hook))
            {
                // Если на самом объекте нет — ищем среди детей (без неактивных для экономии)
                hook = cube.GetComponentInChildren<Hook>(false);
            }

            if (hook != null)
            {
                _cubeIdToHook[cubeId] = hook;
            }
        }

        if (hook)
        {
            hook.Detach();
        }
    }
}

