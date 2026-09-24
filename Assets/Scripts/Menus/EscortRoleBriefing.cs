using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Plays the screen that opens an escort leg: on leg 1 a coin decides which role this seat holds
/// and lands on it, and on every leg the role itself is put across the rail in one word.
///
/// The coin is drawn with <see cref="Painter2D"/> rather than assembled from elements, for the
/// same reason <see cref="BattleReportBoard"/> is: a two-faced disc with a milled rim, a contact
/// shadow and a motion trail is a dozen shapes whose geometry all derives from one radius and one
/// spin angle, and rebuilding that as a element tree every frame churns layout for a picture that
/// never accepts input. The one thing Painter2D cannot draw is the word stamped on the face, so
/// that stays a label, squashed in step with the face under it.
///
/// The outcome is decided on the server and arrives as <see cref="EscortRole"/>. Nothing here
/// draws anything: the spin lands on an even number of half turns, so the face that was up when
/// the toss began is the face that is up when it stops.
/// </summary>
public sealed class EscortRoleBriefing
{
    // Timeline, in real seconds from the first frame of the briefing. The coin variant's stages
    // run in sequence; the card variant skips straight to the verdict.
    /// <summary>
    /// The screen's entry. The page fading up and the curtain going opaque behind it both run on
    /// this one clock, so the board is covered the instant it elapses and the two can never fall
    /// out of step. Public because the HUD times the deployment card's retirement against it: that
    /// card is only taken down once this screen is opaque over it.
    /// </summary>
    public const float EntrySeconds = 0.12f;

    private const float ExitSeconds = 0.14f;
    private const float CoinRiseSeconds = 0.34f;
    private const float CoinWindUpSeconds = 0.16f;
    private const float CoinTossSeconds = 2.3f;
    private const float CoinLandSeconds = 0.24f;
    private const float CoinTossAt = CoinRiseSeconds + CoinWindUpSeconds;
    private const float CoinLandAt = CoinTossAt + CoinTossSeconds;
    private const float CoinSettleSeconds = 0.5f;
    private const float ImpactSeconds = 0.55f;
    private const float CoinVerdictAt = CoinLandAt + 0.12f;
    private const float CardVerdictAt = 0.12f;
    private const float VerdictSeconds = 0.55f;
    private const float BriefDelaySeconds = 0.18f;
    private const float BriefSeconds = 0.42f;

    /// <summary>How long the curtain takes to ease away once the board behind it is ready.</summary>
    private const float CurtainLiftSeconds = 0.3f;

    /// <summary>Even, so the toss ends on the face it started on — the one the server chose.</summary>
    private const float HalfTurns = 14f;

    private const int EllipseSegments = 56;
    private const int KnurlTicks = 44;
    private const float CoinRadiusFraction = 0.78f;
    private const float CoinThicknessFraction = 0.13f;

    /// <summary>How far above the stage's centre the coin rests, leaving room for its shadow.</summary>
    private const float CoinRestOffsetFraction = 0.1f;

    // The two faces, taken from the board rather than picked to look like coins. Protecting is the
    // objective, so its face is the bone plate the board's cover wears (--toy-warm) under the
    // objective's own orange (--toy-primary); defending is the wall under the pale cap that catches
    // the sky (Map_WallCap).
    //
    // The defending face is Map_Cover walked up rather than the colour itself. The briefing sits on
    // a scrim over the board, and at #46525A the disc separated from that page by 1.2-1.5:1 — a
    // dark coin lost on a dark table. Lifted, it reaches 1.8-2.1:1, which is better but is not
    // what makes the disc findable: the rim, its milling and the inner ring are all Map_WallCap and
    // run 5.6-6.7:1 against the same page, so the shape is bounded by line rather than by fill.
    // That is the arrangement the whole interface uses; the lift is only there so the fill inside
    // that boundary does not read as a hole in the screen.
    private static readonly Color ProtectFace = TeamPalette.HillUnclaimed;
    private static readonly Color ProtectRim = TeamPalette.AbilityBright;
    private static readonly Color DefendFace = new(0.361f, 0.416f, 0.455f);
    private static readonly Color DefendRim = new(0.678f, 0.780f, 0.820f);
    private static readonly Color CoinEdge = new(0.545f, 0.529f, 0.490f);
    private static readonly Color CoinShadow = new(0.435f, 0.392f, 0.333f);

