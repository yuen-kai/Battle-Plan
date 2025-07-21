using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ActivateAbility : MonoBehaviour
{

    public bool selectAbilitySquare;
    public GameObject unit;
    public PlanMovement PlanMovement;

    public void activateAbility()
    {
        //pause time
        Time.timeScale = 0f;

        //player selects ability square
        Vector3 abilitySquare = selectAbilitySquare? PlanMovement.GetGridCellUnderMouse(): unit.transform.position;

        //Response
        //StartCoroutine(PlanMovement.ChoosePaths(enemyteam, )

        //activate ability
        StartCoroutine(unit.GetComponent<Ability>().ExecuteAbility(abilitySquare));

        //continue time
        Time.timeScale = 1f;
    }
}
