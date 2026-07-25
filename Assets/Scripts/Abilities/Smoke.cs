using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Smoke : Ability
{
    public const int FootprintRadius = 1;

    // Long enough that the canister reads as thrown rather than teleported, short enough that the
    // sightline the flight leaves open stays incidental instead of becoming the counterplay.
    public const float ThrowSeconds = 0.35f;

    // A flat toss rather than the grenade's high lob: a canister is pitched onto nearby ground,
    // not arced over cover.
    private const float ArcApexHeight = 1.4f;

    private const float TumbleDegreesPerSecond = 540f;

    [SerializeField]
    private GameObject canisterPrefab;

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

    /// <summary>Where the canister comes to rest: the target square at the thrower's own height.</summary>
    private Vector3 GetLandingPosition(Vector3 abilitySquare)
    {
        return abilitySquare + Helper.heightOffset(transform);
    }

    public bool RegisterTargetFootprint(Vector3 abilitySquare)
    {
        if (!IsServer || GameLoop.Instance == null)
            return false;

        Vector3 snappedSquare = GridSystem.GetNearestGridCell(abilitySquare);
        Vector2Int center = GridSystem.ConvertToGridCoords(snappedSquare);
        return GameLoop.Instance.TryRegisterSmokeFootprint(center);
    }

    /// <summary>
    /// Throws the canister, then deploys the screen where it lands. Registering the footprint on
    /// impact rather than in one batch at the top of the round is what makes the throw honest: the
    /// cloud a player can see and the occluder that stops a shot begin at the same moment, paid for
    /// with the brief sightline the flight leaves open.
    /// </summary>
    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Vector3 startPosition = transform.position;
        Vector3 landingPosition = GetLandingPosition(abilitySquare);
        GameObject canister =
            canisterPrefab != null
                ? NetworkHelper.Spawn(canisterPrefab, startPosition, Quaternion.identity)
                : null;

        ThrowFxClientRpc(startPosition, landingPosition);

        float elapsed = 0f;
        while (elapsed < ThrowSeconds)
        {
            elapsed += Time.deltaTime;
            if (canister != null)
            {
                canister.transform.position = AbilityTrajectory.SampleLob(
                    startPosition,
                    landingPosition,
                    ArcApexHeight,
                    Mathf.Clamp01(elapsed / ThrowSeconds)
                );
                canister.transform.Rotate(
                    Vector3.right,
                    TumbleDegreesPerSecond * Time.deltaTime,
                    Space.Self
                );
            }

            yield return null;
        }

        RegisterTargetFootprint(abilitySquare);
        if (canister != null)
            NetworkHelper.Instance.Despawn(canister);
    }

    /// <summary>
    /// The toss itself, on the thrower's cell and fog-gated with it. The bloom when the screen
    /// actually deploys is a separate, deliberately unmasked cue raised in GameLoop, because a
    /// screen changes sightlines for both players. Client-local presentation only.
    /// </summary>
    [ClientRpc]
    private void ThrowFxClientRpc(Vector3 fromPosition, Vector3 landingPosition)
    {
        BattlePlanAudio.PlayAt(AudioCueId.SmokeThrow, fromPosition, gameObject);

        // Anticipation (§9.3): the exact 3x3 the screen will occupy, held for the flight and faded
        // as the puffs take over. Shown to both players for the same reason the deploy bloom is —
        // a screen changes sightlines for everyone, so its footprint is not a secret.
        AbilityFX.SmokeThrow(landingPosition, ThrowSeconds);
    }
}
