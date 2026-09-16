using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// How much an ability's effect depends on a clear grid line from its caster to what it is aimed
/// at. Planning never enforces this — a player may aim anywhere the range allows, and walls are
/// part of what several of these abilities are for — so it exists to tell the bot which of its
/// legal shots are worth taking.
/// </summary>
public enum AbilityLineOfFire
{
    /// <summary>Walls are no obstacle: the effect is lobbed, leapt, or laid on the caster.</summary>
    Irrelevant,

    /// <summary>
    /// A wall costs the shot most of its value but not all of it, so it is the last resort rather
    /// than an illegal aim.
    /// </summary>
    Preferred,

    /// <summary>Nothing lands through a wall; aiming past one wastes the ability outright.</summary>
    Required,
}

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
        Interrupt();
    }

    /// <summary>
    /// Stops this ability whether or not it reached an interruptible window, unlike
    /// <see cref="InterruptForStun"/>. Callers own refunding the charge.
    /// </summary>
    public bool CancelForDisplacement()
    {
        if (execution == null)
            return false;

        StopCoroutine(execution);
        execution = null;
        interruptibleExecution = null;
        OnAbilityInterrupted();
        GetComponent<Unit>()?.ResumeShootingAfterAbility();
        return true;
    }

    public virtual bool CancelsAlliedOrders => false;

    public virtual AbilityLineOfFire LineOfFire => AbilityLineOfFire.Irrelevant;

    private void Interrupt()
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

    /// <summary>
    /// The cell this ability sets its caster down on, for the abilities that carry it — a rush, a
    /// jump. Planning holds that cell against the rest of the team and frees the one the caster
    /// launches from, so an ability that leaves its caster where it stands reports nothing.
    /// </summary>
    public virtual bool TryGetCasterDestination(
        Vector3 targetSquare,
        UnitData data,
        out Vector2Int destinationCell
    )
    {
        destinationCell = default;
        return false;
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