    // Ink for each face, pinned to the fill it is stamped on rather than taken from a token. The
    // HUD re-declares the palette dark inside .game-screen and both candidate tokens flip with it,
    // so a tokenised face ink put light grey on the bone plate at 1.4:1 and near-black on the
    // slate at 2.2:1. Pinned, they measure 14.3:1 and 4.7:1 and cannot drift from the disc again.
    private static readonly Color ProtectInk = new(0.110f, 0.071f, 0.008f);
    private static readonly Color DefendInk = new(0.910f, 0.925f, 0.933f);

    private static readonly string[] RoleClasses =
    {
        "escort-briefing__role--protect",
        "escort-briefing__role--defend",
        "escort-briefing__role--both",
    };

    private readonly VisualElement overlay;
    private readonly VisualElement curtain;
    private readonly VisualElement stage;
    private readonly VisualElement verdict;
    private readonly VisualElement rule;
    private readonly Label coinFace;
    private readonly Label legLabel;
    private readonly Label roleLabel;
    private readonly Label briefLabel;

    private EscortRole role;
    private float spin;
    private float faceTurn = 1f;
    private float coinLift;
    private float coinScaleX = 1f;
    private float coinScaleY = 1f;
    private float coinAlpha;
    private float trail;
    private float impact = -1f;

    public EscortRoleBriefing(VisualElement overlay)
    {
        this.overlay = overlay;
        curtain = overlay?.Q<VisualElement>("escort-briefing-curtain");
        stage = overlay?.Q<VisualElement>("escort-briefing-stage");
        verdict = overlay?.Q<VisualElement>("escort-briefing-verdict");
        rule = overlay?.Q<VisualElement>("escort-briefing-rule");
        coinFace = overlay?.Q<Label>("escort-briefing-coin-face");
        legLabel = overlay?.Q<Label>("escort-briefing-leg");
        roleLabel = overlay?.Q<Label>("escort-briefing-role");
        briefLabel = overlay?.Q<Label>("escort-briefing-brief");

        if (stage != null)
            stage.generateVisualContent += Paint;
    }

    public bool IsUsable =>
        overlay != null
        && curtain != null
        && stage != null
        && verdict != null
        && rule != null
        && coinFace != null
        && legLabel != null
        && roleLabel != null
        && briefLabel != null;

    public string RoleText => roleLabel?.text ?? string.Empty;

    /// <summary>
    /// Clocked off <see cref="Time.realtimeSinceStartup"/> rather than a summed per-frame delta.
    /// Both readings are meant to be real seconds, but summing Time.unscaledDeltaTime under a dev
    /// fast-forward overshoots real time by half again — measured at 2.98s of deltas across 2.00s
    /// of wall clock at timeScale 6 — and the server opens planning on a real-seconds wait, so a
    /// briefing on the drifting clock ends before the match is ready for it.
    /// </summary>
    public IEnumerator Play(
        EscortRole briefedRole,
        int legNumber,
        bool withCoin,
        float seconds,
        float coverSeconds
    )
    {
        if (!IsUsable)
            yield break;

        role = briefedRole;
        float verdictAt = withCoin ? CoinVerdictAt : CardVerdictAt;
        seconds = Mathf.Max(verdictAt + VerdictSeconds + ExitSeconds, seconds);
        float fadeOutAt = seconds - ExitSeconds;

        legLabel.text = EscortSeries.DescribeLeg(legNumber);
        roleLabel.text = EscortSeries.RoleHeadline(role);
        briefLabel.text = EscortSeries.RoleBrief(role);
        for (int index = 0; index < RoleClasses.Length; index++)
            roleLabel.EnableInClassList(RoleClasses[index], index == (int)role);

        ResetCoin();
        stage.EnableInClassList("hidden", !withCoin);
        overlay.RemoveFromClassList("hidden");
        overlay.BringToFront();

        float startedAt = Time.realtimeSinceStartup;
        for (float elapsed = 0f; elapsed < seconds; elapsed = Time.realtimeSinceStartup - startedAt)
        {
            overlay.style.opacity =
                elapsed < EntrySeconds ? EaseOutCubic(elapsed / EntrySeconds)
                : elapsed >= fadeOutAt ? 1f - EaseOutCubic((elapsed - fadeOutAt) / ExitSeconds)
                : 1f;

            if (withCoin)
            {
                StepCoin(elapsed);
                ApplyCoinFace();
                stage.MarkDirtyRepaint();
            }
            StepVerdict(elapsed, verdictAt);
            StepCurtain(elapsed, coverSeconds);
            yield return null;
        }

        Hide();
    }

