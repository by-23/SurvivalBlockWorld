using UnityEngine;

[RequireComponent(typeof(Entity))]
public class EntityVehicleConnector : MonoBehaviour
{
    private VehicleForce vehicleForce;
    private Entity entity;

    private void Awake()
    {
        entity = GetComponent<Entity>();
        vehicleForce = GetComponentInParent<VehicleForce>();
    }

    public void OnEntityRecalculated()
    {
        if (vehicleForce)
            vehicleForce.CheckJoint();
    }
}

