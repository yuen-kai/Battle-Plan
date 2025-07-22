using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shield : MonoBehaviour, IAbility
{
    float abilityTime = 3;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare)
    {
        AbilityHelper.DisableShooting(transform); //TODO: Fix shooting disable/enable

        GameObject shield = transform.Find("Shield").gameObject;
        shield?.SetActive(true);
        yield return new WaitForSecondsRealtime(abilityTime);
        shield?.SetActive(false);

        AbilityHelper.EnableShooting(transform);
    }
}
