using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Movement system on a grid, handling movement and rotation. Transitions to shooting mode after movement.
/// </summary>
public class Movement : NetworkBehaviour
{
    public struct TimedMoveSpeedBoost
    {
        private float multiplier;
        private float startsAt;
        private float expiresAt;

        public bool TrySet(float requestedMultiplier, float duration, float startsAt)
        {
            if (
                requestedMultiplier <= 1f
                || duration <= 0f
                || float.IsNaN(requestedMultiplier)
                || float.IsInfinity(requestedMultiplier)
                || float.IsNaN(duration)
                || float.IsInfinity(duration)
                || float.IsNaN(startsAt)
                || float.IsInfinity(startsAt)
            )
            {
                return false;
            }

            multiplier = requestedMultiplier;
            this.startsAt = startsAt;
            expiresAt = startsAt + duration;
            return true;
        }

        public float GetMultiplier(float atTime)
        {
            return atTime >= startsAt && atTime < expiresAt ? multiplier : 1f;
        }

        public bool IsActive(float atTime)
        {
            return GetMultiplier(atTime) > 1f;
        }

        public float GetEffectiveSpeed(float baseSpeed, float atTime)
        {
            return Mathf.Max(0f, baseSpeed) * GetMultiplier(atTime);
        }

        public void Clear()
        {
            multiplier = 1f;
            startsAt = float.PositiveInfinity;
            expiresAt = float.NegativeInfinity;
        }
    }

    public UnitData unitData;

    [HideInInspector]
    public bool moving = true;

    public float CurrentMoveSpeedMultiplier =>
        temporaryMoveSpeedBoost.GetMultiplier(Time.time);
    public bool IsSpeedBoostIndicatorActive =>
        speedBoostIndicator != null && speedBoostIndicator.IsVisible;

    /// <summary>True while a dodger is picking itself up and cannot shoot.</summary>
    public bool IsRecoveringFromDive => diveRecovery.Value.Active;

    public bool CanShootWhileMoving => unitData != null && unitData.canShootWhileMoving;

    private Coroutine moveListRoutine;
    private Coroutine moveRoutine;
    private Coroutine rotateRoutine;

    private AnimationHandler animator;
    private TimedMoveSpeedBoost temporaryMoveSpeedBoost;
    private SpeedBoostIndicatorVisual speedBoostIndicator;
    private bool speedBoostIndicatorCreationAttempted;
    private DiveRecoveryPulse diveRecoveryPulse;

    // NetworkVariable (not ClientRpc) so a unit revealed by fog midway through the boost
    // reconstructs the current presentation state. Gameplay speed remains server-only above.
    private readonly NetworkVariable<bool> temporaryMoveSpeedBoostPresentationActive = new(false);

    // One replicated write per dive rather than a per-frame countdown: the indicator's fill is a
    // pure function of elapsed server time, so every peer can draw the correct point in the
    // recovery from the moment it learns about it — including a peer fog reveals midway through.
    private readonly NetworkVariable<DiveRecoveryState> diveRecovery = new();

    // CONTROLLER
    public override void OnNetworkSpawn()
    {
        temporaryMoveSpeedBoost.Clear();
        temporaryMoveSpeedBoostPresentationActive.OnValueChanged +=
            OnTemporaryMoveSpeedBoostPresentationChanged;
        if (IsServer)
            temporaryMoveSpeedBoostPresentationActive.Value = false;
        ApplyTemporaryMoveSpeedBoostPresentation(
            temporaryMoveSpeedBoostPresentationActive.Value
        );

        diveRecovery.OnValueChanged += OnDiveRecoveryChanged;
        if (IsServer)
            diveRecovery.Value = default;
        ApplyDiveRecoveryPresentation(diveRecovery.Value);

        if (!IsServer)
        {
            enabled = false;
            return;
        }
        enabled = true;
        animator = GetComponent<AnimationHandler>();
    }

    public override void OnNetworkDespawn()
    {
        temporaryMoveSpeedBoostPresentationActive.OnValueChanged -=
            OnTemporaryMoveSpeedBoostPresentationChanged;
        temporaryMoveSpeedBoost.Clear();
        ApplyTemporaryMoveSpeedBoostPresentation(false);

        diveRecovery.OnValueChanged -= OnDiveRecoveryChanged;
        ApplyDiveRecoveryPresentation(default);

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (
            IsServer
            && temporaryMoveSpeedBoostPresentationActive.Value
            && !temporaryMoveSpeedBoost.IsActive(Time.time)
        )
        {
            ClearTemporaryMoveSpeedBoost();
        }
    }

