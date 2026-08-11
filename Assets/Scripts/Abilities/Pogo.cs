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

        // Landing on every peer. A third of the shake a grenade takes: enough to say a body with
        // weight arrived, not enough to say something went off.
        LandingFxClientRpc(targetPosition);
        CameraEffects.Instance?.CameraShakeClientRpc(0.25f, 0.035f);

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
        // Sized against what the jump costs the other crew, which is nothing. It moves one unit
        // and takes no health off anyone, and it was still the second loudest thing in the game
        // behind a grenade: the only event carrying a light pop, a clod field and a debris throw
        // on top of its ring. The ring now sits between the 1.8 an ability activation fires and
        // the 3.0 an AreaLock hit fires, so a landing reads as louder than a cast and quieter than
        // anything that costs health. The light pop comes off with it — a flash is a detonation,
        // and nothing detonated.
        ImpactShockwave.Spawn(
            landingPosition,
            teamColor,
            2.4f,
            0.42f,
            withLightPop: false,
            groundDust: 0.35f
        );

        // The clods the ring throws are now the only hard material here. A DebrisBurst used to run
        // with them, and it is an explosion: it builds flame cards, coal seams and a scorch mark
        // multiplied into the deck. The line under it has always said nothing here is on fire, and
        // a jump that takes no health off anyone was still leaving a burn on the floor for the
        // rest of the round.
        //
        // Everything below is placed against the rider's own silhouette rather than on top of it.
        // The one thing that makes this an arrival is a unit standing in the middle of the mess,
        // and measured on the frames either side of the peak the plume was burying seventy percent
        // of him — so the dust is two unequal plumes flanking him instead of one stamped on his
        // chest. Their offsets come in with their sizes: held at the old spread, plumes this small
        // would leave him standing between two puffs that have nothing to do with him.
        Vector3 lateral = ViewerLateral();
        Vector3 towardViewer = Vector3.Cross(Vector3.up, lateral);

        // Displaced material rather than a burn: the deck was struck, not lit.
        Aftermath.Spawn(
            landingPosition + lateral * 0.8f - towardViewer * 0.15f,
            teamColor,
            0.75f,
            AftermathKind.Dust
        );
        Aftermath.Spawn(
            landingPosition - lateral * 0.7f + towardViewer * 0.4f,
            teamColor,
            0.6f,
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
