using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public enum MatchVerdictTone : byte
{
    Neutral,
    Victory,
    Defeat,
}

/// <summary>
/// Plays the screen that ends a match: the rail is struck, the verdict lands on it, and the reason
/// and the two buttons arrive behind it.
///
/// The strike itself is drawn with <see cref="Painter2D"/> rather than assembled from elements, for
/// the same reason <see cref="EscortRoleBriefing"/>'s coin is: rings, radiating spokes, a band of
/// light and a drift of motes are a hundred shapes whose geometry all derives from one strike point
/// and one clock, and rebuilding that as an element tree every frame churns layout for a picture
/// that never accepts input.
///
/// The tone is the whole difference between the two endings. A win expands, overshoots and rises; a
/// loss contracts, settles and falls. Neither is a hue — see --toy-defeat.
/// </summary>
public sealed class MatchVerdictScreen
{
    // Timeline, in real seconds from the first frame, before the tone's pace is applied.
    public const float EntrySeconds = 0.14f;

    private const float DeciderAt = 0.06f;
    private const float DeciderSeconds = 0.2f;
    private const float MarkAt = 0.16f;
    private const float MarkSeconds = 0.26f;
    private const float HeadlineAt = 0.38f;
    private const float HeadlineSeconds = 0.34f;

    /// <summary>
    /// The tracking runs long past the word being readable, which is the whole point of it: a
    /// settle the eye can follow is what makes the word feel thrown rather than switched on.
    /// </summary>
    private const float HeadlineTrackSeconds = 0.62f;
    private const float StatusAt = 0.62f;
    private const float StatusSeconds = 0.28f;
    private const float ActionsAt = 0.8f;
    private const float ActionsSeconds = 0.26f;

    private const float WashSeconds = 0.2f;
    private const float SweepSeconds = 0.42f;
    private const float RingStagger = 0.075f;
    private const float RingSeconds = 0.62f;
    private const float MoteFadeSeconds = 0.7f;

    private const int CircleSegments = 56;
    private const int SweepSteps = 9;
    private const int SpokeCount = 22;
    private const int MoteCount = 30;

    /// <summary>
    /// How far the burst reaches, in multiples of the word's own height. Measured against the word
    /// rather than the screen: a ring scaled to the frame is an arc sweeping across the board, and
    /// at that size it reads as something gone wrong rather than as something landing.
    /// </summary>
    private const float ReachFactor = 1.9f;

    // Pinned rather than tokenised, for the reason written over EscortRoleBriefing's face inks:
    // Painter2D cannot resolve a custom property, and the HUD re-declares the whole palette dark
    // inside .game-screen, so a colour read from the front end's tokens would invert here.
    //
    // The light is the white the verdict itself goes to; the warm is --toy-primary, which already
    // owns the rule above the word and the button under it. It is texture on a few motes and
    // spokes, never the verdict's own colour.
    private static readonly Color BurstLight = new(0.969f, 0.984f, 0.992f);
    private static readonly Color BurstWarm = new(0.945f, 0.561f, 0.004f);
    private static readonly Color BurstCool = new(0.588f, 0.659f, 0.69f);

    /// <summary>Both the rule and the word grow away from the rail rather than about their middle.</summary>
    private static readonly TransformOrigin LeftCentre =
        new(Length.Percent(0f), Length.Percent(50f), 0f);

    private readonly VisualElement overlay;
    private readonly VisualElement stage;
    private readonly VisualElement mark;
    private readonly VisualElement actions;
    private readonly Label decider;
    private readonly Label headline;
    private readonly Label status;

    private MatchVerdictTone tone;
    private float pace = 1f;
    private float start;
    private float clock;
    private float baseSpacing = float.NaN;

    public MatchVerdictScreen(VisualElement overlay)
    {
        this.overlay = overlay;
        stage = overlay?.Q<VisualElement>("results-stage");
        mark = overlay?.Q<VisualElement>("results-mark");
        actions = overlay?.Q<VisualElement>(className: "results-actions");
        decider = overlay?.Q<Label>("results-decider");
        headline = overlay?.Q<Label>("results-headline");
        status = overlay?.Q<Label>("results-status");

        if (stage != null)
            stage.generateVisualContent += Paint;
    }

