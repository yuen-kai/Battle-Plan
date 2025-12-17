using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Reroute : Ability
{
    float planningTimePerUnit = 3;
    float abilityRange = 7;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        Time.timeScale = 0f;

        //Response
        string team = gameObject.tag;

        List<GameObject> alliesInRange = Helper.GetObjectsInRange(
            transform.position,
            team,
            abilityRange
        );

        // convert alliesInRange (List<GameObject>) to NetworkObjectReference[]
        NetworkObjectReference[] alliesInRangeArray = new NetworkObjectReference[
            alliesInRange.Count
        ];
        for (int i = 0; i < alliesInRange.Count; i++)
        {
            GameObject ally = alliesInRange[i];
            if (ally.TryGetComponent<NetworkObject>(out NetworkObject netObj))
            {
                alliesInRangeArray[i] = netObj;
            }
        }

        GameLoop.Instance.setOverlayUITextClientRpc($"Rerouting: {team}", team);

        double endTime = NetworkManager.ServerTime.Time + planningTimePerUnit * alliesInRange.Count;
        RerouteClientRpc(endTime, alliesInRangeArray);
        yield return null;
    }

    [ClientRpc]
    void RerouteClientRpc(double endTime, NetworkObjectReference[] alliesInRangeArray)
    {
        if (IsOwner)
        {
            List<GameObject> alliesInRange = new();
            foreach (var ally in alliesInRangeArray)
            {
                if (ally.TryGet(out NetworkObject netObj))
                {
                    alliesInRange.Add(netObj.gameObject);
                }
            }

            StartCoroutine(
                PlanMovement.Instance.StartPlanning(
                    (PathsDict paths) => RerouteServerRpc(paths),
                    endTime,
                    alliesInRange
                )
            );
        }
        else
        {
            StartCoroutine(CountDown(endTime));
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RerouteServerRpc(PathsDict paths, ServerRpcParams rpcParams = default)
    {
        GameLoop.Instance.setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

        //continue time
        Time.timeScale = 1f;
        GameLoop.Instance.unitCards.GetComponent<UnitCardContainer>().SetUnitCardsInteractable(true);
        //execute dive
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            if (movementPath.Count == 0)
                continue; //skip if no path
            unit.GetComponent<Shooting>().PauseShooting();
            unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath));
        }
    }

    public IEnumerator CountDown(double endTime)
    {
        float timeRemaining;
        while ((timeRemaining = (float)(endTime - NetworkManager.Singleton.ServerTime.Time)) > 0f)
        {
            PlanMovement.Instance.timerTextUI.text = (Mathf.CeilToInt(timeRemaining)).ToString();
            yield return null;
        }
        PlanMovement.Instance.timerTextUI.text = "";
    }
}
