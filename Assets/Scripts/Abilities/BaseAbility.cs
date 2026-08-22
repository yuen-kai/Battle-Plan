using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public abstract class Ability : NetworkBehaviour
{
    private Coroutine execution;
    private Coroutine interruptibleExecution;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
    }

    public virtual void ResetForRespawn()
    {
        CancelExecution();
        StopAllCoroutines();
    }

    public abstract IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius);

    public IEnumerator RunAbility(Vector3 abilitySquare, float areaRadius)
    {
        if (execution != null)
            yield break;

        execution = StartCoroutine(ExecuteTracked(abilitySquare, areaRadius));
        interruptibleExecution = execution;
        while (execution != null)
            yield return null;
    }

    public void InterruptForStun()
    {
        if (interruptibleExecution == null)
            return;

        StopCoroutine(interruptibleExecution);
        execution = null;
        interruptibleExecution = null;
        OnAbilityInterrupted();
        GetComponent<Unit>()?.ResumeShootingAfterAbility();
    }

    private IEnumerator ExecuteTracked(Vector3 abilitySquare, float areaRadius)
    {
        yield return null;
        interruptibleExecution = null;
        yield return ExecuteAbility(abilitySquare, areaRadius);
        execution = null;
    }

    public virtual AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        points.Clear();
        return AbilityPathKind.None;
    }

    protected void BeginInterruptibleAbilityAction()
    {
        interruptibleExecution = execution;
        GetComponent<Unit>()?.PauseShootingForAbility();
    }

    protected void CompleteInterruptibleAbilityAction()
    {
        if (interruptibleExecution == null)
            return;

        interruptibleExecution = null;
        GetComponent<Unit>()?.ResumeShootingAfterAbility();
    }

    protected virtual void OnAbilityInterrupted() { }

    protected virtual void OnDisable()
    {
        CancelExecution();
    }

    private void CancelExecution()
    {
        Coroutine runningExecution = execution;
        Coroutine interruptedExecution = interruptibleExecution;
        execution = null;
        interruptibleExecution = null;

        if (runningExecution != null)
            StopCoroutine(runningExecution);
        if (interruptedExecution != null)
            OnAbilityInterrupted();
    }
}