    public void Hide()
    {
        if (overlay == null)
            return;

        overlay.style.opacity = 0f;
        if (curtain != null)
            curtain.style.opacity = 0f;
        overlay.AddToClassList("hidden");
    }

    /// <summary>
    /// The curtain fades up to opaque on the screen's entry clock, holds there while the board is
    /// torn down and rebuilt behind it, then eases away to leave the page translucent over a board
    /// that has finished arriving.
    /// </summary>
    private void StepCurtain(float t, float coverSeconds)
    {
        float lift = Mathf.Max(EntrySeconds, coverSeconds);
        curtain.style.opacity =
            t < EntrySeconds ? EaseOutCubic(t / EntrySeconds)
            : t < lift ? 1f
            : 1f - EaseOutCubic((t - lift) / CurtainLiftSeconds);
    }

    /// <summary>
    /// The HUD is re-bound every time its document is re-enabled, and the element tree it binds to
    /// outlives that. Without this the paint callback is registered a second time and the coin is
    /// drawn twice over itself.
    /// </summary>
    public void Dispose()
    {
        if (stage != null)
            stage.generateVisualContent -= Paint;
    }

    private void ResetCoin()
    {
        spin = 0f;
        faceTurn = 1f;
        coinLift = 0f;
        coinScaleX = 1f;
        coinScaleY = 1f;
        coinAlpha = 0f;
        trail = 0f;
        impact = -1f;
    }

    /// <summary>
    /// The toss, as four beats: the coin arrives, dips onto the thumb, flies, and lands. Nothing
    /// here decides anything — the spin is arranged to finish where it began.
    /// </summary>
    private void StepCoin(float t)
    {
        coinAlpha = 1f;
        coinLift = 0f;
        coinScaleX = 1f;
        coinScaleY = 1f;
        trail = 0f;
        impact = t >= CoinLandAt ? (t - CoinLandAt) / ImpactSeconds : -1f;
        if (impact > 1f)
            impact = -1f;

        if (t < CoinRiseSeconds)
        {
            float u = t / CoinRiseSeconds;
            float rise = EaseOutBack(u);
            coinAlpha = Mathf.Clamp01(u * 2f);
            coinScaleX = Mathf.Lerp(0.52f, 1f, rise);
            coinScaleY = coinScaleX;
            spin = 0f;
        }
        else if (t < CoinTossAt)
        {
            // Anticipation. The coin drops onto the thumb and flattens a touch, which is what
            // makes the launch on the next frame read as a launch rather than a teleport.
            float u = (t - CoinRiseSeconds) / CoinWindUpSeconds;
            float dip = Mathf.Sin(u * Mathf.PI);
            coinLift = -0.07f * dip;
            coinScaleY = 1f - 0.1f * dip;
            coinScaleX = 1f + 0.06f * dip;
            spin = 0f;
        }
        else if (t < CoinLandAt)
        {
            float u = (t - CoinTossAt) / CoinTossSeconds;
            // Part constant rate, part eased: the coin leaves the thumb already turning and is
            // still slowing when it lands, and the blend ends at exactly 1 either way, which is
            // what keeps the landing face honest.
            spin = HalfTurns * Mathf.PI * (0.3f * u + 0.7f * Mathf.SmoothStep(0f, 1f, u));
            // Flatter at the apex than a parabola, so the coin hangs where it is read.
            coinLift = Mathf.Sin(u * Mathf.PI);
            trail = Mathf.Clamp01((0.3f + 4.2f * u * (1f - u)) / 1.4f);
        }
        else if (t < CoinLandAt + CoinLandSeconds)
        {
            float punch = Mathf.Sin((t - CoinLandAt) / CoinLandSeconds * Mathf.PI);
            spin = HalfTurns * Mathf.PI;
            coinScaleY = 1f - 0.2f * punch;
            coinScaleX = 1f + 0.13f * punch;
        }
        else
        {
            float u = Mathf.Clamp01((t - CoinLandAt - CoinLandSeconds) / CoinSettleSeconds);
            float ring = Mathf.Exp(-7f * u) * Mathf.Cos(17f * u);
            spin = HalfTurns * Mathf.PI;
            coinScaleY = 1f + 0.05f * ring;
            coinScaleX = 1f - 0.035f * ring;
        }

        faceTurn = Mathf.Cos(spin);
    }

