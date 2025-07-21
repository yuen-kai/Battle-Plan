using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability : MonoBehaviour
{
    public float abilityTime;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare)
    {
        AbilityHelper.DisableShooting(transform);

        GameObject shield = transform.Find("Shield").gameObject;
        shield.SetActive(true);
        yield return new WaitForSecondsRealtime(abilityTime);
        shield.SetActive(false);

        AbilityHelper.EnableShooting(transform);
    }
}
