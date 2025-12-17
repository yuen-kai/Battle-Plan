using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class Unit : NetworkBehaviour
{
    public List<Material> teamMaterials;

    [HideInInspector]
    public bool selectMovement = true;

    public override void OnNetworkSpawn()
    {
        SetTeamIndicators();
    }

    void SetTeamIndicators()
    {
        GameObject[] teamIndicators = GetComponentsInChildren<Transform>()
            .Where(t => t.CompareTag("TeamIndicatorProp"))
            .Select(t => t.gameObject)
            .ToArray();

        foreach (GameObject indicator in teamIndicators)
        {
            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.materials = new Material[] { IsOwner ? teamMaterials[0] : teamMaterials[1] };
            }
        }

    }
}