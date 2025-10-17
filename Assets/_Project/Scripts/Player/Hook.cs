using UnityEngine;

public class Hook : MonoBehaviour
{
    public bool _notAttraction;
    public Transform target;
    public float shrinkSpeed = 20;


    public Rigidbody rb;
    public Joint joint;
    public Rope rope;

    void FixedUpdate()
    {
        if (_notAttraction) return;

        if (target != null)
        {
            if (transform.position != target.position)
            {
                Vector3 heading = target.position - this.transform.position;
                Vector3 direction = heading / heading.magnitude;

                if (target.parent.GetComponent<PlayerMovement>())
                {
                    direction *= 5;
                    direction.y += 4;
                }

                rb.AddForce(direction * (shrinkSpeed * 1000 * Time.fixedDeltaTime), ForceMode.Force);
            }
        }
    }

    public void Detach()
    {
        rope.Clear();
    }

}
