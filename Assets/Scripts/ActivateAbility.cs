using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ActivateAbility : MonoBehaviour
{
    public GameObject unit;
    public PlanMovement PlanMovementScript;

    [SerializeField] private bool selectAbilitySquare;
    [SerializeField] private float abilitySquareRange = 3f;
    [SerializeField] private float selectTime = 4f;

    [SerializeField] private float responseRange = 5f;
    [SerializeField] private TMP_Text overlayUIText;
    [SerializeField] private float timeDivePerUnit = 3f;
    [SerializeField] private int diveRange = 2;

    public void activateAbility()
    {
        //pause time
        Time.timeScale = 0f;

        //player selects ability square
        Vector3 abilitySquare = selectAbilitySquare ? PlanMovement.GetGridCellUnderMouse() : unit.transform.position; //TODO: fix

        //Response
        string enemyTeam = unit.tag == "BlueTeam" ? "RedTeam" : "BlueTeam";

        List<GameObject> enemiesInRange = GetEnemiesInRange(abilitySquare, enemyTeam);

        overlayUIText.text = $"Dodging: {enemyTeam}";
        StartCoroutine(PlanMovementScript.ChoosePaths(enemyTeam, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            overlayUIText.text = "Executing Moves";
            //continue time
            Time.timeScale = 1f;

            //execute dive
            foreach (var pair in paths)
            {
                GameObject unit = pair.Key;
                List<Vector3> movementPath = pair.Value;
                unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath), true);
            }

            //activate ability
            StartCoroutine(unit.GetComponent<Ability>().ExecuteAbility(abilitySquare));
        }, timeDivePerUnit * enemiesInRange.Count, enemiesInRange, diveRange));
    }


    public List<GameObject> GetEnemiesInRange(Vector3 abilitySquare, string enemyTeam)
    {
        List<GameObject> enemiesInRange = new List<GameObject>();
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);

        foreach (GameObject enemy in allEnemies)
        {
            float distance = Vector3.Distance(abilitySquare, enemy.transform.position);
            if (distance <= responseRange * GameLoop.cellSize)
            {
                enemiesInRange.Add(enemy);
            }
        }

        return enemiesInRange;
    }

    //IEnumerator selectAbilitySquareFunc()
    //{
        
    //}

}