    public bool TryApplyTemporaryMoveSpeedBoost(float multiplier, float duration, float startsAt)
    {
        if (!IsServer)
            return false;

        if (!temporaryMoveSpeedBoost.TrySet(multiplier, duration, startsAt))
            return false;

        temporaryMoveSpeedBoostPresentationActive.Value = true;
        return true;
    }

    public void ClearTemporaryMoveSpeedBoost()
    {
        temporaryMoveSpeedBoost.Clear();
        if (IsServer)
            temporaryMoveSpeedBoostPresentationActive.Value = false;
    }

    public void PauseMovement()
    {
        if (moveListRoutine != null)
            StopCoroutine(moveListRoutine);
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);
        if (rotateRoutine != null)
            StopCoroutine(rotateRoutine);

        // The recovery hold lives inside the stopped movement coroutine, so anything that cuts
        // movement short — the next round, a respawn — has already ended it in fact.
        ClearDiveRecovery();
    }

    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        PauseMovement();
        moveListRoutine = StartCoroutine(MoveToCells(cells, dive));
    }

    public void transitionToShooting(bool onlyIfWeaponsStillFree = false)
    {
        PauseMovement();
        moving = false;

        Shooting shooting = transform.GetComponent<Shooting>();
        // A dodger stops counting as moving the moment it lands, so the round can order weapons
        // cold while it is still on the floor. Coming up after that means it missed the fight,
        // rather than earning a late magazine fired at units that have already ceased fire.
        if (onlyIfWeaponsStillFree && !shooting.allowShooting)
        {
            shooting.StandDown();
            return;
        }

        if (GetComponent<Unit>()?.IsStunned == true)
        {
            shooting.StandDown();
            return;
        }

        shooting.StartShooting();
    }

    // MOVEMENT
    public IEnumerator MoveToCells(List<Vector3> cells, bool dive = false)
    {
        if (animator != null)
        {
            animator.PlayAnimation("Moving");
        }

        moving = true;
        foreach (Vector3 cell in cells)
        {
            yield return moveRoutine = StartCoroutine(MoveToCell(cell, dive));
        }

        // Nested rather than started as its own coroutine, so PauseMovement stopping this one
        // stops the recovery with it.
        if (dive)
        {
            yield return RecoverFromDive();
        }

        // A "shoot while moving" unit's Shooting coroutine was already started by GameLoop
        // alongside this one (see GameLoop.ExecuteMoves), so calling transitionToShooting here
        // would restart it from a fresh magazine the moment movement finishes, discarding whatever
        // it fired mid-move. Dive always still goes through the normal gate below regardless of
        // this flag: the dodge recovery cost is exactly what stops a fast dive from being a free
        // "reposition and immediately open fire," and that must hold for every unit that can dodge,
        // not just the ones without this flag.
        if (!dive && CanShootWhileMoving)
        {
            PauseMovement();
            moving = false;
            yield break;
        }

        transitionToShooting(onlyIfWeaponsStillFree: dive);
    }

    /// <summary>
    /// The cost of the dive: the dodger is down where it landed and cannot shoot until it is back
    /// up. It stops counting as moving first, so a dodge holds the round's weapons free no longer
    /// than the dive itself did — the recovery is the dodger's to pay, not everyone else's.
    /// </summary>
    private IEnumerator RecoverFromDive()
    {
        float recovery = GameLoop.DodgeRecoverySeconds;
        if (recovery <= 0f)
            yield break;

        moving = false;
        if (animator != null)
        {
            animator.PlayAnimation("Idle");
        }

        BeginDiveRecovery(recovery);
        yield return new WaitForSeconds(recovery);
        ClearDiveRecovery();
    }

    private void BeginDiveRecovery(float duration)
    {
        if (!IsServer || duration <= 0f)
            return;

        diveRecovery.Value = new DiveRecoveryState(NetworkManager.ServerTime.Time, duration);
    }

    public void ClearDiveRecovery()
    {
        if (IsServer && diveRecovery.Value.Active)
            diveRecovery.Value = default;
    }

    private IEnumerator MoveToCell(Vector3 cell, bool dive = false)
    {
        Vector3 targetPosition = cell + Helper.heightOffset(transform);

        if (rotateRoutine != null)
            StopCoroutine(rotateRoutine);
        rotateRoutine = StartCoroutine(RotateToFaceTarget(targetPosition, unitData.rotationSpeed));

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            yield return null;
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                (
                    dive
                        ? unitData.diveSpeed
                        : temporaryMoveSpeedBoost.GetEffectiveSpeed(unitData.moveSpeed, Time.time)
                )
                    * GameLoop.cellSize
                    * Time.deltaTime
            );
        }

        transform.position = targetPosition;
    }

    public IEnumerator RotateToFaceTarget(Vector3 targetPosition, float rotationSpeed)
    {
        Vector3 direction = targetPosition - transform.position;
        if (direction.magnitude < 0.01f)
            yield break;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        while (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
        {
            yield return null;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        transform.rotation = targetRotation;
    }

    private void OnTemporaryMoveSpeedBoostPresentationChanged(
        bool previousValue,
        bool newValue
    )
    {
        ApplyTemporaryMoveSpeedBoostPresentation(newValue);
    }

    private void ApplyTemporaryMoveSpeedBoostPresentation(bool active)
    {
        if (active && !IsClient)
            return;

        if (active && speedBoostIndicator == null && !speedBoostIndicatorCreationAttempted)
        {
            speedBoostIndicatorCreationAttempted = true;
            speedBoostIndicator = SpeedBoostIndicatorVisual.Create(transform);
        }

        if (speedBoostIndicator != null)
            speedBoostIndicator.SetVisible(active);
    }

    private void OnDiveRecoveryChanged(
        DiveRecoveryState previousValue,
        DiveRecoveryState newValue
    )
    {
        ApplyDiveRecoveryPresentation(newValue);
    }

    private void ApplyDiveRecoveryPresentation(DiveRecoveryState state)
    {
        if (state.Active && !IsClient)
            return;

        if (state.Active && diveRecoveryPulse == null)
            diveRecoveryPulse = DiveRecoveryPulse.Attach(gameObject);

        if (diveRecoveryPulse != null)
            diveRecoveryPulse.SetRecovery(state);
    }

    // HELPER
    //public Vector2Int ConvertToGridCoords(Vector3 position)
    //{
    //    int x = Mathf.RoundToInt(position.x / GameLoop.cellSize);
    //    int z = Mathf.RoundToInt(position.z / GameLoop.cellSize);
    //    return new Vector2Int(x, z);
    //}
}

/// <summary>
/// A dive recovery as replicated: when the dodger went down on the server clock and how long it
/// stays down. The indicator's fill is a pure function of elapsed time, so this is written once per
/// dive instead of every frame, and a peer that fog reveals midway through still draws the right
/// point in the recovery.
/// </summary>
public struct DiveRecoveryState : INetworkSerializable, System.IEquatable<DiveRecoveryState>
{
    public bool Active;
    public double StartServerTime;
    public float Duration;

    public DiveRecoveryState(double startServerTime, float duration)
    {
        Active = true;
        StartServerTime = startServerTime;
        Duration = Mathf.Max(0.0001f, duration);
    }

    /// <summary>Recovery position, 0 as the dodger lands through 1 when it is back on its feet.</summary>
    public float ProgressAt(double serverTime)
    {
        return Mathf.Clamp01((float)((serverTime - StartServerTime) / Duration));
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Active);
        serializer.SerializeValue(ref StartServerTime);
        serializer.SerializeValue(ref Duration);
    }

    public bool Equals(DiveRecoveryState other)
    {
        return Active == other.Active
            && StartServerTime.Equals(other.StartServerTime)
            && Duration.Equals(other.Duration);
    }

    public override bool Equals(object obj)
    {
        return obj is DiveRecoveryState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Active, StartServerTime, Duration);
    }
}

