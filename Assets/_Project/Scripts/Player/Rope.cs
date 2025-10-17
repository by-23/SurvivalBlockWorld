using UnityEditor;
using UnityEngine;

public class Rope : MonoBehaviour
{
    [HideInInspector]
    public LineRenderer render;
    [HideInInspector]
    public RopeGenerator ropeGenerator;
    public GameObject[] hooks = new GameObject[2];
    float distance;
    public float minDistance = 1;

    Hook _hook_1, _hook_2;

    void Awake()
    {
        render = GetComponent<LineRenderer>();
    }

    void Update()
    {
        if (hooks[0] && hooks[1])
        {
            if (!_hook_1)
                _hook_1 = hooks[0].GetComponent<Hook>();

            if (!_hook_2)
                _hook_2 = hooks[1].GetComponent<Hook>();

            distance = Vector3.Distance(hooks[0].transform.position, hooks[1].transform.position);

            if (distance < 5)
            {
                // if (hooks[1].transform.parent.GetComponent<Player>())
                // {
                //     ropeGenerator._ragdoll = hooks[0].transform.root.GetComponent<Ragdoll>();
                // }
            }
            if (distance < minDistance)
            {
                if(_hook_1)
                    _hook_1._notAttraction = true;

                if (_hook_2)
                    _hook_2._notAttraction = true;

                /*Ragdoll _ragdoll_0 = hooks[0].GetComponentInParent<Ragdoll>();
                Ragdoll _ragdoll_1 = hooks[1].GetComponentInParent<Ragdoll>();

                if (_ragdoll_0)
                    _ragdoll_0.DestroyRope();

                if (_ragdoll_1)
                    _ragdoll_1.DestroyRope();

                if (hooks[1].CompareTag("Hook"))
                {
                    Clear();
                }

                Destroy(hooks[0]);
                hooks[0] = null;*/
            }
            else
            {
                if (_hook_1)
                    _hook_1._notAttraction = false;

                if (_hook_2)
                    _hook_2._notAttraction = false;
            }


            if (render)
            {
                Vector3[] positions = new Vector3[2];
                positions[0] = hooks[0].transform.position;
                positions[1] = hooks[1].transform.position;

                render.SetPositions(positions);
            }
        }
    }

    public void Clear()
    {
        if(hooks[0] && hooks[0].gameObject.name != "Point")
            Destroy(hooks[0]);

        if (hooks[1] && hooks[1].gameObject.name != "Point")
            Destroy(hooks[1]);

        if(gameObject)
            Destroy(gameObject);
    }

}