    private void ApplyCoinFace()
    {
        EscortRole shown = faceTurn >= 0f ? role : EscortSeries.OpposingRole(role);
        string word = EscortSeries.RoleHeadline(shown);
        if (coinFace.text != word)
            coinFace.text = word;
        coinFace.style.color = IsProtectFace(shown) ? ProtectInk : DefendInk;

        if (!TryGetCoinMetrics(out float radius, out Vector2 rest, out float maxLift))
            return;

        float absTurn = Mathf.Abs(faceTurn);
        coinFace.style.fontSize = radius * 0.34f;
        coinFace.style.opacity =
            coinAlpha * Mathf.Clamp01((absTurn - 0.2f) / 0.28f);
        coinFace.style.scale = new Scale(
            new Vector2(coinScaleX, Mathf.Max(0.001f, absTurn * coinScaleY))
        );
        // Layout centres the label on the stage, so what it owes is the gap between the stage's
        // centre and the coin's rest, plus however far the toss has carried the coin.
        coinFace.style.translate = new Translate(
            0f,
            -radius * CoinRestOffsetFraction - coinLift * maxLift
        );
    }

    private void StepVerdict(float t, float verdictAt)
    {
        float u = Mathf.Clamp01((t - verdictAt) / VerdictSeconds);
        verdict.style.opacity = EaseOutCubic(u);

        float slam = EaseOutQuint(u);
        roleLabel.style.scale = new Scale(Vector2.one * Mathf.Lerp(1.3f, 1f, slam));
        // The word arrives wide and closes onto its tracking, which is the same gesture the
        // display type makes everywhere else, run in time.
        roleLabel.style.letterSpacing = new StyleLength(Mathf.Lerp(24f, 2f, slam));
        rule.style.scale = new Scale(
            new Vector2(EaseOutCubic(Mathf.Clamp01(u * 1.7f)), 1f)
        );

        float briefU = Mathf.Clamp01((t - verdictAt - BriefDelaySeconds) / BriefSeconds);
        briefLabel.style.opacity = briefU;
        briefLabel.style.translate = new Translate(
            0f,
            Mathf.Lerp(16f, 0f, EaseOutCubic(briefU))
        );
    }

    /// <summary>The decider's card carries no coin, so its face takes the warm side's dress.</summary>
    private static bool IsProtectFace(EscortRole shown) => shown != EscortRole.Defend;

    private bool TryGetCoinMetrics(out float radius, out Vector2 rest, out float maxLift)
    {
        Rect box = stage.contentRect;
        radius = 0f;
        rest = Vector2.zero;
        maxLift = 0f;
        if (box.width <= 2f || box.height <= 2f)
            return false;

        radius = Mathf.Min(box.width, box.height) * 0.5f * CoinRadiusFraction;
        rest = new Vector2(
            box.width * 0.5f,
            box.height * 0.5f - radius * CoinRestOffsetFraction
        );
        maxLift = Mathf.Max(0f, rest.y - radius * 0.72f);
        return true;
    }

