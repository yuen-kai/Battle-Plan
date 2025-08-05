using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public abstract class Ability: NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false; // disables Update(), Start(), etc.
            return;
        }
    }

    public abstract IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius);
}

