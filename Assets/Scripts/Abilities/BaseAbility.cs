using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAbility
{
    public IEnumerator ExecuteAbility(Transform unit, Vector3 abilitySquare);
}

