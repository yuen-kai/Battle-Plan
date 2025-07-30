using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Reroute : MonoBehaviour, IAbility
{
    float planningTimePerUnit = 3;
    float abilityRange = 7;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        Time.timeScale = 0f;

        //Response
        string team = gameObject.tag;

        List<GameObject> alliesInRange = ActivateAbility.GetUnitsInRange(transform.position, team, abilityRange);


        GameLoop.Instance.setOverlayUIText($"Rerouting: {team}", team);

        yield return StartCoroutine(PlanMovement.Instance.ChoosePaths(team, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            GameLoop.Instance.setOverlayUIText("Executing Moves", "neutral");

            //continue time
            Time.timeScale = 1f;
            GameLoop.setUnitCardsInteractable(true);
            //execute dive
            foreach (var pair in paths)
            {
                GameObject unit = pair.Key;
                List<Vector3> movementPath = pair.Value;
                if (movementPath.Count == 0) continue; //skip if no path
                unit.GetComponent<Shooting>().StopShooting();
                unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath));
            }
        }, planningTimePerUnit * alliesInRange.Count, alliesInRange));
    }
}
