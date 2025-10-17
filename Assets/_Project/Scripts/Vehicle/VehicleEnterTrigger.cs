using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleEnterTrigger : MonoBehaviour
{
    [SerializeField] VehicleForce _vehicleForce;

    private void OnTriggerStay(Collider other)
    {
        Player player = other.GetComponent<Player>();

        if (player)
        {
            //player._vehicleForce = _vehicleForce;
            Debug.Log("Enter: " + other.name);
        }
    }
}