/// <summary>
/// Where a ground effect drawn under a unit has to sit to be seen at all. A unit does not stand on
/// the board — it stands on its own base plate, whose top face is above the collider bottom that
/// the board plane is measured from — so a quad laid on that plane is depth-buried by the very
/// plate the unit is standing on. Every floor visual clears the plate through this.
/// </summary>
public static class UnitBasePlate
{
    public const string NamePrefix = "BasePuck";

    /// <summary>
    /// Height above a unit's board plane that clears its base plate by <paramref name="groundOffset"/>,
    /// falling back to the bare offset for anything built without a plate.
    /// </summary>
    public static float ClearanceAbovePlane(Transform unitTransform, float groundOffset)
    {
        Collider unitCollider =
            unitTransform != null ? unitTransform.GetComponent<Collider>() : null;
        if (unitCollider == null)
            return groundOffset;

        float bottom = unitCollider.bounds.min.y;
        float plateTop = bottom;
        foreach (Renderer part in unitTransform.GetComponentsInChildren<Renderer>(true))
        {
            if (!part.gameObject.name.StartsWith(NamePrefix, System.StringComparison.Ordinal))
                continue;
            plateTop = Mathf.Max(plateTop, part.bounds.max.y);
        }

        return plateTop - bottom + groundOffset;
    }

