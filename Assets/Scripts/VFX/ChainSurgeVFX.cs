using System.Collections.Generic;
using UnityEngine;

public sealed class ChainSurgeVFX : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";
    private const int RingSegments = 56;
    private const float GroundHeight = 0.085f;
    private const float CancelFadeSeconds = 0.08f;

    private static readonly Dictionary<ulong, ChainSurgeVFX> ActiveCharges = new();

    private sealed class Ring
    {
        public LineRenderer Glow;
        public LineRenderer Core;
        public float StartTime;
        public float EndTime;
        public float StartRadius;
        public float EndRadius;
        public float GlowWidth;
        public float CoreWidth;
        public float Phase;
        public Vector3[] Points;
    }

    private readonly List<Ring> rings = new();
    private readonly List<Material> ownedMaterials = new();

    private bool charging;
    private float lifetime;
    private float elapsed;
    private float cancelStarted;
    private ulong chargeOwnerId;
    private bool registeredCharge;
    private bool canceling;

    public static void SpawnCharge(ulong casterId, Vector3 center, Color color, float duration)
    {
        CancelCharge(casterId);

        ChainSurgeVFX effect = Create("ChainSurgeCharge", center, color, duration, true);
        if (effect == null)
            return;

        effect.chargeOwnerId = casterId;
        effect.registeredCharge = true;
        ActiveCharges[casterId] = effect;

        float cell = CellSize();
        effect.AddRing(0f, duration, cell * 1.45f, cell * 0.28f, 0.16f, 0.045f, 0f);
        effect.AddRing(duration * 0.16f, duration, cell, cell * 0.16f, 0.1f, 0.03f, 1.7f);
        effect.ApplyFrame();
    }

    public static void CancelCharge(ulong casterId)
    {
        if (!ActiveCharges.TryGetValue(casterId, out ChainSurgeVFX effect))
            return;

        ActiveCharges.Remove(casterId);
        if (effect != null)
            effect.BeginCancel();
    }

    public static void SpawnDischarge(
        Vector3 center,
        Color color,
        float scale = 1f,
        float intensity = 1f
    )
    {
        const float duration = 0.34f;
        ChainSurgeVFX effect = Create("ChainSurgeDischarge", center, color, duration, false);
        if (effect == null)
            return;

        float cell = CellSize() * Mathf.Max(0.2f, scale) * Mathf.Lerp(0.8f, 1.15f, intensity);
        effect.AddRing(0f, duration * 0.8f, cell * 0.12f, cell * 1.55f, 0.2f, 0.055f, 0f);
        effect.AddRing(0.045f, duration * 0.9f, cell * 0.2f, cell * 1.28f, 0.13f, 0.038f, 1.2f);
        effect.AddRing(0.09f, duration, cell * 0.3f, cell, 0.08f, 0.024f, 2.4f);
        effect.ApplyFrame();
    }

    public static void SpawnStrike(
        Vector3 center,
        Color color,
        float scale = 1f,
        float intensity = 1f
    )
    {
        const float duration = 0.24f;
        ChainSurgeVFX effect = Create("ChainSurgeStrike", center, color, duration, false);
        if (effect == null)
            return;

        float cell = CellSize() * Mathf.Max(0.2f, scale) * Mathf.Lerp(0.72f, 1f, intensity);
        effect.AddRing(0f, duration, cell * 0.08f, cell * 0.62f, 0.12f, 0.035f, 0.5f);
        effect.AddRing(0.04f, duration, cell * 0.15f, cell * 0.42f, 0.07f, 0.02f, 2.1f);
        effect.ApplyFrame();
    }

    private static ChainSurgeVFX Create(
        string objectName,
        Vector3 center,
        Color color,
        float duration,
        bool isCharging
    )
    {
        Shader shader = Shader.Find(BeamShaderName);
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        GameObject root = new(objectName);
        root.transform.position = new Vector3(center.x, GroundHeight, center.z);

        ChainSurgeVFX effect = root.AddComponent<ChainSurgeVFX>();
        effect.charging = isCharging;
        effect.lifetime = Mathf.Max(0.05f, duration);
        effect.shader = shader;
        effect.color = color;
        return effect;
    }

    private Shader shader;
    private Color color;

    private void AddRing(
        float startTime,
        float endTime,
        float startRadius,
        float endRadius,
        float glowWidth,
        float coreWidth,
        float phase
    )
    {
        Ring ring = new()
        {
            StartTime = startTime,
            EndTime = endTime,
            StartRadius = startRadius,
            EndRadius = endRadius,
            GlowWidth = glowWidth,
            CoreWidth = coreWidth,
            Phase = phase,
            Points = new Vector3[RingSegments],
        };
        ring.Glow = CreateLine("Glow", glowWidth, color, color * 1.7f, 0.35f);
        ring.Core = CreateLine(
            "Core",
            coreWidth,
            Color.Lerp(color, Color.white, 0.65f) * 2f,
            Color.white * 4f,
            0.85f
        );
        rings.Add(ring);
    }

    private LineRenderer CreateLine(
        string layerName,
        float width,
        Color glow,
        Color core,
        float coreShare
    )
    {
        GameObject lineObject = new(layerName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        Material material = new(shader);
        ownedMaterials.Add(material);
        if (material.HasProperty("_GlowColor"))
        {
            material.SetColor("_GlowColor", glow);
            material.SetColor("_CoreColor", core);
            material.SetFloat("_CoreWidth", coreShare);
            material.SetFloat("_ScrollSpeed", charging ? 12f : 18f);
            material.SetFloat("_NoiseScale", 20f);
            material.SetFloat("_NoiseStrength", 0.62f);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", glow);
        }
        line.material = material;

        line.positionCount = RingSegments;
        line.loop = true;
        line.useWorldSpace = false;
        line.textureMode = LineTextureMode.Tile;
        line.alignment = LineAlignment.View;
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        ApplyFrame();
    }

    private void BeginCancel()
    {
        if (canceling)
            return;

        canceling = true;
        cancelStarted = elapsed;
        lifetime = Mathf.Min(lifetime, elapsed + CancelFadeSeconds);
    }

    private void ApplyFrame()
    {
        for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            Ring ring = rings[ringIndex];
            float progress = Mathf.Clamp01(
                (elapsed - ring.StartTime) / Mathf.Max(0.001f, ring.EndTime - ring.StartTime)
            );
            bool waiting = elapsed < ring.StartTime;
            float alpha = waiting ? 0f : ResolveAlpha(progress);
            if (canceling)
                alpha *= 1f - Mathf.Clamp01((elapsed - cancelStarted) / CancelFadeSeconds);
            float eased = charging ? progress * progress : 1f - (1f - progress) * (1f - progress);
            float radius = Mathf.Lerp(ring.StartRadius, ring.EndRadius, eased);

            SetRingPoints(ring, radius);
            SetLine(ring.Glow, ring.GlowWidth, alpha);
            SetLine(ring.Core, ring.CoreWidth, alpha);
        }
    }

    private float ResolveAlpha(float progress)
    {
        if (charging)
            return Mathf.SmoothStep(0.08f, 1f, Mathf.Clamp01(progress / 0.7f));
        return (1f - progress) * (1f - progress);
    }

    private void SetRingPoints(Ring ring, float radius)
    {
        float motion = elapsed * (charging ? 7f : 14f) + ring.Phase;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (float)i / RingSegments * Mathf.PI * 2f;
            float crackle =
                1f
                + Mathf.Sin(angle * 7f + motion) * 0.035f
                + Mathf.Sin(angle * 13f - motion * 1.35f) * 0.018f;
            float pointRadius = radius * crackle;
            ring.Points[i] = new Vector3(
                Mathf.Cos(angle) * pointRadius,
                0f,
                Mathf.Sin(angle) * pointRadius
            );
        }
        ring.Glow.SetPositions(ring.Points);
        ring.Core.SetPositions(ring.Points);
    }

    private static void SetLine(LineRenderer line, float width, float alpha)
    {
        line.startWidth = width * Mathf.Lerp(0.65f, 1.15f, alpha);
        line.endWidth = line.startWidth;
        Color level = new(1f, 1f, 1f, alpha);
        line.startColor = level;
        line.endColor = level;
    }

    private static float CellSize()
    {
        return GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
    }

    private void OnDestroy()
    {
        if (
            registeredCharge
            && ActiveCharges.TryGetValue(chargeOwnerId, out ChainSurgeVFX active)
            && active == this
        )
            ActiveCharges.Remove(chargeOwnerId);

        foreach (Material material in ownedMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        ownedMaterials.Clear();
    }
}
