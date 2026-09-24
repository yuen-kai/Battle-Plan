using System.Collections.Generic;
using UnityEngine;

public sealed class ArcSurgeVFX : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";
    private const int RingSegments = 56;
    private const float GroundHeight = 0.085f;
    private const float CancelFadeSeconds = 0.08f;

    private const float ArcSlowInterval = 0.17f;
    private const float ArcFastInterval = 0.028f;
    private const float ArcLifetime = 0.14f;
    private const float ArcJaggedness = 0.55f;
    private const int ArcSegments = 9;

    private const float GlowPeak = 18f;
    private const float GlowRangeShare = 1.4f;

    private const float StrikeScale = 0.9f;
    private const float StrikeIntensity = 1f;

    private static readonly Dictionary<ulong, ArcSurgeVFX> ActiveCharges = new();

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

    private Shader shader;
    private Color color;
    private Vector3 focus;
    private float reach;
    private Light glow;
    private float nextArc;

    public static void SpawnCharge(
        ulong casterId,
        Vector3 center,
        Vector3 focusPoint,
        Color color,
        float duration,
        float radius
    )
    {
        CancelCharge(casterId);

        ArcSurgeVFX effect = Create("ArcSurgeCharge", center, color, duration, true);
        if (effect == null)
            return;

        effect.chargeOwnerId = casterId;
        effect.registeredCharge = true;
        effect.focus = focusPoint;
        ActiveCharges[casterId] = effect;

        float cell = CellSize();
        float edge = Mathf.Max(radius, cell);
        effect.reach = edge;

        effect.AddRing(0f, duration, edge, edge, 0.32f, 0.05f, 0f);
        effect.AddRing(0f, duration, edge * 0.97f, cell * 0.3f, 0.4f, 0.06f, 0.9f);
        effect.AddRing(duration * 0.22f, duration, edge * 0.7f, cell * 0.18f, 0.26f, 0.04f, 2.1f);
        effect.AddRing(duration * 0.5f, duration, edge * 0.42f, cell * 0.12f, 0.18f, 0.03f, 3.4f);
        effect.CreateGlow();
        effect.ApplyFrame();
    }

    public static void CancelCharge(ulong casterId)
    {
        if (!ActiveCharges.TryGetValue(casterId, out ArcSurgeVFX effect))
            return;

        ActiveCharges.Remove(casterId);
        if (effect != null)
            effect.BeginCancel();
    }

    public static void SpawnDischarge(
        Vector3 center,
        Color color,
        float radius,
        float intensity = 1f
    )
    {
        const float duration = 0.42f;
        ArcSurgeVFX effect = Create("ArcSurgeDischarge", center, color, duration, false);
        if (effect == null)
            return;

        float cell = CellSize();
        float edge = Mathf.Max(radius, cell) * Mathf.Lerp(0.92f, 1.1f, Mathf.Clamp01(intensity));
        effect.AddRing(0f, duration * 0.72f, cell * 0.1f, edge * 1.04f, 0.55f, 0.085f, 0f);
        effect.AddRing(0.05f, duration * 0.86f, cell * 0.16f, edge * 0.78f, 0.38f, 0.055f, 1.2f);
        effect.AddRing(0.1f, duration, cell * 0.26f, edge * 0.52f, 0.24f, 0.036f, 2.4f);
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
        ArcSurgeVFX effect = Create("ArcSurgeStrike", center, color, duration, false);
        if (effect == null)
            return;

        float cell = CellSize() * Mathf.Max(0.2f, scale) * Mathf.Lerp(0.72f, 1f, intensity);
        effect.AddRing(0f, duration, cell * 0.08f, cell * 0.62f, 0.3f, 0.042f, 0.5f);
        effect.AddRing(0.04f, duration, cell * 0.15f, cell * 0.42f, 0.18f, 0.028f, 2.1f);
        effect.ApplyFrame();
    }

    /// <summary>
    /// Throws one arc from the caster's hand to each of <paramref name="strikePoints"/>, a beat
    /// apart, restrikes the whole fan <paramref name="bursts"/> times, then leaves the victims
    /// crackling for as long as the stun holds them.
    /// </summary>
    public static void SpawnArcs(
        Vector3 origin,
        Vector3[] strikePoints,
        Color color,
        float boltLifetime,
        float staggerSeconds,
        int bursts,
        float burstSeconds,
        float crackleSeconds
    )
    {
        if (strikePoints == null || strikePoints.Length == 0)
            return;

        GameObject root = new("ArcSurgeArcs");
        root.transform.position = origin;
        root.AddComponent<ArcRunner>()
            .Begin(
                origin,
                strikePoints,
                color,
                boltLifetime,
                staggerSeconds,
                bursts,
                burstSeconds,
                crackleSeconds
            );
    }

    private sealed class ArcRunner : MonoBehaviour
    {
        private const float MainJaggedness = 0.4f;
        private const int MainSegments = 15;
        private const float StrandJaggedness = 0.85f;
        private const int StrandSegments = 8;
        private const float StrandLifetimeShare = 0.4f;
        private const float MinBoltLifetime = 0.1f;

        // Each restrike lands lighter than the one before it, so the surge decays instead of
        // pulsing at one level until it is cut off.
        private const float BurstFalloff = 0.75f;

        private const float HitCoreShare = 0.24f;
        private const float HitCorePunch = 0.5f;

        private const float CrackleInterval = 0.055f;
        private const float CrackleLifetime = 0.13f;
        private const float CrackleJaggedness = 0.9f;
        private const int CrackleSegments = 5;
        private const float CrackleReachCells = 0.8f;

        private Vector3 origin;
        private Vector3[] targets;
        private Color color;
        private float boltLifetime;
        private float staggerSeconds;
        private float burstSeconds;
        private float crackleSeconds;
        private int bursts;
        private float elapsed;
        private float nextCrackle;
        private float surgeEnd;
        private int burst;
        private int thrown;

        public void Begin(
            Vector3 hand,
            Vector3[] strikePoints,
            Color boltColor,
            float lifetime,
            float stagger,
            int burstCount,
            float secondsBetweenBursts,
            float crackle
        )
        {
            origin = hand;
            targets = strikePoints;

            color = boltColor;
            boltLifetime = Mathf.Max(MinBoltLifetime, lifetime);
            staggerSeconds = Mathf.Max(0f, stagger);
            bursts = Mathf.Max(1, burstCount);
            burstSeconds = Mathf.Max(staggerSeconds * targets.Length, secondsBetweenBursts);
            crackleSeconds = Mathf.Max(0f, crackle);
            surgeEnd = burstSeconds * (bursts - 1) + boltLifetime;

            Throw();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            while (burst < bursts && elapsed >= BurstStart(burst) + thrown * staggerSeconds)
                Throw();

            if (elapsed >= nextCrackle && elapsed < surgeEnd + crackleSeconds)
            {
                Crackle();
                nextCrackle = elapsed + CrackleInterval;
            }

            if (elapsed >= surgeEnd + crackleSeconds)
                Destroy(gameObject);
        }

        private float BurstStart(int index) => index * burstSeconds;

        private void Throw()
        {
            Strike(targets[thrown++]);
            if (thrown < targets.Length)
                return;

            burst++;
            thrown = 0;
        }

        private void Strike(Vector3 target)
        {
            // Every arc of a burst gets the same life rather than a shared death frame: they leave
            // the hand milliseconds apart, and matching their deaths instead made the last one
            // thrown several times brighter than the first for the whole of the burst.
            float level = Mathf.Pow(BurstFalloff, burst);
            float life = Mathf.Max(MinBoltLifetime, boltLifetime * (0.6f + 0.4f * level));

            LightningBolt.Spawn(origin, target, color, life, MainJaggedness, MainSegments);
            LightningBolt.Spawn(
                origin,
                target,
                color,
                life * StrandLifetimeShare,
                StrandJaggedness,
                StrandSegments
            );

            SpawnStrike(target, color, StrikeScale * level, StrikeIntensity * level);
            if (burst == 0)
                ImpactCore.Spawn(target, color, CellSize() * HitCoreShare, HitCorePunch);
        }

        private void Crackle()
        {
            Vector3 node = targets[Random.Range(0, targets.Length)];
            Vector3 offset = new(
                Random.Range(-1f, 1f),
                Random.Range(-0.5f, 0.9f),
                Random.Range(-1f, 1f)
            );

            LightningBolt.Spawn(
                node + offset.normalized * (CellSize() * CrackleReachCells),
                node,
                color,
                CrackleLifetime,
                CrackleJaggedness,
                CrackleSegments
            );
        }
    }

    private static ArcSurgeVFX Create(
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

        ArcSurgeVFX effect = root.AddComponent<ArcSurgeVFX>();
        effect.charging = isCharging;
        effect.lifetime = Mathf.Max(0.05f, duration);
        effect.shader = shader;
        effect.color = color;
        return effect;
    }

    private void CreateGlow()
    {
        GameObject lightObject = new("Glow");
        lightObject.transform.SetParent(transform, worldPositionStays: false);
        lightObject.transform.position = focus;

        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = Normalized(color);
        glow.range = reach * GlowRangeShare;
        glow.shadows = LightShadows.None;
        glow.intensity = 0f;
    }

    private static Color Normalized(Color hot)
    {
        float peak = Mathf.Max(hot.r, Mathf.Max(hot.g, hot.b));
        return peak > 1f ? new Color(hot.r / peak, hot.g / peak, hot.b / peak) : hot;
    }

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
        ring.Glow = CreateLine("Glow", glowWidth, color, color * 1.7f, 0.14f);
        ring.Core = CreateLine(
            "Core",
            coreWidth,
            Color.Lerp(color, Color.white, 0.65f) * 2f,
            Color.white * 4f,
            0.55f
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

        if (charging)
            UpdateCharge();

        ApplyFrame();
    }

    private void UpdateCharge()
    {
        float ramp = Mathf.Clamp01(elapsed / lifetime);

        if (glow != null)
            glow.intensity = GlowPeak * ramp * ramp * Random.Range(0.78f, 1.18f) * CancelLevel();

        if (canceling || elapsed < nextArc)
            return;

        nextArc = elapsed + Mathf.Lerp(ArcSlowInterval, ArcFastInterval, ramp);

        float angle = Random.value * Mathf.PI * 2f;
        float distance = Mathf.Lerp(reach, reach * 0.3f, ramp) * Random.Range(0.45f, 1f);
        Vector3 ground = transform.position
            + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

        LightningBolt.Spawn(ground, focus, color, ArcLifetime, ArcJaggedness, ArcSegments);
    }

    private void BeginCancel()
    {
        if (canceling)
            return;

        canceling = true;
        cancelStarted = elapsed;
        lifetime = Mathf.Min(lifetime, elapsed + CancelFadeSeconds);
    }

    private float CancelLevel()
    {
        if (!canceling)
            return 1f;
        return 1f - Mathf.Clamp01((elapsed - cancelStarted) / CancelFadeSeconds);
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
            float alpha = waiting ? 0f : ResolveAlpha(progress) * CancelLevel();
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
        {
            float level = Mathf.SmoothStep(0.08f, 1f, Mathf.Clamp01(progress / 0.7f));
            return level * Mathf.Lerp(0.72f, 1f, Mathf.PerlinNoise(elapsed * 16f, 0f));
        }
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
                + Mathf.Sin(angle * 7f + motion) * 0.055f
                + Mathf.Sin(angle * 13f - motion * 1.35f) * 0.032f
                + Mathf.Sin(angle * 23f + motion * 2.1f) * 0.014f;
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
            && ActiveCharges.TryGetValue(chargeOwnerId, out ArcSurgeVFX active)
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
