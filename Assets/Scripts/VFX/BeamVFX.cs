using System.Collections;
using UnityEngine;

/// <summary>
/// Layered energy beam built from LineRenderers on the BattlePlan/EnergyBeam shader. Purely local
/// and visual — spawn it on each peer (e.g. from the existing ClientRpcs in AreaLock/Shooting)
/// instead of the old Sprites/Default LineRenderers.
///
/// Typical use:
///   BeamVFX beam = BeamVFX.Create(transform, start, end, teamColor);
///   beam.Pulse();                        // idle threat loop
///   yield return beam.Rush(0.35f);       // firing surge along the beam
///   yield return beam.FadeOut(0.2f);     // cleanup (destroys itself)
///
/// THREE LAYERS, drawn back to front (ArtDirection §9.2.1, §9.4, §4.3):
///   Contour  widest,   Alpha, ink   — the 2px ink edge the contour law requires
///   Body     mid,      Alpha        — the line scored across the board
///   Core     thinnest, Alpha        — the filament inside it
///
/// ALL THREE ARE ALPHA, and that is the whole point. A beam is drawn at unit height across the
/// full board, so at a 73 degree camera it overlaps deck, cover, units and the rail within a single
/// draw. Any composite mode that behaves differently per host — multiply, or a runtime switch
/// between multiply and alpha — puts a seam in the beam exactly where it crosses a cover block, and
/// a player reads that seam as the beam doing something different at that cell. §9.2.1 rules that
/// out: an effect must never change appearance along its own length. Alpha at 0.85 in a near-ink
/// colour is within a hair of the multiply form over pale deck anyway, and it is identical
/// everywhere else, so the constant-appearance version costs nothing to look at.
/// </summary>
public class BeamVFX : MonoBehaviour
{
    [Header("Widths (world units)")]
    public float coreWidth = 0.05f;
    public float glowWidth = 0.34f;

    /// <summary>The ink edge outside the body. 1.24x, which puts it just over the two screen
    /// pixels the contour law asks for at the tactical camera.</summary>
    public float contourWidthMultiplier = 1.24f;

    /// <summary>
    /// Floor on the core's width. The authored 0.05 is about one screen pixel at the tactical
    /// camera, and a one-pixel line shimmers as the beam swings. The floor is the same two screen
    /// pixels §4.3 requires of an ink contour, for the same reason: below it a line stops being a
    /// line and starts being aliasing.
    /// </summary>
    public const float MinCoreWidth = 0.09f;

    [Header("Rush surge")]
    public float rushWidthMultiplier = 3.5f;

    public Color glowColor = new(1f, 0.14f, 0.25f, 1f);

    /// <summary>The filament inside the body. Alpha like everything else in the stack, so it does
    /// not brighten where the beam happens to cross something dark.</summary>
    public Color coreColor = FXPalette.Paper;

    // === Snap-on and fade envelope ===
    private const float SnapOnSeconds = 0.05f;
    private const float SnapOnOvershoot = 1.6f;

    /// <summary>The contour outlives the body, so the last thing on screen is a dark streak. At
    /// high key a trailing darkness sells violence the way a trailing glow does at low key.</summary>
    private const float ContourFadeTail = 0.06f;

    private const float RushBulgeWidth = 0.15f;
    private const float RushImpactHoldSeconds = 0.08f;
    private const float RushImpactWidthFraction = 0.75f;

    /// <summary>One line of the stack. Base width is its unmultiplied width in world units.</summary>
    private sealed class Layer
    {
        public LineRenderer Line;
        public Material Material;
        public float BaseWidth;
        public bool IsContour;
    }

    private Layer[] layers = System.Array.Empty<Layer>();
    private Coroutine pulseRoutine;

    /// <summary>Creates a beam from start to end, parented under <paramref name="parent"/>.</summary>
    public static BeamVFX Create(
        Transform parent,
        Vector3 start,
        Vector3 end,
        Color teamGlowColor,
        Color? hotCoreColor = null
    )
    {
        GameObject beamObject = new("BeamVFX");
        beamObject.transform.SetParent(parent, worldPositionStays: true);
        BeamVFX beam = beamObject.AddComponent<BeamVFX>();
        beam.glowColor = teamGlowColor;
        if (hotCoreColor.HasValue)
            beam.coreColor = hotCoreColor.Value;
        beam.Build();
        beam.SetPositions(start, end);
        beam.StartCoroutine(beam.SnapOn());
        return beam;
    }

    private void Build()
    {
        Shader beamShader = Shader.Find("BattlePlan/EnergyBeam");
        if (beamShader == null)
        {
            Debug.LogWarning(
                "[BeamVFX] BattlePlan/EnergyBeam shader not found, falling back to Sprites/Default"
            );
            beamShader = Shader.Find("Sprites/Default");
        }

        layers = new Layer[3];
        layers[0] = CreateLayer(
            "Contour",
            beamShader,
            glowWidth * contourWidthMultiplier,
            FXPalette.Ink,
            intensity: 1f,
            FXPalette.ContourComposite,
            sortingOrder: 0,
            isContour: true
        );
        layers[1] = CreateLayer(
            "Body",
            beamShader,
            glowWidth,
            glowColor,
            intensity: 1f,
            FXPalette.AbovePlaneComposite,
            sortingOrder: 1,
            isContour: false
        );
        layers[2] = CreateLayer(
            "Core",
            beamShader,
            Mathf.Max(coreWidth, MinCoreWidth),
            coreColor,
            intensity: 1f,
            FXPalette.AbovePlaneComposite,
            sortingOrder: 2,
            isContour: false
        );
    }

