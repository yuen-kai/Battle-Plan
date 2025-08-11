using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Shield : Ability
{
    float abilityTime = 3;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        GameObject shield = transform.Find("Shield").gameObject;
        shield?.SetActive(true);
        ToggleShieldClientRpc(true);
        yield return new WaitForSeconds(abilityTime);
        shield?.SetActive(false);
        ToggleShieldClientRpc(false);
    }

    [ClientRpc]
    private void ToggleShieldClientRpc(bool enabled)
    {
        var shield = transform.Find("Shield");
        if (shield != null)
        {
            shield.gameObject.SetActive(enabled);
        }
    }
}