    public bool IsUsable =>
        overlay != null
        && stage != null
        && mark != null
        && actions != null
        && decider != null
        && headline != null
        && status != null;

    /// <summary>
    /// Clocked off <see cref="Time.realtimeSinceStartup"/> rather than a summed per-frame delta,
    /// for the reason written over <see cref="EscortRoleBriefing.Play"/>: under a dev fast-forward
    /// the summed unscaled deltas overshoot real time by half again.
    ///
    /// Carries no copy. The HUD has already written the rail by the time this starts, so a match
    /// that ends with the screen unusable still reads correctly; only the motion is lost.
    ///
    /// Runs on past the reveal so the motes keep drifting: a verdict the player sits on for a
    /// minute deciding whether to play again should not be a frozen frame.
    /// </summary>
    public IEnumerator Play(MatchVerdictTone verdictTone)
    {
        if (!IsUsable)
            yield break;

        tone = verdictTone;
        pace = verdictTone switch
        {
            MatchVerdictTone.Victory => 1f,
            MatchVerdictTone.Defeat => 1.35f,
            _ => 1.15f,
        };

        Reset();

        while (!overlay.ClassListContains("hidden"))
        {
            clock = Time.realtimeSinceStartup - start;
            StepEntry();
            StepDecider();
            StepMark();
            StepHeadline();
            StepStatus();
            StepActions();
            stage.MarkDirtyRepaint();
            yield return null;
        }
    }

    public void Hide()
    {
        if (overlay == null)
            return;

        overlay.style.opacity = new StyleFloat(StyleKeyword.Null);
        ClearInline(decider);
        ClearInline(status);
        ClearInline(actions);
        if (mark != null)
        {
            mark.style.scale = new StyleScale(StyleKeyword.Null);
            mark.style.backgroundColor = new StyleColor(StyleKeyword.Null);
        }
        if (headline != null)
        {
            headline.style.opacity = new StyleFloat(StyleKeyword.Null);
            headline.style.scale = new StyleScale(StyleKeyword.Null);
            headline.style.letterSpacing = new StyleLength(StyleKeyword.Null);
        }
        baseSpacing = float.NaN;
    }

    /// <summary>
    /// The HUD is re-bound every time its document is re-enabled and the element tree outlives
    /// that, so without this the paint callback is registered twice and the burst is drawn over
    /// itself at double strength.
    /// </summary>
    public void Dispose()
    {
        if (stage != null)
            stage.generateVisualContent -= Paint;
    }

    private static void ClearInline(VisualElement element)
    {
        if (element == null)
            return;

        element.style.opacity = new StyleFloat(StyleKeyword.Null);
        element.style.translate = new StyleTranslate(StyleKeyword.Null);
    }

    private void Reset()
    {
        start = Time.realtimeSinceStartup;
        clock = 0f;
        baseSpacing = float.NaN;

        overlay.style.opacity = 0f;
        decider.style.opacity = 0f;
        status.style.opacity = 0f;
        actions.style.opacity = 0f;
        headline.style.opacity = 0f;
        headline.style.letterSpacing = new StyleLength(StyleKeyword.Null);
        headline.style.transformOrigin = LeftCentre;
        headline.style.scale = new Scale(new Vector2(1f, HeadlineEntryScale));
        mark.style.transformOrigin = LeftCentre;
        mark.style.scale = new Scale(new Vector2(0f, 1f));
    }

    /// <summary>A win is thrown down onto the rail from above its line; a loss sinks onto it.</summary>
    private float HeadlineEntryScale => tone == MatchVerdictTone.Victory ? 1.16f : 0.92f;

    private void StepEntry()
    {
        overlay.style.opacity = Mathf.Clamp01(EaseOutCubic(clock / (EntrySeconds * pace)));
    }

    private void StepDecider()
    {
        float u = Progress(DeciderAt, DeciderSeconds);
        float eased = EaseOutCubic(u);
        decider.style.opacity = eased;
        decider.style.translate = new Translate((1f - eased) * 14f, 0f, 0f);
    }