    /// <summary>
    /// The same clearance measured from the unit's own origin instead of its board plane, for
    /// anything that has to place itself on the frame the unit was positioned.
    /// </summary>
    /// <remarks>
    /// <see cref="ClearanceAbovePlane"/> answers from the board plane, so a caller parenting to the
    /// unit has to subtract the collider's half height to get back to the origin — reading the
    /// collider twice and the plate's renderer once. A collider's bounds lag a transform that moved
    /// this frame and a renderer's do not, so on exactly the frame a unit spawns or teleports those
    /// two readings disagree and the visual lands a whole unit height out. This reads the plate
    /// only, which is never stale.
    /// </remarks>
    public static float ClearanceAboveOrigin(Transform unitTransform, float groundOffset)
    {
        if (unitTransform == null)
            return groundOffset;

        float originY = unitTransform.position.y;
        bool foundPlate = false;
        float plateTop = originY;
        foreach (Renderer part in unitTransform.GetComponentsInChildren<Renderer>(true))
        {
            if (!part.gameObject.name.StartsWith(NamePrefix, System.StringComparison.Ordinal))
                continue;
            plateTop = foundPlate ? Mathf.Max(plateTop, part.bounds.max.y) : part.bounds.max.y;
            foundPlate = true;
        }

        if (foundPlate)
            return plateTop - originY + groundOffset;

        Collider unitCollider = unitTransform.GetComponent<Collider>();
        return (unitCollider != null ? unitCollider.bounds.min.y - originY : 0f) + groundOffset;
    }

    /// <summary>
    /// Height above a unit's own origin that clears the board itself by
    /// <paramref name="groundOffset"/>, for a visual that has to sit at a fixed height over the
    /// deck rather than hug whatever plate its unit happens to have.
    /// </summary>
    /// <remarks>
    /// The plate's underside is the board plane: the puck's rim rests on the deck, so its lowest
    /// renderer bound is the collider's bottom. It is read from the renderers anyway, for the
    /// reason given on <see cref="ClearanceAboveOrigin"/> — a collider's bounds lag a transform
    /// that moved this frame and a renderer's do not.
    /// </remarks>
    public static float BoardClearanceAboveOrigin(Transform unitTransform, float groundOffset)
    {
        if (unitTransform == null)
            return groundOffset;

        float originY = unitTransform.position.y;
        bool foundPlate = false;
        float plateBottom = originY;
        foreach (Renderer part in unitTransform.GetComponentsInChildren<Renderer>(true))
        {
            if (!part.gameObject.name.StartsWith(NamePrefix, System.StringComparison.Ordinal))
                continue;
            plateBottom = foundPlate
                ? Mathf.Min(plateBottom, part.bounds.min.y)
                : part.bounds.min.y;
            foundPlate = true;
        }

        if (foundPlate)
            return plateBottom - originY + groundOffset;

        Collider unitCollider = unitTransform.GetComponent<Collider>();
        return (unitCollider != null ? unitCollider.bounds.min.y - originY : 0f) + groundOffset;
    }
}

