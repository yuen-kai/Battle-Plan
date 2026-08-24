using System.Collections.Generic;
using UnityEngine;

public sealed class LightningBolt : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";

    public static readonly Color ElectricBlue = new(0.28f, 0.6f, 1f, 1f);

    // A wide halo carrying the hue and a thin filament carrying the heat. The board's floor is
    // bright enough that an additive line trends white on its own, so the blue only survives where
    // the glow is wide and the white core inside it is narrow.
    private const float CoreWidth = 0.06f;
    private const float GlowWidth = 0.46f;

    private const float ForkWidthShare = 0.55f;

    private const float CrackleInterval = 0.045f;

    private const float HoldShare = 0.28f;

    private const float StrikeSwell = 0.85f;
    private const float StrikeShare = 0.2f;

    private const float ThinShare = 0.45f;

    private const float LiftShare = 0.25f;

    private const float MinSegmentLength = 0.22f;

    private const int ForkSegments = 4;

    private const float ForkMinSpanCells = 0.6f;

    private readonly List<Material> ownedMaterials = new();

    private LineRenderer glowLine;
    private LineRenderer coreLine;
    private LineRenderer forkLine;

    private Vector3[] boltPoints;
    private Vector3[] forkPoints;

    private Vector3 origin;
    private Vector3 destination;

    private Vector3 heading;
    private Vector3 lateral;
    private Vector3 rise;

    private float span;
    private float amplitude;
    private int segments;
    private float lifetime;
    private float elapsed;
    private float nextCrackle;

    public static LightningBolt Spawn(
        Vector3 from,
        Vector3 to,
        Color color,
        float lifetime = 0.28f,
        float jaggedness = 0.35f,
        int segments = 12
    )
    {
        GameObject root = new("LightningBolt");
        root.transform.position = from;
        LightningBolt bolt = root.AddComponent<LightningBolt>();
        bolt.Build(from, to, color, lifetime, jaggedness, segments);
        return bolt;
    }

    private void Build(
        Vector3 from,
        Vector3 to,
        Color color,
        float boltLifetime,
        float jaggedness,
        int wantedSegments
    )
    {
        origin = from;
        destination = to;
        lifetime = Mathf.Max(0.05f, boltLifetime);
        span = Vector3.Distance(from, to);

        Shader beamShader = Shader.Find(BeamShaderName);
        if (beamShader == null)
        {
            Debug.LogWarning(
                $"[LightningBolt] {BeamShaderName} shader not found, falling back to Sprites/Default"
            );
            beamShader = Shader.Find("Sprites/Default");
        }

        if (beamShader == null || span < 0.01f)
        {
            Destroy(gameObject);
            return;
        }

        heading = (destination - origin) / span;
        lateral = Vector3.Cross(heading, Vector3.up);
        if (lateral.sqrMagnitude < 0.0001f)
            lateral = Vector3.right;
        lateral.Normalize();
        rise = Vector3.Cross(heading, lateral).normalized;

        float cell = GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
        amplitude = Mathf.Min(Mathf.Max(0f, jaggedness) * cell * 0.5f, span * 0.2f);

        segments = Mathf.Clamp(
            wantedSegments,
            3,
            Mathf.Max(3, Mathf.FloorToInt(span / MinSegmentLength))
        );
        boltPoints = new Vector3[segments + 1];

        glowLine = CreateLine(
            "Glow",
            beamShader,
            GlowWidth,
            color,
            color * 1.6f,
            coreShare: 0.14f,
            points: boltPoints.Length
        );
        coreLine = CreateLine(
            "Core",
            beamShader,
            CoreWidth,
            Color.Lerp(color, Color.white, 0.6f) * 2f,
            Color.white * 4f,
            coreShare: 0.55f,
            points: boltPoints.Length
        );

        if (span > cell * ForkMinSpanCells && segments >= 5)
        {
            forkPoints = new Vector3[ForkSegments + 1];
            forkLine = CreateLine(
                "Fork",
                beamShader,
                GlowWidth * ForkWidthShare,
                color,
                Color.Lerp(color, Color.white, 0.45f) * 1.8f,
                coreShare: 0.22f,
                points: forkPoints.Length
            );
        }

        Crackle();
        Apply(0f);
        nextCrackle = CrackleInterval;
    }

    private LineRenderer CreateLine(
        string childName,
        Shader shader,
        float width,
        Color glow,
        Color core,
        float coreShare,
        int points
    )
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, worldPositionStays: false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        Material material = new(shader);
        ownedMaterials.Add(material);
        if (material.HasProperty("_GlowColor"))
        {
            material.SetColor("_GlowColor", glow);
            material.SetColor("_CoreColor", core);
            material.SetFloat("_CoreWidth", coreShare);
            material.SetFloat("_ScrollSpeed", 9f);
            material.SetFloat("_NoiseScale", 16f);
            material.SetFloat("_NoiseStrength", 0.55f);
        }
        line.material = material;

        line.startColor = Color.white;
        line.endColor = Color.white;
        line.startWidth = width;
        line.endWidth = width;
        line.positionCount = points;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;

        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void Crackle()
    {
        boltPoints[0] = origin;
        boltPoints[segments] = destination;
        for (int i = 1; i < segments; i++)
        {
            float along = (float)i / segments;

            float taper = Mathf.Sin(along * Mathf.PI);
            boltPoints[i] = origin + heading * (span * along) + Swing(i, taper);
        }

        glowLine.SetPositions(boltPoints);
        coreLine.SetPositions(boltPoints);
        CrackleFork();
    }

    private Vector3 Swing(int index, float taper)
    {
        float sway = ((index & 1) == 0 ? 1f : -1f) * Random.Range(0.5f, 1f);
        float lift = Random.Range(-1f, 1f) * LiftShare;
        return (lateral * sway + rise * lift) * (amplitude * taper);
    }

    private void CrackleFork()
    {
        if (forkLine == null)
            return;

        int branch = Random.Range(Mathf.Max(1, segments / 4), Mathf.Max(2, segments * 2 / 3));
        Vector3 root = boltPoints[branch];
        Vector3 forkHeading = (
            heading * Random.Range(0.25f, 0.7f)
            + lateral * ((Random.value < 0.5f ? -1f : 1f) * Random.Range(0.6f, 1f))
            + rise * Random.Range(-0.12f, 0.18f)
        ).normalized;
        float reach = span * Random.Range(0.16f, 0.3f);

        for (int i = 0; i <= ForkSegments; i++)
        {
            float along = (float)i / ForkSegments;

            forkPoints[i] = root + forkHeading * (reach * along) + Swing(i, along * 0.6f);
        }
        forkLine.SetPositions(forkPoints);
    }

    private void LateUpdate()
    {
        if (boltPoints == null)
            return;

        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (elapsed >= nextCrackle)
        {
            Crackle();

            nextCrackle += CrackleInterval;
        }

        Apply(elapsed);
    }

    private void Apply(float time)
    {
        float fade = Falloff(time, lifetime, lifetime * HoldShare);
        float strike = 1f + StrikeSwell * (1f - Mathf.Clamp01(time / (lifetime * StrikeShare)));
        float width = strike * Mathf.Lerp(ThinShare, 1f, fade);

        SetLayer(glowLine, GlowWidth * width, fade);
        SetLayer(coreLine, CoreWidth * width, fade);

        SetLayer(forkLine, GlowWidth * ForkWidthShare * width, fade * fade);
    }

    private static void SetLayer(LineRenderer line, float width, float alpha)
    {
        if (line == null)
            return;

        line.startWidth = width;
        line.endWidth = width;

        Color level = new(1f, 1f, 1f, alpha);
        line.startColor = level;
        line.endColor = level;
    }

    private static float Falloff(float time, float life, float holdSeconds)
    {
        if (time <= holdSeconds)
            return 1f;
        if (time >= life)
            return 0f;
        float progress = (time - holdSeconds) / Mathf.Max(0.0001f, life - holdSeconds);
        return (1f - progress) * (1f - progress);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
                Destroy(ownedMaterials[i]);
        }
        ownedMaterials.Clear();
    }
}