    private Layer CreateLayer(
        string childName,
        Shader shader,
        float width,
        Color color,
        float intensity,
        float compositeMode,
        int sortingOrder,
        bool isContour
    )
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, worldPositionStays: false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();

        Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_GlowColor"))
        {
            // Both terms take the layer's own colour. Each line in the stack is one job and one
            // value; letting _CoreColor default to white would smuggle a white filament into the
            // ink contour and the darkening body, which is the exact thing FIELD DAY removes.
            material.SetColor("_GlowColor", color * intensity);
            material.SetColor("_CoreColor", color * intensity);
        }
        if (material.HasProperty("_CompositeMode"))
            FXPalette.Composite.Apply(material, compositeMode);

        // sharedMaterial, not material: assigning through the `material` property and then reading
        // it back in OnDestroy instantiates a second copy and leaks the first.
        line.sharedMaterial = material;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.startWidth = width;
        line.endWidth = width;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        // All three share a render queue, so draw order has to be stated rather than inferred.
        line.sortingOrder = sortingOrder;

        return new Layer
        {
            Line = line,
            Material = material,
            BaseWidth = width,
            IsContour = isContour,
        };
    }

    public void SetPositions(Vector3 start, Vector3 end)
    {
        foreach (Layer layer in layers)
        {
            if (layer.Line == null)
                continue;
            layer.Line.positionCount = 2;
            layer.Line.SetPosition(0, start);
            layer.Line.SetPosition(1, end);
        }
    }

    /// <summary>
    /// One-frame width overshoot settling to full. A beam that grows in reads as a beam that
    /// arrived slowly; a beam that overshoots and settles reads as one that snapped into place.
    /// </summary>
    private IEnumerator SnapOn()
    {
        float elapsed = 0f;
        while (elapsed < SnapOnSeconds)
        {
            float progress = Mathf.Clamp01(elapsed / SnapOnSeconds);
            ApplyWidthMultiplier(Mathf.Lerp(SnapOnOvershoot, 1f, progress));
            elapsed += Time.deltaTime;
            yield return null;
        }
        ApplyWidthMultiplier(1f);
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

    private IEnumerator PulseLoop(float speed, float amount)
    {
        while (true)
        {
            float multiplier =
                1f + amount * 0.5f * (1f + Mathf.Sin(Time.time * speed * Mathf.PI * 2f));
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
        if (layers.Length == 0 || layers[0].Line == null)
            yield break;

        Vector3 start = layers[0].Line.GetPosition(0);
        Vector3 end = layers[0].Line.GetPosition(1);

        foreach (Layer layer in layers)
        {
            if (layer.Line == null)
                continue;
            layer.Line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
                layer.Line.SetPosition(i, Vector3.Lerp(start, end, (float)i / segments));
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            foreach (Layer layer in layers)
            {
                if (layer.Line == null)
                    yield break;

                AnimationCurve curve = new();
                for (int i = 0; i <= segments; i++)
                {
                    float segmentProgress = (float)i / segments;
                    // Bulge centered on the rush front, ~15% of beam length wide
                    float distanceToFront = Mathf.Abs(progress - segmentProgress);
                    float bulge = Mathf.Clamp01(1f - distanceToFront / RushBulgeWidth);
                    float multiplier = Mathf.Lerp(1f, rushWidthMultiplier, bulge * bulge);
                    curve.AddKey(segmentProgress, layer.BaseWidth * multiplier);
                }
                layer.Line.widthCurve = curve;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Impact frame: whole beam flashes wide for a beat
        ApplyWidthMultiplier(rushWidthMultiplier * RushImpactWidthFraction);
        yield return new WaitForSeconds(RushImpactHoldSeconds);
        ApplyWidthMultiplier(1f);
    }

    private void ApplyWidthMultiplier(float multiplier)
    {
        foreach (Layer layer in layers)
        {
            if (layer.Line != null)
            {
                layer.Line.widthCurve = AnimationCurve.Constant(
                    0f,
                    1f,
                    layer.BaseWidth * multiplier
                );
            }
        }
    }

    /// <summary>Fades the beam to nothing and destroys the whole BeamVFX object.</summary>
    public IEnumerator FadeOut(float duration = 0.2f)
    {
        StopPulse();
        float total = duration + ContourFadeTail;
        float elapsed = 0f;
        while (elapsed < total)
        {
            foreach (Layer layer in layers)
            {
                if (layer.Line == null)
                    continue;

                float span = layer.IsContour ? total : duration;
                float alpha = Mathf.Clamp01(1f - elapsed / span);
                layer.Line.startColor = layer.Line.endColor = new Color(1f, 1f, 1f, alpha);
                layer.Line.widthCurve = AnimationCurve.Constant(
                    0f,
                    1f,
                    layer.BaseWidth * alpha
                );
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (Layer layer in layers)
        {
            if (layer.Material != null)
                Destroy(layer.Material);
        }
    }
}
