using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameLoop : MonoBehaviour
{
    [SerializeField] private GameObject[] blueTeamCharacters;
    [SerializeField] private GameObject[] redTeamCharacters;
    // Start is called before the first frame update
    void Start()
    {
        //// Start shooting for all blue team characters
        //foreach (GameObject character in blueTeamCharacters)
        //{
        //    if (character.GetComponent<Shooting>() != null)
        //    {
        //        StartCoroutine(character.GetComponent<Shooting>().StartShooting());
        //    }
        //}

        //// Start shooting for all red team characters
        //foreach (GameObject character in redTeamCharacters)
        //{
        //    if (character.GetComponent<Shooting>() != null)
        //    {
        //        StartCoroutine(character.GetComponent<Shooting>().StartShooting());
        //    }
        //}

        StartCoroutine(blueTeamCharacters[0].GetComponent<Shooting>().StartShooting());
        StartCoroutine(redTeamCharacters[0].GetComponent<Shooting>().StartShooting());
        //StartCoroutine(GameLoopTemp());
    }

    IEnumerator GameLoopTemp()
    {
        Dictionary<GameObject, List<Vector3>> bluePaths = new Dictionary<GameObject, List<Vector3>>();
        Dictionary<GameObject, List<Vector3>> redPaths = new Dictionary<GameObject, List<Vector3>>();

        yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths("BlueTeam", blueTeamCharacters, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            bluePaths = new Dictionary<GameObject, List<Vector3>>(paths);
        }));
        yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths("RedTeam", redTeamCharacters, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            redPaths = new Dictionary<GameObject, List<Vector3>>(paths);
        }));

        ExecuteMoves(bluePaths);
        ExecuteMoves(redPaths);
    }

    void ExecuteMoves(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            StartCoroutine(unit.GetComponent<Movement>().MoveToCells(new List<Vector3>(movementPath))); // C# passes parameters by reference, so we need to create a new list
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
