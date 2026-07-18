using System.Collections;
using UnityEngine;

/// <summary>
/// Two-layer energy beam (white-hot core + colored glow) built from LineRenderers using the
/// BattlePlan/EnergyBeam shader. Purely local/visual — spawn it on each peer (e.g. from the
/// existing ClientRpcs in AreaLock/Shooting) instead of the old Sprites/Default LineRenderers.
///
/// Typical use:
///   BeamVFX beam = BeamVFX.Create(transform, start, end, teamColor);
///   beam.Pulse();                        // idle threat loop
///   yield return beam.Rush(0.35f);       // firing surge along the beam
///   yield return beam.FadeOut(0.2f);     // cleanup (destroys itself)
/// </summary>
public class BeamVFX : MonoBehaviour
{
    [Header("Widths (world units)")]
    public float coreWidth = 0.08f;
    public float glowWidth = 0.5f;

    [Header("Rush surge")]
    public float rushWidthMultiplier = 3.5f;

    public Color glowColor = new(1f, 0.14f, 0.25f, 1f);

    static readonly Color CoreColor = Color.white;

    LineRenderer coreLine;
    LineRenderer glowLine;
    Coroutine pulseRoutine;

    /// <summary>Creates a beam from start to end, parented under <paramref name="parent"/>.</summary>
    public static BeamVFX Create(Transform parent, Vector3 start, Vector3 end, Color teamGlowColor)
    {
        GameObject beamObject = new("BeamVFX");
        beamObject.transform.SetParent(parent, worldPositionStays: true);
        BeamVFX beam = beamObject.AddComponent<BeamVFX>();
        beam.glowColor = teamGlowColor;
        beam.Build();
        beam.SetPositions(start, end);
        return beam;
    }

    void Build()
    {
        Shader beamShader = Shader.Find("BattlePlan/EnergyBeam");
        if (beamShader == null)
        {
            Debug.LogWarning("[BeamVFX] BattlePlan/EnergyBeam shader not found, falling back to Sprites/Default");
            beamShader = Shader.Find("Sprites/Default");
        }

        glowLine = CreateLine("Glow", beamShader, glowWidth, glowColor, intensity: 1f);
        coreLine = CreateLine("Core", beamShader, coreWidth, CoreColor, intensity: 2.5f);
    }

    LineRenderer CreateLine(string childName, Shader shader, float width, Color color, float intensity)
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, worldPositionStays: false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        Material material = new(shader);
        if (material.HasProperty("_GlowColor"))
        {
            material.SetColor("_GlowColor", color * intensity);
            material.SetColor("_CoreColor", Color.white * intensity * 2f);
        }
        line.material = material;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.startWidth = width;
        line.endWidth = width;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    public void SetPositions(Vector3 start, Vector3 end)
    {
        coreLine.positionCount = 2;
        glowLine.positionCount = 2;
        coreLine.SetPosition(0, start);
        coreLine.SetPosition(1, end);
        glowLine.SetPosition(0, start);
        glowLine.SetPosition(1, end);
    }

    /// <summary>Slow menace pulse while the beam is armed but not firing.</summary>
    public void Pulse(float speed = 1.2f, float amount = 0.35f)
    {
        StopPulse();
        pulseRoutine = StartCoroutine(PulseLoop(speed, amount));
    }

    public void StopPulse()
    {
        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
            ApplyWidthMultiplier(1f);
        }
    }

    IEnumerator PulseLoop(float speed, float amount)
    {
        while (true)
        {
            float multiplier = 1f + amount * 0.5f * (1f + Mathf.Sin(Time.time * speed * Mathf.PI * 2f));
            ApplyWidthMultiplier(multiplier);
            yield return null;
        }
    }

    /// <summary>
    /// Firing surge: a fat bulge travels start->end over <paramref name="duration"/>, ending on
    /// full-width impact. Await this, then spawn ImpactShockwave + HitFlash at the target.
    /// </summary>
    public IEnumerator Rush(float duration, int segments = 24)
    {
        StopPulse();
        Vector3 start = glowLine.GetPosition(0);
        Vector3 end = glowLine.GetPosition(1);

        coreLine.positionCount = segments + 1;
        glowLine.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)i / segments);
            coreLine.SetPosition(i, point);
            glowLine.SetPosition(i, point);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            AnimationCurve coreCurve = new();
            AnimationCurve glowCurve = new();
            for (int i = 0; i <= segments; i++)
            {
                float segmentProgress = (float)i / segments;
                // Bulge centered on the rush front, ~15% of beam length wide
                float distanceToFront = Mathf.Abs(progress - segmentProgress);
                float bulge = Mathf.Clamp01(1f - distanceToFront / 0.15f);
                float multiplier = Mathf.Lerp(1f, rushWidthMultiplier, bulge * bulge);
                coreCurve.AddKey(segmentProgress, coreWidth * multiplier);
                glowCurve.AddKey(segmentProgress, glowWidth * multiplier);
            }
            coreLine.widthCurve = coreCurve;
            glowLine.widthCurve = glowCurve;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Impact frame: whole beam flashes wide for a beat
        ApplyWidthMultiplier(rushWidthMultiplier * 0.75f);
        yield return new WaitForSeconds(0.08f);
        ApplyWidthMultiplier(1f);
    }

    void ApplyWidthMultiplier(float multiplier)
    {
        if (coreLine == null || glowLine == null)
            return;
        coreLine.widthCurve = AnimationCurve.Constant(0f, 1f, coreWidth * multiplier);
        glowLine.widthCurve = AnimationCurve.Constant(0f, 1f, glowWidth * multiplier);
    }

    /// <summary>Fades the beam to nothing and destroys the whole BeamVFX object.</summary>
    public IEnumerator FadeOut(float duration = 0.2f)
    {
        StopPulse();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = 1f - elapsed / duration;
            Color faded = new(1f, 1f, 1f, alpha);
            coreLine.startColor = coreLine.endColor = faded;
            glowLine.startColor = glowLine.endColor = faded;
            ApplyWidthMultiplier(alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // Materials were instantiated in Build(); free them.
        if (coreLine != null)
            Destroy(coreLine.material);
        if (glowLine != null)
            Destroy(glowLine.material);
    }
}
