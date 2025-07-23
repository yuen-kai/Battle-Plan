using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shield : MonoBehaviour, IAbility
{
    float abilityTime = 3;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare)
    {
        GameObject shield = transform.Find("Shield").gameObject;
        shield?.SetActive(true);
        yield return new WaitForSeconds(abilityTime);
        shield?.SetActive(false);
    }
}
