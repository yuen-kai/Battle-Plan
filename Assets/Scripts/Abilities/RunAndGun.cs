using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Outrider's active: for this round only, nearby allies fight the way Outrider always does — firing
/// while they move instead of stopping first. The character's own run-and-gun is a permanent
/// <c>UnitData.canShootWhileMoving</c> trait; this ability lends it out.
/// <para>
/// The whole effect is just starting those allies' weapons. <c>GameLoop.ExecuteMoves</c> already
/// dispatches movement and then, for a run-and-gun unit, calls
/// <see cref="Shooting.StartShooting"/> alongside it — so an ally that is already marching only needs
/// that same call to begin firing mid-move. There is no separate movement mode to switch on.
/// </para>
/// <para>
/// Each ally is also handed the trait itself for the round (see
/// <see cref="Movement.GrantShootWhileMovingForThisRound"/>) so the end of its move behaves the same
/// as Outrider's: <c>Movement.MoveToCells</c> skips its usual restart-shooting-on-arrival step for a
/// run-and-gun unit, which is what keeps the burst this ability started from being thrown away and
/// replaced with a fresh magazine the moment the ally stops walking. The grant is runtime state on
/// the component, never written into the shared <c>UnitData</c> asset — that would turn every copy of
/// that character into a run-and-gunner permanently, in every future match.
/// </para>
/// </summary>
public class RunAndGun : Ability
{
    /// <summary>
    /// How far the call reaches, in cells. Read from the caster's <c>UnitData.abilityRadius</c> at
    /// runtime (this is the fallback when none is supplied), so the radius shown on the planning
    /// overlay and the radius actually affected are the same number.
    /// </summary>
    private const float DefaultRadiusCells = 3f;

    /// <summary>
    /// Dead time before Outrider itself may resume shooting. Short — nothing was fired, this is a
    /// shout — but not free, so the ability still costs its caster a beat.
    /// </summary>
    private const float RecoverySeconds = 0.5f;

    public override IEnumerator ExecuteAbility(
        Vector3 abilitySquare,
        float AreaRadius = DefaultRadiusCells
    )
    {
        if (!IsServer)
            yield break;

        Unit casterIdentity = GetComponent<Unit>();
        if (casterIdentity == null)
            yield break;

        float radius = AreaRadius > 0f ? AreaRadius : DefaultRadiusCells;
        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );

        List<NetworkObjectReference> lent = new();
        foreach (GameObject ally in GameLoop.GetTeamUnits(casterIdentity.TeamIndex))
        {
            if (ally == null || ally == gameObject || !ally.activeInHierarchy)
                continue;

            Health allyHealth = ally.GetComponent<Health>();
            Movement allyMovement = ally.GetComponent<Movement>();
            Shooting allyShooting = ally.GetComponent<Shooting>();
            Unit allyIdentity = ally.GetComponent<Unit>();
            if (
                allyHealth == null
                || allyMovement == null
                || allyShooting == null
                || allyIdentity == null
                || !allyHealth.IsAlive
                || allyIdentity.TeamIndex != casterIdentity.TeamIndex
            )
            {
                continue;
            }

            Vector2Int allyCell = GridSystem.ConvertToGridCoords(
                GridSystem.GetNearestGridCell(ally)
            );
            if (Vector2.Distance(casterCell, allyCell) > radius + 0.001f)
                continue;

            allyMovement.GrantShootWhileMovingForThisRound();

            // A dodger is deliberately excluded: the dive recovery window exists so a fast
            // reposition cannot also be a free early shot, and ExecuteMoves withholds run-and-gun
            // from a diving unit for exactly that reason. Lending the trait here would undercut it.
            if (!allyMovement.IsRecoveringFromDive)
                allyShooting.StartShooting();

            NetworkObject allyObject = ally.GetComponent<NetworkObject>();
            if (allyObject != null)
                lent.Add(allyObject);
        }

        if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
            RunAndGunFxClientRpc(lent.ToArray());

        yield return new WaitForSeconds(RecoverySeconds);
        GetComponent<Movement>()?.transitionToShooting();
    }

    /// <summary>
    /// The cast, on every peer: a flash on Outrider and an aura on each ally that was actually lent
    /// the trait — so the player can see exactly who the call reached, which is the tactical
    /// information the ability is about. Only the server knows that set, hence passing it down.
    /// <para>
    /// No shockwave ring: <c>GameLoop.ShowAbilityActivationFxClientRpc</c> already fires one for
    /// every ability activation in the game, and in this project ring radius is how loudness is
    /// stated. A shout is not a loud event; it is a state handed to other units, and the aura is what
    /// carries a state.
    /// </para>
    /// </summary>
    [ClientRpc]
    private void RunAndGunFxClientRpc(NetworkObjectReference[] lentAllies)
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);
        ImpactCore.Spawn(transform.position, AbilityJuice.Hot(teamColor, 1.4f), 1f, 0.9f);

        if (lentAllies == null)
            return;

        foreach (NetworkObjectReference reference in lentAllies)
        {
            if (!reference.TryGet(out NetworkObject allyObject) || allyObject == null)
                continue;

            BuffAura aura = BuffAura.Attach(allyObject.gameObject);
            if (aura != null)
                aura.HoldFor(BuffAuraSource.FireRate, teamColor, RunAndGunAuraSeconds, 0.7f);
        }
    }

    /// <summary>
    /// How long the lent-trait aura shows. Not a gameplay duration — the trait itself lasts the round
    /// and is cleared at the round boundary — just long enough to read who was included.
    /// </summary>
    private const float RunAndGunAuraSeconds = 3f;
}
