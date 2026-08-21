using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One jagged lightning arc between two world points, with a short fork branching off it. Voltaic's
/// bolt: <see cref="ChainSurge"/> fires one of these per enemy a cast reaches, and a weaker,
/// shorter-lived one is what a single zap from the hand should look like.
/// <para>
/// This carries its own LineRenderers rather than building on <see cref="BeamVFX"/>, and the
/// reason is geometry alone. Everything else here is BeamVFX's approach reused verbatim -- the same
/// BattlePlan/EnergyBeam shader, the same white-hot-core-inside-a-coloured-glow split across two
/// lines, the same HDR additive colours, the same Sprites/Default fallback -- so a bolt belongs to
/// the same visual language as the Area Lock laser rather than being a second energy look. But
/// BeamVFX draws a straight line and only a straight line: <c>SetPositions</c> writes exactly two
/// points, <c>Rush</c> rebuilds those into evenly spaced collinear ones, and both of its
/// LineRenderers are private, so there is no way in for a point list. Lightning is a jagged polyline
/// that is re-randomised while it burns, and teaching BeamVFX that would put a bolt's crackle clock
/// inside the beam every laser in the game shares.
/// </para>
/// <para>
/// Purely local and visual: no networking, no gameplay, no state anything else reads. Fire it on
/// every peer from a ClientRpc at the instant of the strike and forget it -- it drives its own
/// crackle and fade and destroys its own GameObject when it is spent, the same contract
/// <see cref="ImpactCore"/> and <see cref="ImpactShockwave"/> already keep.
/// </para>
/// <para>
/// Deliberately unparented, and its endpoints are world-space snapshots. A bolt outlives the frame
/// it was fired on and either end of it can belong to a unit that dies on that frame -- a client
/// deactivates a dead unit's GameObject (see <c>Health.OnAliveChanged</c>), which would take a
/// parented bolt's renderers off screen mid-strike and leave the kill unexplained.
/// </para>
/// </summary>
public sealed class LightningBolt : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";

    /// <summary>
    /// Electric blue-white, the colour this effect is written in. Pass it through
    /// <see cref="AbilityJuice.Hot"/> before handing it to <see cref="Spawn"/>: the EnergyBeam
    /// shader is additive and authored for HDR, so an LDR colour lands here as a dim smear that
    /// never reaches the scene's bloom threshold.
    /// </summary>
    public static readonly Color ElectricBlue = new(0.52f, 0.76f, 1f, 1f);

    // Widths in world units, both under BeamVFX's 0.08/0.5. A bolt is a thin, hot thread with a
    // haze around it; a laser is a beam with mass, and at the laser's width the jag stops reading
    // as a jag because consecutive segments overlap each other.
    private const float CoreWidth = 0.05f;
    private const float GlowWidth = 0.26f;

    /// <summary>
    /// The fork's share of the main bolt's width. Thinner and dimmer on purpose: a branch as strong
    /// as the arc it came off is a second bolt, and a cast that fires five of these at once has to
    /// stay countable.
    /// </summary>
    private const float ForkWidthShare = 0.55f;

    // How often the jag is re-rolled. Roughly six shapes across a default lifetime, which reads as
    // a crackle; re-rolling every frame is a strobe that reads as noise, and re-rolling twice over
    // the whole life is a bolt that visibly changes its mind.
    private const float CrackleInterval = 0.045f;

    // Share of the lifetime the bolt burns at full strength before it starts leaving, for the same
    // reason ImpactCore holds its peak: at the 30 Hz a capture samples at, a strike whose hottest
    // frame lasts one 60 Hz tick is caught on its decayed frame more often than on its good one.
    private const float HoldShare = 0.4f;

    // The strike itself. The frames where the bolt arrives are the widest it ever gets, then it
    // settles back to its nominal width -- the arrival has to be a different image from the burn.
    private const float StrikeSwell = 0.85f;
    private const float StrikeShare = 0.2f;

    /// <summary>
    /// Width left at the end of the fade. The bolt thins as it goes but never to nothing: alpha is
    /// what takes it off screen, and a line whose width has already collapsed has no pixels left
    /// for the alpha to act on.
    /// </summary>
    private const float ThinShare = 0.45f;

    /// <summary>
    /// How much of the swing the out-of-deck axis carries. The board camera looks down at the deck,
    /// so a vertical offset is foreshortened on screen while a ground-plane one is not -- splitting
    /// them evenly would spend most of the jag on the axis the player cannot see.
    /// </summary>
    private const float LiftShare = 0.7f;

    /// <summary>
    /// Shortest segment the jag is allowed, in world units. A point-blank zap asked for twelve
    /// segments is a scribble; this caps the count by the span so it stays a zigzag.
    /// </summary>
    private const float MinSegmentLength = 0.22f;

    private const int ForkSegments = 4;

    /// <summary>Below this span (in board cells) there is no room for a branch, so none is built.</summary>
    private const float ForkMinSpanCells = 0.6f;

    /// <summary>
    /// Every material this bolt instantiated, held so <see cref="OnDestroy"/> frees exactly the
    /// objects it created -- the same bookkeeping <see cref="ImpactShockwave"/> keeps, and the
    /// reason it is a list rather than a read back off the renderer: <c>Renderer.material</c> is a
    /// getter that can hand back a fresh instance, which would leak the original and destroy a copy.
    /// </summary>
    private readonly List<Material> ownedMaterials = new();

    private LineRenderer glowLine;
    private LineRenderer coreLine;
    private LineRenderer forkLine;

    private Vector3[] boltPoints;
    private Vector3[] forkPoints;

    private Vector3 origin;
    private Vector3 destination;

    // The bolt's own frame: where it is going, the ground-plane axis normal to that, and the
    // remaining axis out of the deck. The jag is written in this frame so it swings across the
    // bolt rather than along it.
    private Vector3 heading;
    private Vector3 lateral;
    private Vector3 rise;

    private float span;
    private float amplitude;
    private int segments;
    private float lifetime;
    private float elapsed;
    private float nextCrackle;

    /// <summary>
    /// Strikes a bolt from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">World start, e.g. the caster's hand anchor.</param>
    /// <param name="to">World end. The jag tapers to zero here, so the bolt lands on this point.</param>
    /// <param name="color">HDR bolt colour; see <see cref="ElectricBlue"/>.</param>
    /// <param name="lifetime">Seconds on screen, fade included.</param>
    /// <param name="jaggedness">Lateral swing at mid-span, as a fraction of half a board cell.</param>
    /// <param name="segments">Kinks along the bolt, capped down for short spans.</param>
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

        // Nothing to draw, and both cases are a caller's data rather than a bug worth an exception:
        // a stripped build with no fallback shader, or two endpoints that resolved to one point.
        if (beamShader == null || span < 0.01f)
        {
            Destroy(gameObject);
            return;
        }

        heading = (destination - origin) / span;
        lateral = Vector3.Cross(heading, Vector3.up);
        if (lateral.sqrMagnitude < 0.0001f)
            lateral = Vector3.right; // bolt runs straight up or down; any horizontal axis will do
        lateral.Normalize();
        rise = Vector3.Cross(heading, lateral).normalized;

        // Swing is stated in cells because a cell is the only unit the board is ever judged in, and
        // then held under a fifth of the bolt's own length so a short arc stays legible as an arc.
        float cell = GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
        amplitude = Mathf.Min(Mathf.Max(0f, jaggedness) * cell * 0.5f, span * 0.2f);

        segments = Mathf.Clamp(
            wantedSegments,
            3,
            Mathf.Max(3, Mathf.FloorToInt(span / MinSegmentLength))
        );
        boltPoints = new Vector3[segments + 1];

        // Glow carries the colour and no white at all; the white belongs to the core line, which is
        // a twentieth of its width. Two lines is how BeamVFX gets a hot thread inside a haze, and
        // the shader's own _CoreWidth then decides how much of each line is that thread.
        glowLine = CreateLine(
            "Glow",
            beamShader,
            GlowWidth,
            color,
            color * 1.6f,
            coreShare: 0.35f,
            points: boltPoints.Length
        );
        coreLine = CreateLine(
            "Core",
            beamShader,
            CoreWidth,
            Color.Lerp(color, Color.white, 0.6f) * 2f,
            Color.white * 4f,
            coreShare: 0.85f,
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
                coreShare: 0.5f,
                points: forkPoints.Length
            );
        }

        // Posed at full strength here rather than waited for. Spawn is called from a ClientRpc,
        // which is dispatched before this frame's LateUpdate, and a bolt that fades up is a bolt
        // whose first frame -- the frame it lands on -- is its weakest.
        Crackle();
        Apply(0f);
        nextCrackle = CrackleInterval;
    }

    /// <summary>
    /// One layer of the bolt. Mirrors <see cref="BeamVFX"/>'s own line setup, with the shader's
    /// shimmer driven harder: the scrolling noise is what keeps the bolt alive between re-jags
    /// instead of holding one shape still for 45 ms at a time.
    /// </summary>
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

        // Sharp mitred corners and flat ends: rounding a kink off is rounding off the one feature
        // that distinguishes a bolt from a beam.
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    /// <summary>
    /// Re-rolls the whole shape: a new jag along the bolt and a new branch off it. Called several
    /// times across the life so the arc crackles rather than being one frozen zigzag that fades.
    /// </summary>
    private void Crackle()
    {
        boltPoints[0] = origin;
        boltPoints[segments] = destination;
        for (int i = 1; i < segments; i++)
        {
            float along = (float)i / segments;

            // Zero at both ends, largest at mid-span. An offset at the endpoints is a bolt that
            // leaves from beside the hand and lands next to the target instead of on it, which is
            // the whole reason the effect exists.
            float taper = Mathf.Sin(along * Mathf.PI);
            boltPoints[i] = origin + heading * (span * along) + Swing(i, taper);
        }

        glowLine.SetPositions(boltPoints);
        coreLine.SetPositions(boltPoints);
        CrackleFork();
    }

    /// <summary>
    /// The lateral kick at one vertex, scaled by <paramref name="taper"/>.
    /// <para>
    /// Sides alternate rather than each vertex drawing its own direction. A zigzag is what reads as
    /// lightning; independent draws per vertex read as a wobbling rope, and often enough they read
    /// as a nearly straight line, which is exactly the frame this effect cannot afford to produce.
    /// </para>
    /// </summary>
    private Vector3 Swing(int index, float taper)
    {
        float sway = ((index & 1) == 0 ? 1f : -1f) * Random.Range(0.5f, 1f);
        float lift = Random.Range(-1f, 1f) * LiftShare;
        return (lateral * sway + rise * lift) * (amplitude * taper);
    }

    /// <summary>
    /// The branch. Leaves the bolt somewhere in its first two thirds and stops in mid-air: one
    /// bolt with a fork reads as lightning where a single zigzag reads as a drawn line, but a fork
    /// that reached the target too would read as a second bolt to the same enemy -- and a cast that
    /// arcs to five separate enemies has to stay countable.
    /// </summary>
    private void CrackleFork()
    {
        if (forkLine == null)
            return;

        int branch = Random.Range(Mathf.Max(1, segments / 4), Mathf.Max(2, segments * 2 / 3));
        Vector3 root = boltPoints[branch];
        Vector3 forkHeading = (
            heading * Random.Range(0.25f, 0.7f)
            + lateral * ((Random.value < 0.5f ? -1f : 1f) * Random.Range(0.6f, 1f))
            + rise * Random.Range(-0.35f, 0.6f)
        ).normalized;
        float reach = span * Random.Range(0.16f, 0.3f);

        for (int i = 0; i <= ForkSegments; i++)
        {
            float along = (float)i / ForkSegments;

            // Grows from nothing at the branch point -- it has to leave the bolt from a point that
            // is actually on the bolt -- and is free to wander by the open tip.
            forkPoints[i] = root + forkHeading * (reach * along) + Swing(i, along * 0.6f);
        }
        forkLine.SetPositions(forkPoints);
    }

    private void LateUpdate()
    {
        // Build gave up and destroyed this object, which does not take effect until the end of the
        // frame -- so there may still be a tick to survive with nothing built to drive.
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

            // Stepped rather than reset from `elapsed`, so a long frame cannot quietly stretch the
            // crackle out with it and turn the bolt into a slideshow.
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

        // The fork goes first. A branch outliving the arc it came off reads as a stray spark with
        // no source, and the main bolt is the thing that has to be the last to leave.
        SetLayer(forkLine, GlowWidth * ForkWidthShare * width, fade * fade);
    }

    private static void SetLayer(LineRenderer line, float width, float alpha)
    {
        if (line == null)
            return;

        line.startWidth = width;
        line.endWidth = width;

        // The EnergyBeam shader multiplies its output by the vertex colour's rgb and alpha, which
        // is the same channel BeamVFX.FadeOut fades on -- so this dims the bolt rather than
        // tinting it.
        Color level = new(1f, 1f, 1f, alpha);
        line.startColor = level;
        line.endColor = level;
    }

    /// <summary>
    /// Full strength for <paramref name="holdSeconds"/>, then a decaying tail to zero -- the same
    /// plateau-then-collapse shape <see cref="ImpactCore"/> holds its peak with, so a bolt and the
    /// flash at the end of it agree about which frame is the money frame.
    /// </summary>
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
        // A material was instantiated per line in CreateLine; free them, as BeamVFX does.
        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
                Destroy(ownedMaterials[i]);
        }
        ownedMaterials.Clear();
    }
}