    /// <summary>
    /// The rule fires out ahead of the word, overshooting on a win so the rail reads as struck
    /// rather than drawn, and flashing white at the moment of contact before settling to the
    /// orange it wears for the rest of the screen.
    /// </summary>
    private void StepMark()
    {
        float u = Progress(MarkAt, MarkSeconds);
        float extent =
            tone == MatchVerdictTone.Victory ? EaseOutBack(u, 2.4f) : EaseOutCubic(u);
        mark.style.scale = new Scale(new Vector2(Mathf.Max(0f, extent), 1f));

        if (tone != MatchVerdictTone.Victory)
        {
            mark.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            return;
        }

        float cool = Mathf.Clamp01((u - 0.45f) / 0.55f);
        mark.style.backgroundColor = Color.Lerp(BurstLight, BurstWarm, EaseOutCubic(cool));
    }

    /// <summary>
    /// The impact is carried by tracking, not by scale: display caps closing from wide to tight is
    /// the move that reads as a word landing, and unlike a scale it cannot be clipped by the scroll
    /// view the rail sits in. The spread is taken from the resolved size so every breakpoint gets
    /// the same gesture without restating it.
    /// </summary>
    private void StepHeadline()
    {
        float u = Progress(HeadlineAt, HeadlineSeconds);
        headline.style.opacity = Mathf.Clamp01(u / 0.28f);
        if (u <= 0f)
            return;

        // Read once the word is real, and never before: Reset clears the inline tracking a previous
        // match left behind, and resolvedStyle does not carry that clear until the next style pass.
        if (float.IsNaN(baseSpacing))
        {
            if (headline.resolvedStyle.fontSize <= 1f)
                return;
            baseSpacing = headline.resolvedStyle.letterSpacing;
        }

        float spread = headline.resolvedStyle.fontSize * 0.2f;
        bool won = tone == MatchVerdictTone.Victory;
        float settle = EaseOutCubic(Progress(HeadlineAt, HeadlineTrackSeconds));
        headline.style.letterSpacing = Mathf.Lerp(baseSpacing + spread, baseSpacing, settle);

        float squash = won ? EaseOutBack(u, 1.7f) : EaseOutCubic(u);
        headline.style.scale = new Scale(
            new Vector2(1f, Mathf.Lerp(HeadlineEntryScale, 1f, squash))
        );
    }

    private void StepStatus()
    {
        float eased = EaseOutCubic(Progress(StatusAt, StatusSeconds));
        status.style.opacity = eased;
        status.style.translate = new Translate(0f, (1f - eased) * 10f, 0f);
    }

    private void StepActions()
    {
        float eased = EaseOutCubic(Progress(ActionsAt, ActionsSeconds));
        actions.style.opacity = eased;
        actions.style.translate = new Translate(0f, (1f - eased) * 14f, 0f);
    }

    private float Progress(float at, float seconds)
    {
        return Mathf.Clamp01((clock - at * pace) / (seconds * pace));
    }

    // === THE STRIKE ===

    private void Paint(MeshGenerationContext context)
    {
        Rect rect = stage.contentRect;
        if (rect.width < 4f || rect.height < 4f)
            return;

        Painter2D painter = context.painter2D;
        painter.lineCap = LineCap.Butt;
        painter.lineJoin = LineJoin.Miter;

        Vector2 strike = StrikePoint(rect);
        float reach = HeadlineHeight() * ReachFactor;
        float band = Mathf.Clamp(HeadlineHeight() * 1.05f, 44f, 190f);

        PaintWash(painter, rect);
        PaintSweep(painter, rect, strike, band);
        PaintRings(painter, strike, reach);
        if (tone != MatchVerdictTone.Defeat)
            PaintSpokes(painter, strike, reach);
        PaintMotes(painter, rect);
    }

    /// <summary>
    /// The word's own left edge, read from layout rather than from the transform, so the tracking
    /// animation above cannot drag the strike point across the screen with it.
    /// </summary>
    private Vector2 StrikePoint(Rect rect)
    {
        Rect layout = headline.layout;
        if (layout.height < 4f || float.IsNaN(layout.x) || headline.parent == null)
            return new Vector2(rect.width * 0.26f, rect.height * 0.5f);

        Vector2 origin = stage.WorldToLocal(headline.parent.LocalToWorld(layout.position));
        return origin + new Vector2(layout.height * 0.3f, layout.height * 0.52f);
    }

