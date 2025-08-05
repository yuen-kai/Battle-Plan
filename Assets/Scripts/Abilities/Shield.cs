using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shield : Ability
{
    float abilityTime = 3;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        GameObject shield = transform.Find("Shield").gameObject;
        shield?.SetActive(true);
        yield return new WaitForSeconds(abilityTime);
        shield?.SetActive(false);
    }
}
