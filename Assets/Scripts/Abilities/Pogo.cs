using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class Pogo : Ability
{
    float abilityTime = 1;

    // How high the rider rises over the midpoint of the jump.
    private const float ArcApexHeight = 5f;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        return AbilityTrajectory.BuildLob(
            transform.position,
            GetLandingPosition(targetSquare),
            ArcApexHeight,
            points
        )
            ? AbilityPathKind.Lob
            : AbilityPathKind.None;
    }

    /// <summary>
    /// Where the rider comes down. Reads the collider, so it has to be resolved before the jump
    /// disables it.
    /// </summary>
    private Vector3 GetLandingPosition(Vector3 abilitySquare)
    {
        return abilitySquare + Helper.heightOffset(transform);
    }

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        Collider unitCollider = GetComponent<Collider>();
        if (unitCollider != null)
            unitCollider.enabled = true;
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = GetLandingPosition(abilitySquare); //Has to be before collider is disabled

        transform.GetComponent<Movement>().PauseMovement();
        transform.GetComponent<Shooting>().PauseShooting();
        transform.GetComponent<Movement>().moving = true;
        transform.GetComponent<Collider>().enabled = false;

        float elapsed = 0f;

        while (elapsed < abilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / abilityTime;

            transform.position = AbilityTrajectory.SampleLob(
                startPosition,
                targetPosition,
                ArcApexHeight,
                progress
            );

            yield return null; // Wait for next frame
        }
        transform.position = targetPosition;

        // Landing slam on every peer + a light shake to sell the weight
        LandingFxClientRpc(targetPosition);
        CameraEffects.Instance?.CameraShakeClientRpc();

        transform.GetComponent<Collider>().enabled = true;
        // Landing is where the rider becomes shootable again, and it starts firing on the same
        // frame. Hold the round's weapons free so the units it came down among can answer it,
        // however late in the round the jump resolves.
        GameLoop.Instance?.HoldReturnFireWindow();
        transform.GetComponent<Movement>().transitionToShooting();
    }

    [ClientRpc]
    private void LandingFxClientRpc(Vector3 landingPosition)
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);
        ImpactShockwave.Spawn(landingPosition, teamColor, 1.8f, 0.4f);
    }
}