    private float HeadlineHeight()
    {
        float height = headline.layout.height;
        return float.IsNaN(height) || height < 4f ? 108f : height;
    }

    /// <summary>
    /// The hit. One frame's worth of light over the whole frame, painted here rather than on the
    /// shared .hud-flash element because that element sits under the scrim, where four fifths of
    /// it is lost. Short and weak enough to read as a punch rather than as a white screen.
    /// </summary>
    private void PaintWash(Painter2D painter, Rect rect)
    {
        float u = Progress(HeadlineAt, WashSeconds);
        if (u <= 0f || u >= 1f)
            return;

        Color light = tone == MatchVerdictTone.Defeat ? BurstCool : BurstLight;
        light.a =
            Mathf.Pow(1f - u, 2.2f)
            * tone switch
            {
                MatchVerdictTone.Victory => 0.13f,
                MatchVerdictTone.Defeat => 0.05f,
                _ => 0.08f,
            };

        painter.fillColor = light;
        painter.BeginPath();
        painter.MoveTo(new Vector2(0f, 0f));
        painter.LineTo(new Vector2(rect.width, 0f));
        painter.LineTo(new Vector2(rect.width, rect.height));
        painter.LineTo(new Vector2(0f, rect.height));
        painter.ClosePath();
        painter.Fill();
    }

    /// <summary>
    /// A band of light thrown out along the line the word sits on. Nested rects rather than one,
    /// because Painter2D has no gradient and a single flat rect at a readable strength is a grey
    /// bar. Enough of them that the falloff stops reading as edges: at three steps the seams
    /// between them were visible as hard lines across the whole frame.
    /// </summary>
    private void PaintSweep(Painter2D painter, Rect rect, Vector2 strike, float band)
    {
        float u = Progress(HeadlineAt, SweepSeconds);
        if (u <= 0f || u >= 1f)
            return;

        float half = EaseOutCubic(u) * rect.width;
        float fade = (1f - u) * (1f - u);
        float peak = tone switch
        {
            MatchVerdictTone.Victory => 0.2f,
            MatchVerdictTone.Defeat => 0.08f,
            _ => 0.12f,
        };
        Color light = tone == MatchVerdictTone.Defeat ? BurstCool : BurstLight;

        for (int i = 0; i < SweepSteps; i++)
        {
            float step = (i + 1f) / SweepSteps;
            PaintBand(
                painter,
                rect,
                strike,
                half,
                band * step,
                light,
                peak * fade / SweepSteps
            );
        }
    }

    private static void PaintBand(
        Painter2D painter,
        Rect rect,
        Vector2 strike,
        float half,
        float height,
        Color light,
        float alpha
    )
    {
        if (alpha <= 0.002f)
            return;

        float left = Mathf.Max(0f, strike.x - half);
        float right = Mathf.Min(rect.width, strike.x + half);
        if (right - left < 1f)
            return;

        light.a = alpha;
        painter.fillColor = light;
        painter.BeginPath();
        painter.MoveTo(new Vector2(left, strike.y - height * 0.5f));
        painter.LineTo(new Vector2(right, strike.y - height * 0.5f));
        painter.LineTo(new Vector2(right, strike.y + height * 0.5f));
        painter.LineTo(new Vector2(left, strike.y + height * 0.5f));
        painter.ClosePath();
        painter.Fill();
    }

    /// <summary>
    /// A win throws rings out; a loss draws one back in. The whole tonal difference of the screen
    /// is in that direction, which is why the count and the colour barely change with it.
    /// </summary>
    private void PaintRings(Painter2D painter, Vector2 strike, float reach)
    {
        bool lost = tone == MatchVerdictTone.Defeat;
        int count = lost ? 2 : 3;
        Color light = lost ? BurstCool : BurstLight;

        for (int i = 0; i < count; i++)
        {
            float u = Progress(HeadlineAt + i * RingStagger, RingSeconds);
            if (u <= 0f || u >= 1f)
                continue;

            float radius = lost
                ? Mathf.Lerp(reach, reach * 0.06f, EaseInOutCubic(u))
                : Mathf.Lerp(reach * 0.08f, reach, EaseOutCubic(u));
            float alpha = lost
                ? Mathf.Sin(u * Mathf.PI) * 0.26f / (1f + i * 0.5f)
                : Mathf.Pow(1f - u, 1.7f) * 0.32f / (1f + i * 0.5f);

            light.a = alpha;
            painter.strokeColor = light;
            painter.lineWidth = Mathf.Lerp(2.6f, 0.6f, u);
            StrokeCircle(painter, strike, radius);
        }
    }

