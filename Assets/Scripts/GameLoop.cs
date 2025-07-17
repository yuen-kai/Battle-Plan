using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameLoop : MonoBehaviour
{
    List<GameObject> doneMovingUnits = new List<GameObject>();
    List<GameObject> doneShootingUnits = new List<GameObject>();
    bool waiting = false;

    void Start()
    {
        StartCoroutine(GameLoopTemp());
    }

    IEnumerator GameLoopTemp()
    {
        while (GameObject.FindGameObjectsWithTag("BlueTeam").Length > 0 && GameObject.FindGameObjectsWithTag("RedTeam").Length > 0)
        {
            Dictionary<GameObject, List<Vector3>> bluePaths = new Dictionary<GameObject, List<Vector3>>();
            Dictionary<GameObject, List<Vector3>> redPaths = new Dictionary<GameObject, List<Vector3>>();

            yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths("BlueTeam", (Dictionary<GameObject, List<Vector3>> paths) =>
            {
                bluePaths = new Dictionary<GameObject, List<Vector3>>(paths);
            }));
            yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths("RedTeam", (Dictionary<GameObject, List<Vector3>> paths) =>
            {
                redPaths = new Dictionary<GameObject, List<Vector3>>(paths);
            }));

            waiting = true;
            ExecuteMoves(bluePaths);
            ExecuteMoves(redPaths);

            while (waiting)
            {
                yield return null;
            }
        }
        Debug.Log("Game Over");
        //yield return StartCoroutine(GameObject.FindGameObjectsWithTag("BlueTeam")[0].GetComponent<Shooting>().StartShooting());
    }

    void ExecuteMoves(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            StartCoroutine(unit.GetComponent<Movement>().MoveToCells(new List<Vector3>(movementPath), () => // C# passes parameters by reference, so we need to create a new list
            {
                doneMovingUnits.Add(unit);
                if (doneMovingUnits.Count >= getRemainingUnits())
                {
                    OrderStopShooting();
                }
            }, () =>
            {
                doneShootingUnits.Add(unit);
                if (doneShootingUnits.Count >= getRemainingUnits())
                {
                    waiting = false;
                }
            }));
        }
    }

    int getRemainingUnits()
    {
        return GameObject.FindGameObjectsWithTag("BlueTeam").Length + GameObject.FindGameObjectsWithTag("RedTeam").Length;
    }

    void OrderStopShooting()
    {
        foreach (GameObject unit in GameObject.FindGameObjectsWithTag("BlueTeam"))
        {
            unit.GetComponent<Shooting>().shooting = false;
        }
        foreach (GameObject unit in GameObject.FindGameObjectsWithTag("RedTeam"))
        {
            unit.GetComponent<Shooting>().shooting = false;
        }
    }

    void PrintPaths(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            Debug.Log($"Unit: {pair.Key.name}, Path: {string.Join(", ", pair.Value)}");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
