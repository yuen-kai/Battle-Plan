using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityHelper : MonoBehaviour
{
    public static void DisableShooting(Transform unit)
    {
        unit.GetComponent<Shooting>().disabledShooting = true;
        unit.GetComponent<Shooting>().allowShooting = false;
        unit.GetComponent<Shooting>().stillShooting = false;
    }

    public static void EnableShooting(Transform unit)
    {
        unit.GetComponent<Shooting>().disabledShooting = false;
    }
}