    /// <summary>
    /// Tracers. Each leaves the strike point on its own clock and thins as it goes, so the burst
    /// reads as debris off an impact rather than as a sunburst drawn in one piece.
    /// </summary>
    private void PaintSpokes(Painter2D painter, Vector2 strike, float reach)
    {
        float scale = tone == MatchVerdictTone.Victory ? 1f : 0.55f;
        int count = tone == MatchVerdictTone.Victory ? SpokeCount : SpokeCount / 2;

        for (int i = 0; i < count; i++)
        {
            float jitter = Hash(i * 3 + 1);
            float u = Progress(HeadlineAt + jitter * 0.06f, 0.42f + jitter * 0.22f);
            if (u <= 0f || u >= 1f)
                continue;

            float angle = (i + jitter * 0.4f) / count * Mathf.PI * 2f + 0.11f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            float inner = Mathf.Lerp(
                reach * 0.12f,
                reach * (0.75f + jitter * 0.65f),
                EaseOutQuint(u)
            );
            float length = reach * 0.24f * (1f - u);

            Color light = i % 4 == 0 ? BurstWarm : BurstLight;
            light.a = Mathf.Pow(1f - u, 1.6f) * 0.8f * scale;
            painter.strokeColor = light;
            painter.lineWidth = Mathf.Lerp(2.6f, 0.7f, u);
            painter.BeginPath();
            painter.MoveTo(strike + direction * inner);
            painter.LineTo(strike + direction * (inner + length));
            painter.Stroke();
        }
    }

    /// <summary>
    /// What keeps the screen alive after the strike has gone. A win sends them up and a loss lets
    /// them fall, which is the same tonal direction the rings run in. Squares, because every radius
    /// in this interface is zero.
    /// </summary>
    private void PaintMotes(Painter2D painter, Rect rect)
    {
        bool lost = tone == MatchVerdictTone.Defeat;
        int count = lost ? 16 : tone == MatchVerdictTone.Victory ? MoteCount : 14;
        float entry = Mathf.Clamp01(clock / MoteFadeSeconds);
        if (entry <= 0f)
            return;

        float peak = (lost ? 0.22f : 0.38f) * entry;

        for (int i = 0; i < count; i++)
        {
            float lane = Hash(i * 2 + 5);
            float seed = Hash(i * 5 + 11);
            float speed = (lost ? 0.028f : 0.04f) + seed * 0.045f;
            float trip = Mathf.Repeat(seed + clock * speed, 1f);
            float y = rect.height * (lost ? trip : 1f - trip);
            float x =
                rect.width * lane
                + Mathf.Sin(clock * (0.4f + seed * 0.7f) + i) * rect.width * 0.013f;
            float size = 2f + seed * 3.4f;

            Color light = lost ? BurstCool : i % 4 == 0 ? BurstWarm : BurstLight;
            light.a = Mathf.Sin(trip * Mathf.PI) * peak;
            if (light.a <= 0.004f)
                continue;

            painter.fillColor = light;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + size, y));
            painter.LineTo(new Vector2(x + size, y + size));
            painter.LineTo(new Vector2(x, y + size));
            painter.ClosePath();
            painter.Fill();
        }
    }

    private static void StrokeCircle(Painter2D painter, Vector2 centre, float radius)
    {
        if (radius < 1f)
            return;

        painter.BeginPath();
        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            Vector2 point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }
        painter.Stroke();
    }

    /// <summary>Deterministic, so a spoke or a mote keeps its lane across a domain reload.</summary>
    private static float Hash(int n)
    {
        n = (n << 13) ^ n;
        return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    private static float EaseOutQuint(float t)
    {
        t = Mathf.Clamp01(t);
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse * inverse * inverse;
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t);
        float inverse = t - 1f;
        return 1f + (overshoot + 1f) * inverse * inverse * inverse + overshoot * inverse * inverse;
    }
}
