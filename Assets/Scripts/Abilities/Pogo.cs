using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pogo : MonoBehaviour, IAbility
{
    public float abilityTime = 3;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare)
    {
        AbilityHelper.DisableShooting(transform);

        GameObject shield = transform.Find("Shield").gameObject;
        shield?.SetActive(true);
        yield return new WaitForSecondsRealtime(abilityTime);
        shield?.SetActive(false);

        AbilityHelper.EnableShooting(transform);
    }
}
