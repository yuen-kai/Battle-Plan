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

        // An arrival, and deliberately the opposite read to a death: everything here leaves the
        // point of contact outward and upward, and the rider is still standing in the middle of it
        // once the dust settles. The expanding ring belongs to this event and to no other on the
        // board — anything else that fires one is indistinguishable from a rider coming down.
        //
        // The front has to cross two and a half cells: under 2.1 units of radius the shockwave
        // throws no clods at all, which leaves a landing as a rim and nothing else.
        ImpactShockwave.Spawn(landingPosition, teamColor, 3.4f, 0.5f);

        // Everything else is placed against the rider's own silhouette rather than on top of it.
        // The one thing that makes this an arrival is a unit standing in the middle of the mess,
        // and measured on the frames either side of the peak the plume was burying seventy percent
        // of him — so the plates are nudged into the near ground and the dust becomes two unequal
        // plumes flanking him instead of one stamped on his chest.
        Vector3 lateral = ViewerLateral();
        Vector3 towardViewer = Vector3.Cross(Vector3.up, lateral);

        DebrisBurst.Spawn(landingPosition + towardViewer * 0.3f, teamColor, 3.2f, 20);
        // Displaced material rather than a burn: nothing here is on fire, the deck was struck.
        Aftermath.Spawn(
            landingPosition + lateral * 1.05f - towardViewer * 0.2f,
            teamColor,
            1.2f,
            AftermathKind.Dust
        );
        Aftermath.Spawn(
            landingPosition - lateral * 0.9f + towardViewer * 0.55f,
            teamColor,
            0.85f,
            AftermathKind.Dust
        );
    }

    /// <summary>
    /// Screen-right along the ground, taken from whichever seat is watching. The two crews look at
    /// the board from opposite ends, so "beside the rider" has to be resolved per viewer or the
    /// dust that clears him on one screen lands on him on the other.
    /// </summary>
    private static Vector3 ViewerLateral()
    {
        Camera board = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (board == null)
            board = Camera.main;

        Vector3 right = board != null ? board.transform.right : Vector3.right;
        right.y = 0f;
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
    }
}