/// <summary>
/// Local-only Shield Rush presentation. A pulsing teal ground ring identifies the positive buff,
/// while three animated floor streaks trail opposite the unit's facing to communicate speed.
/// Runtime construction keeps the effect on every unit without prefab or scene dependencies.
/// </summary>
public sealed class SpeedBoostIndicatorVisual : MonoBehaviour
{
    public const string GameObjectName = "ShieldRushSpeedBoostIndicator";

    private const int StreakCount = 3;
    private const float GroundOffset = 0.08f;
    private const float RingDiameter = 2.15f;
    private const float StreakCyclesPerSecond = 1.8f;

    private static readonly Color BoostColor = new(0.08f, 1.15f, 1.35f, 0.72f);
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");

    private Material visualMaterial;
    private Mesh groundQuadMesh;
    private Renderer ringRenderer;
    private Transform[] streaks;
    private Renderer[] streakRenderers;
    private MaterialPropertyBlock[] streakPropertyBlocks;

    public bool IsVisible => gameObject.activeSelf;

    public static SpeedBoostIndicatorVisual Create(Transform unitTransform)
    {
        if (unitTransform == null)
            return null;

        bool forceRenderingOff = false;
        foreach (Renderer unitRenderer in unitTransform.GetComponentsInChildren<Renderer>(true))
        {
            if (unitRenderer.forceRenderingOff)
            {
                forceRenderingOff = true;
                break;
            }
        }

        Transform existing = unitTransform.Find(GameObjectName);
        if (
            existing != null
            && existing.TryGetComponent(out SpeedBoostIndicatorVisual existingVisual)
        )
        {
            return existingVisual;
        }

        Shader glowShader = Shader.Find("BattlePlan/GroundGlow");
        if (glowShader == null)
        {
            Debug.LogWarning(
                "[SpeedBoostIndicatorVisual] BattlePlan/GroundGlow shader not found; "
                    + "Shield Rush speed indicator disabled."
            );
            return null;
        }

        GameObject indicatorObject = new(GameObjectName);
        indicatorObject.layer = unitTransform.gameObject.layer;
        indicatorObject.SetActive(false);
        indicatorObject.transform.SetParent(unitTransform, worldPositionStays: false);

        SpeedBoostIndicatorVisual visual =
            indicatorObject.AddComponent<SpeedBoostIndicatorVisual>();
        visual.Build(unitTransform, glowShader);
        // Host fog cannot NetworkHide server-owned objects, so GameLoop suppresses their existing
        // renderers locally. Inherit that state before activation to avoid a one-frame information
        // leak when this visual is created while an enemy is hidden.
        visual.SetForceRenderingOff(forceRenderingOff);
        return visual;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    public void SetForceRenderingOff(bool forceRenderingOff)
    {
        if (ringRenderer != null)
            ringRenderer.forceRenderingOff = forceRenderingOff;

        if (streakRenderers == null)
            return;
        foreach (Renderer streakRenderer in streakRenderers)
        {
            if (streakRenderer != null)
                streakRenderer.forceRenderingOff = forceRenderingOff;
        }
    }

    private void Build(Transform unitTransform, Shader glowShader)
    {
        PositionAtUnitFeet(unitTransform);
        groundQuadMesh = CreateGroundQuadMesh();

        visualMaterial = new Material(glowShader)
        {
            name = "Shield Rush Speed Boost (Runtime)",
            hideFlags = HideFlags.DontSave,
            enableInstancing = true,
        };

        ringRenderer = CreateGroundQuad(
            "RushRing",
            Vector3.zero,
            new Vector3(RingDiameter, RingDiameter, 1f)
        );
        MaterialPropertyBlock ringProperties = new();
        SetProperties(
            ringProperties,
            ringWidth: 0.16f,
            edgeSoftness: 0.13f,
            intensity: 1.15f,
            pulseSpeed: 2.2f,
            pulseAmount: 0.34f
        );
        ringRenderer.SetPropertyBlock(ringProperties);

        streaks = new Transform[StreakCount];
        streakRenderers = new Renderer[StreakCount];
        streakPropertyBlocks = new MaterialPropertyBlock[StreakCount];
        for (int index = 0; index < StreakCount; index++)
        {
            Renderer streakRenderer = CreateGroundQuad(
                $"RushStreak{index + 1}",
                Vector3.zero,
                Vector3.one
            );
            MaterialPropertyBlock streakProperties = new();
            SetProperties(
                streakProperties,
                ringWidth: 1f,
                edgeSoftness: 0.48f,
                intensity: 0f,
                pulseSpeed: 0f,
                pulseAmount: 0f
            );
            streakRenderer.SetPropertyBlock(streakProperties);

            streaks[index] = streakRenderer.transform;
            streakRenderers[index] = streakRenderer;
            streakPropertyBlocks[index] = streakProperties;
        }
        AnimateStreaks();
    }

    private void Update()
    {
        AnimateStreaks();
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(visualMaterial);
        DestroyRuntimeObject(groundQuadMesh);
    }

    private void PositionAtUnitFeet(Transform unitTransform)
    {
        Vector3 parentScale = unitTransform.lossyScale;
        float parentScaleX = Mathf.Max(Mathf.Abs(parentScale.x), 0.001f);
        float parentScaleY = Mathf.Max(Mathf.Abs(parentScale.y), 0.001f);
        float parentScaleZ = Mathf.Max(Mathf.Abs(parentScale.z), 0.001f);

        Collider unitCollider = unitTransform.GetComponent<Collider>();
        float worldDownToGround =
            unitCollider != null ? unitCollider.bounds.extents.y : GroundOffset;
        transform.localPosition = new Vector3(
            0f,
            (
                -worldDownToGround
                + UnitBasePlate.ClearanceAbovePlane(unitTransform, GroundOffset)
            ) / parentScaleY,
            0f
        );
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(
            1f / parentScaleX,
            1f / parentScaleY,
            1f / parentScaleZ
        );
    }

    private Renderer CreateGroundQuad(string objectName, Vector3 position, Vector3 scale)
    {
        GameObject quad = new(objectName);
        quad.layer = gameObject.layer;
        quad.transform.SetParent(transform, worldPositionStays: false);
        quad.transform.localPosition = position;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = scale;

        MeshFilter meshFilter = quad.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = groundQuadMesh;
        MeshRenderer quadRenderer = quad.AddComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = visualMaterial;
        quadRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        quadRenderer.reflectionProbeUsage =
            UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return quadRenderer;
    }

    private static Mesh CreateGroundQuadMesh()
    {
        Mesh mesh = new()
        {
            name = "Shield Rush Speed Boost Quad (Runtime)",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeObject);
        else
            DestroyImmediate(runtimeObject);
    }

    private static void SetProperties(
        MaterialPropertyBlock properties,
        float ringWidth,
        float edgeSoftness,
        float intensity,
        float pulseSpeed,
        float pulseAmount
    )
    {
        properties.SetColor(GlowColorId, BoostColor);
        properties.SetFloat(RingWidthId, ringWidth);
        properties.SetFloat(EdgeSoftnessId, edgeSoftness);
        properties.SetFloat(IntensityId, intensity);
        properties.SetFloat(PulseSpeedId, pulseSpeed);
        properties.SetFloat(PulseAmountId, pulseAmount);
    }

    private void AnimateStreaks()
    {
        if (streaks == null)
            return;

        float basePhase = Mathf.Repeat(Time.time * StreakCyclesPerSecond, 1f);
        for (int index = 0; index < StreakCount; index++)
        {
            float phase = Mathf.Repeat(basePhase + (float)index / StreakCount, 1f);
            float fade = Mathf.Sin(phase * Mathf.PI);
            float lateralOffset = (index - 1) * 0.42f;
            float trailingOffset = Mathf.Lerp(0.12f, -0.95f, phase * phase);
            float streakLength = Mathf.Lerp(0.24f, 0.62f, fade);
            float streakWidth = index == 1 ? 0.14f : 0.11f;

            Transform streak = streaks[index];
            streak.localPosition = new Vector3(
                lateralOffset,
                0.012f + index * 0.002f,
                trailingOffset
            );
            streak.localScale = new Vector3(streakWidth, streakLength, 1f);

            MaterialPropertyBlock properties = streakPropertyBlocks[index];
            properties.SetFloat(IntensityId, Mathf.Lerp(0.12f, 0.92f, fade));
            streakRenderers[index].SetPropertyBlock(properties);
        }
    }
}
