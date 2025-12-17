using Unity.Netcode;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class UnitCardContainer : NetworkBehaviour
{
    public void SetUnitCardsInteractable(bool interactable)
    {
        SetUnitCardsInteractableClientRpc(interactable);
    }

    [ClientRpc]
    private void SetUnitCardsInteractableClientRpc(bool interactable)
    {
        foreach (Transform child in transform)
        {
            child.GetComponent<UnitCard>().setUnitCardInteractable(interactable);
        }
    }

    //Server
    public void DisableUnitCard(GameObject unit)
    {
        (ulong clientId, int index) = FindUnitIndex(unit);
        DisableUnitCardClientRpc(index, NetworkHelper.ToClient(clientId));
    }

    private (ulong clientId, int index) FindUnitIndex(GameObject unit)
    {
        foreach (var kvp in GameLoop.allTeamUnitObjects)
        {
            int index = System.Array.IndexOf(kvp.Value, unit);
            if (index != -1) return (kvp.Key, index);
        }
        return (0, -1);
    }

    [ClientRpc]
    private void DisableUnitCardClientRpc(int unitIndex, ClientRpcParams clientRpcParams = default)
    {
        transform.GetChild(unitIndex).GetComponent<UnitCard>().disabled = true;
    }
}