    private void Paint(MeshGenerationContext context)
    {
        if (coinAlpha <= 0.001f)
            return;
        if (!TryGetCoinMetrics(out float radius, out Vector2 rest, out float maxLift))
            return;

        Painter2D painter = context.painter2D;
        painter.dashPattern = System.ReadOnlySpan<float>.Empty;
        painter.lineCap = LineCap.Butt;

        Vector2 centre = new(rest.x, rest.y - coinLift * maxLift);
        float groundY = rest.y + radius * 1.18f;
        float rx = radius * coinScaleX;
        float ry = radius * Mathf.Abs(faceTurn) * coinScaleY;
        float thickness = radius * CoinThicknessFraction;

        bool protectFace = IsProtectFace(faceTurn >= 0f ? role : EscortSeries.OpposingRole(role));
        Color face = protectFace ? ProtectFace : DefendFace;
        Color rim = protectFace ? ProtectRim : DefendRim;

        PaintContactShadow(painter, rest.x, groundY, radius);
        PaintTrail(painter, centre, rx, radius, face);

        // The side wall, sat half a thickness low so the coin reads as a solid disc seen slightly
        // from above rather than as a cut-out. Edge-on it is the whole coin.
        painter.fillColor = CoinEdge.WithAlpha(coinAlpha);
        painter.BeginPath();
        TraceEllipse(
            painter,
            new Vector2(centre.x, centre.y + thickness * 0.5f),
            rx,
            ry + thickness * 0.5f,
            EllipseSegments
        );
        painter.Fill();

        painter.fillColor = face.WithAlpha(coinAlpha);
        painter.BeginPath();
        TraceEllipse(painter, centre, rx, Mathf.Max(0.5f, ry), EllipseSegments);
        painter.Fill();

        if (ry < radius * 0.06f)
            return;

        PaintMilledRim(painter, centre, rx, ry, radius, rim);

        painter.strokeColor = rim.WithAlpha(coinAlpha);
        painter.lineWidth = Mathf.Max(1.5f, radius * 0.05f);
        painter.BeginPath();
        TraceEllipse(painter, centre, rx * 0.9f, ry * 0.9f, EllipseSegments);
        painter.Stroke();

        // Highlight on the upper left, which is where the stage's own key light is.
        painter.strokeColor = new Color(1f, 1f, 1f, 0.34f * coinAlpha * Mathf.Abs(faceTurn));
        painter.lineWidth = Mathf.Max(1.5f, radius * 0.045f);
        painter.BeginPath();
        TraceEllipseArc(painter, centre, rx * 0.9f, ry * 0.9f, 196f, 306f);
        painter.Stroke();

        painter.strokeColor = rim.WithAlpha(0.34f * coinAlpha);
        painter.lineWidth = Mathf.Max(1f, radius * 0.014f);
        painter.BeginPath();
        TraceEllipse(painter, centre, rx * 0.72f, ry * 0.72f, EllipseSegments);
        painter.Stroke();

        // The mark the deployment card and the debrief both wear above their title, stamped into
        // the face above the word.
        float markHeight = Mathf.Max(1f, radius * 0.05f * Mathf.Abs(faceTurn) * coinScaleY);
        float markHalfWidth = rx * 0.26f;
        float markY = centre.y - ry * 0.46f;
        painter.fillColor = rim.WithAlpha(coinAlpha);
        painter.BeginPath();
        painter.MoveTo(new Vector2(centre.x - markHalfWidth, markY - markHeight * 0.5f));
        painter.LineTo(new Vector2(centre.x + markHalfWidth, markY - markHeight * 0.5f));
        painter.LineTo(new Vector2(centre.x + markHalfWidth, markY + markHeight * 0.5f));
        painter.LineTo(new Vector2(centre.x - markHalfWidth, markY + markHeight * 0.5f));
        painter.ClosePath();
        painter.Fill();

        PaintImpactRing(painter, rest.x, groundY, radius);
    }

    /// <summary>
    /// Four stacked ellipses instead of a blur, which Painter2D has none of. Tight and dark under
    /// a coin on the table, wide and faint under one at the top of its arc — the only cue that
    /// says the coin left the surface rather than just got bigger.
    /// </summary>
    private void PaintContactShadow(Painter2D painter, float x, float y, float radius)
    {
        float grounded = 1f - Mathf.Clamp01(coinLift);
        float halfWidth = radius * Mathf.Lerp(0.58f, 0.94f, grounded) * coinScaleX;
        float halfHeight = halfWidth * 0.17f;

        for (int layer = 4; layer >= 1; layer--)
        {
            float spread = 1f + layer * 0.3f;
            painter.fillColor = CoinShadow.WithAlpha(
                Mathf.Lerp(0.04f, 0.15f, grounded) * coinAlpha / layer
            );
            painter.BeginPath();
            TraceEllipse(painter, new Vector2(x, y), halfWidth * spread, halfHeight * spread, 40);
            painter.Fill();
        }
    }

