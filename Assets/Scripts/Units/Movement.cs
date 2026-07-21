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

    private Coroutine moveListRoutine;
    private Coroutine moveRoutine;
    private Coroutine rotateRoutine;

    private AnimationHandler animator;
    private TimedMoveSpeedBoost temporaryMoveSpeedBoost;
    private SpeedBoostIndicatorVisual speedBoostIndicator;
    private bool speedBoostIndicatorCreationAttempted;

    // NetworkVariable (not ClientRpc) so a unit revealed by fog midway through the boost
    // reconstructs the current presentation state. Gameplay speed remains server-only above.
    private readonly NetworkVariable<bool> temporaryMoveSpeedBoostPresentationActive = new(false);

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
    }

    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        PauseMovement();
        moveListRoutine = StartCoroutine(MoveToCells(cells, dive));
    }

    public void transitionToShooting()
    {
        PauseMovement();
        moving = false;

        transform.GetComponent<Shooting>().StartShooting();
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

        transitionToShooting();
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

    // HELPER
    //public Vector2Int ConvertToGridCoords(Vector3 position)
    //{
    //    int x = Mathf.RoundToInt(position.x / GameLoop.cellSize);
    //    int z = Mathf.RoundToInt(position.z / GameLoop.cellSize);
    //    return new Vector2Int(x, z);
    //}
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
            (-worldDownToGround + GroundOffset) / parentScaleY,
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