    /// <summary>
    /// Two outlines a fraction of a turn behind the coin. At four turns a second the face itself
    /// is a strobe, and these are what make it read as one object moving instead of two.
    /// </summary>
    private void PaintTrail(Painter2D painter, Vector2 centre, float rx, float radius, Color face)
    {
        if (trail <= 0.02f)
            return;

        painter.strokeColor = face.WithAlpha(0.2f * trail * coinAlpha);
        painter.lineWidth = Mathf.Max(1f, radius * 0.028f);
        for (int ghost = 1; ghost <= 2; ghost++)
        {
            float ghostRy = radius * Mathf.Abs(Mathf.Cos(spin - ghost * 0.22f)) * coinScaleY;
            painter.BeginPath();
            TraceEllipse(painter, centre, rx, Mathf.Max(0.5f, ghostRy), 40);
            painter.Stroke();
        }
    }

    private void PaintMilledRim(
        Painter2D painter,
        Vector2 centre,
        float rx,
        float ry,
        float radius,
        Color rim
    )
    {
        painter.strokeColor = rim.WithAlpha(0.5f * coinAlpha);
        painter.lineWidth = Mathf.Max(1f, radius * 0.022f);
        painter.BeginPath();
        for (int tick = 0; tick < KnurlTicks; tick++)
        {
            float angle = tick / (float)KnurlTicks * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            painter.MoveTo(new Vector2(centre.x + cos * rx * 0.985f, centre.y + sin * ry * 0.985f));
            painter.LineTo(new Vector2(centre.x + cos * rx * 0.87f, centre.y + sin * ry * 0.87f));
        }
        painter.Stroke();
    }

    private void PaintImpactRing(Painter2D painter, float x, float y, float radius)
    {
        if (impact < 0f)
            return;

        float spread = EaseOutCubic(impact);
        float halfWidth = radius * (0.6f + 1.9f * spread);
        painter.strokeColor = ProtectRim.WithAlpha(0.5f * (1f - impact) * coinAlpha);
        painter.lineWidth = Mathf.Max(1f, radius * 0.055f * (1f - impact));
        painter.BeginPath();
        TraceEllipse(painter, new Vector2(x, y), halfWidth, halfWidth * 0.17f, 48);
        painter.Stroke();
    }

    /// <summary>
    /// Painter2D draws circles and nothing else round, and every shape the coin is made of is an
    /// ellipse whose height is the cosine of the spin. Sampled rather than fitted with curves:
    /// the whole point is that the height changes every frame, so there is nothing to cache.
    /// </summary>
    private static void TraceEllipse(
        Painter2D painter,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        int segments
    )
    {
        painter.MoveTo(new Vector2(centre.x + halfWidth, centre.y));
        for (int step = 1; step <= segments; step++)
        {
            float angle = step / (float)segments * Mathf.PI * 2f;
            painter.LineTo(
                new Vector2(
                    centre.x + Mathf.Cos(angle) * halfWidth,
                    centre.y + Mathf.Sin(angle) * halfHeight
                )
            );
        }
        painter.ClosePath();
    }

    private static void TraceEllipseArc(
        Painter2D painter,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        float fromDegrees,
        float toDegrees
    )
    {
        const int segments = 20;
        for (int step = 0; step <= segments; step++)
        {
            float angle =
                Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, step / (float)segments);
            Vector2 point = new(
                centre.x + Mathf.Cos(angle) * halfWidth,
                centre.y + Mathf.Sin(angle) * halfHeight
            );
            if (step == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }
    }

    private static float EaseOutCubic(float u)
    {
        u = Mathf.Clamp01(u);
        float inverse = 1f - u;
        return 1f - inverse * inverse * inverse;
    }

    private static float EaseOutQuint(float u)
    {
        u = Mathf.Clamp01(u);
        float inverse = 1f - u;
        return 1f - inverse * inverse * inverse * inverse * inverse;
    }

    private static float EaseOutBack(float u)
    {
        u = Mathf.Clamp01(u);
        float inverse = u - 1f;
        return 1f + 2.2f * inverse * inverse * inverse + 1.2f * inverse * inverse;
    }
}
