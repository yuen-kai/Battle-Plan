using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// The number that tells a player how much the hit was worth.
/// <para>
/// The reference draws a hard line between state and event. A gold-keylined plate with content
/// inside it is state — a level, a rank, a nameplate — and a stranger reads it as a signpost
/// because that is what it is. A hit is an event, so it is a bare numeral with a hard black
/// stroke and an offset shadow, laid straight onto whatever it happened over, violent and gone.
/// Everything a plate used to do for the silhouette is the stroke's job here.
/// </para>
/// <para>Purely local and visual. Call on every peer when damage is applied.</para>
/// </summary>
public static class DamagePopup
{
    // Callers hand over whatever position they have: Health knows only the networked root, which
    // stands on the deck at y 0, and the film rig aims a couple of units over a head. Neither of
    // those is the body, and a hit marker belongs on the body the hit was taken from, so both
    // resolve to the same mass centre. Pinning it also pins how far the number sits from the
    // camera, which is the only thing that decides how large it reads.
    private const float BodyCentreHeight = 0.9f;

    /// <summary>
    /// Pops a damage number on <paramref name="worldPosition"/>'s victim.
    /// </summary>
    /// <param name="worldPosition">Anywhere on the victim; only its ground column is used.</param>
    /// <param name="amount">Damage dealt; rendered rounded.</param>
    /// <param name="tone">How loudly it should read.</param>
    public static void Spawn(Vector3 worldPosition, float amount, DamageTone tone = DamageTone.Normal)
    {
        if (amount <= 0f)
            return;

        Vector3 anchoredAt = new(worldPosition.x, BodyCentreHeight, worldPosition.z);

        GameObject root = new("DamageNumber");
        root.transform.position = anchoredAt;
        root.AddComponent<DamagePopupLabel>().Build(anchoredAt, amount, tone);
    }
}

/// <summary>
/// One damage number, from the frame it lands to the frame it clears.
/// <para>
/// It lives in world space rather than on a screen-space overlay: it belongs to the board, on the
/// unit it was taken from, and an overlay would neither sit in the diorama nor reach a camera that
/// renders into a texture. It is aligned to the camera plane every frame, because at the board's
/// 73-degree pitch anything lying flat on the ground is unreadable — and for the same reason it
/// climbs along the camera's own up axis, since a world-space lift of two units moves a number
/// less than a quarter of a cell up the screen.
/// </para>
/// </summary>
public sealed class DamagePopupLabel : MonoBehaviour
{
    // The face the digits are set in. This is the only thing that selects it: the cap height and
    // the horizontal squeeze are both measured off whichever TMP_FontAsset loads, so nothing else
    // moves when the face does.
    //
    // It is aimed at a face that does not exist yet, and deliberately. What the number wants is
    // Oswald Bold — a condensed grotesque whose numerals already stand tall and narrow, which is
    // the proportion the squeeze below is faking on a wide grotesque. OswaldCaps-Bold.ttf is in
    // the tree under Assets/Fonts/OswaldCaps/, but the TMP_FontAsset the Font Asset Creator builds
    // from it is not, and cannot be authored from code. Build it into a Resources folder at this
    // path and the number picks it up with no code change at all; until then the load misses once
    // and the fallback answers.
    private const string FontResourcePath = "Fonts/OswaldCaps-Bold SDF";

    // Liberation Sans lives inside TextMeshPro's own resources and is the one face guaranteed to
    // resolve in any project.
    private const string FallbackFontResourcePath = "Fonts & Materials/LiberationSans SDF";

    // TMP lays out a world-space glyph at 0.1 world units per point. The cap height per em is read
    // off the face that loaded; this is only the opening guess for a face that reports none, and
    // the size is corrected against the glyphs TMP actually produced either way.
    private const float WorldUnitsPerPoint = 0.1f;
    private const float CapHeightPerEm = 0.69f;

    // The reference numerals are narrow with thick stems and tight counters, and how much squeeze
    // that needs is a property of the face rather than of this number. Liberation Sans' digits
    // advance 48 against a 59 cap and want about a seventh taken out of them; an already-condensed
    // face wants none and must not be squeezed into a slab.
    private const float TargetDigitAdvancePerCap = 0.7f;
    private const float MinCondense = 0.8f;
    private const float DefaultCondense = 0.86f;

    // Tracking, carried in ems so it survives a face with a different design size.
    private const float TightenEm = -0.0407f;
    private const float DefaultTighten = -3.5f;

    // The entire silhouette, now that there is nothing behind the digits. A distance field can only
    // reach as far off a contour as it was cut with padding for — Liberation Sans is sampled at 86
    // points per em with nine texels of it, so 9/59 of the cap height is the hard ceiling on any
    // stroke — and TMP centres its outline on the contour, spending half the width inside the
    // glyph. 0.64 lays 2.88 texels of hard black outside every contour, 0.049 of the cap, which is
    // the proportion the reference's bare numerals carry, and leaves the stems 58 percent gold.
    //
    // Face dilate stays at zero. It is summed into the same divisor the outline is scaled by, so
    // buying face weight back with it is paid for out of the stroke that has to do the reading.
    private const float OutlineWidth = 0.64f;

    // The shadow is what makes the number sit above the board rather than in it, and it is the
    // second half of the value delta the plate used to supply. Dilated enough to clear the
    // outline's own outer edge before it is offset, so it reads as a shadow rather than as a
    // thicker stroke, and nearly opaque: at 0.85 the deck shows through it at luminance 87 and the
    // keyline it is meant to extend stops being one.
    private const float ShadowOffset = 0.22f;
    private const float ShadowDilate = 0.62f;
    private const float ShadowAlpha = 0.95f;

    // How far the stroke and its shadow reach past the glyph box, in cap heights. Only the frame
    // clamp consumes this; the effect itself is sized by the distance field.
    private const float StrokeReachPerCap = 0.08f;

    // The number is spawned on the victim's centre, which is inside the victim, and it is a plane
    // facing the camera. This lifts it along the view axis — not up the screen — until it clears a
    // unit's own bulk: a 1.8-tall body reaches 1.01 units toward a 73-degree camera from its
    // centre. Sliding along the ray the number is seen down leaves its screen position untouched
    // and lands it at the same distance the last three rounds were framed and measured at.
    private const float CameraLead = 1.25f;

    // Frame kept clear outside the number's own half-extent, as a fraction of the viewport.
    private const float ViewportMargin = 0.014f;

    // A hit marker, not a label. Full size before a 30 Hz capture can take a second frame, at rest
    // by the fourth, climbing for its whole life and gone inside four hundred milliseconds.
    //
    // There is no brightening laid over the arrival. The tonemapper's shoulder turns a fifth more
    // drive into four levels of output and spends bloom threshold to do it, so the overshoot is
    // the entire event: area, colour and luminance all peak on the frame the number is largest,
    // because that is the only frame on which any of them can.
    private const float Life = 0.4f;
    private const float GrowSeconds = 0.06f;
    private const float SettleSeconds = 0.06f;
    private const float OvershootScale = 1.3f;
    private const float RestScale = 0.94f;
    private const float FadeSeconds = 0.11f;

    // Front-loaded, but never parked: the number is still climbing a seventh of a cell through its
    // last three frames, which is what keeps the tail from reading as a decal on an opacity ramp.
    private const float RiseEase = 0.65f;

    private const float RollDegrees = 5f;
    private const float RollSeconds = 0.14f;

    private const float ShakeSeconds = 0.13f;
    private const float ShakeCyclesPerSecond = 18f;
    private const float ShakeOffset = 0.06f;

    private const float BoardPitchDegrees = 73f;

    // Past the ability's own transparents, so smoke and dust settling over the hit never swallow
    // the one element the player has to read exactly.
    private const int DigitQueue = 3404;

    // Near-black rather than black: a stroke with a trace of the fill's own hue in it belongs to
    // the number instead of to the interface. Both land under luminance 10 against the deck's 181.
    private static readonly Color StrokeInk = Rgb(7, 6, 4);
    private static readonly Color ShadowInk = Rgb(3, 2, 2);

    private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor");
    private static readonly int FaceDilateId = Shader.PropertyToID("_FaceDilate");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineSoftnessId = Shader.PropertyToID("_OutlineSoftness");
    private static readonly int UnderlayColorId = Shader.PropertyToID("_UnderlayColor");
    private static readonly int UnderlayOffsetXId = Shader.PropertyToID("_UnderlayOffsetX");
    private static readonly int UnderlayOffsetYId = Shader.PropertyToID("_UnderlayOffsetY");
    private static readonly int UnderlayDilateId = Shader.PropertyToID("_UnderlayDilate");
    private static readonly int UnderlaySoftnessId = Shader.PropertyToID("_UnderlaySoftness");

    /// <summary>How one tone of hit presents itself, before any of it is animated.</summary>
    private sealed class Tone
    {
        /// <summary>Digit cap height as a fraction of a board cell.</summary>
        public float CapCells;

        /// <summary>
        /// Travel along the camera's up axis over the whole life, in world cells. On screen it
        /// reads larger than that, because the number floats a metre and a half nearer the lens
        /// than the deck a cell is measured across: at the framing these are shot at, 0.64 to 0.76
        /// world cells arrive as 0.84 to 0.99 of a cell of the floor beneath, and they hold above
        /// three quarters of one out to the widest camera any ability is filmed on.
        /// </summary>
        public float RiseCells;

        public float ShakeDegrees;

        /// <summary>
        /// What the fill is driven to. TMP's face colour is not an HDR property, so the engine
        /// gamma-corrects it on the way in and a drive of d arrives as d raised to 2.2. These land
        /// the brightest channel between 2.1 and 2.7 in linear: inside the soft knee of a bloom
        /// threshold that resolves to 3.62, where under four percent of the fill's energy reaches
        /// the bloom pyramid and the stroke around it stays a hard edge instead of a halo.
        /// </summary>
        public float Drive;

        public Color FillTop;
        public Color FillBottom;
    }

    // Magnitude has to be learnable from three samples, and the tonemapper decides how much of it
    // can be carried in light: past about 1.3 in linear the shoulder crushes every drive into the
    // same 238-245 output, so luminance alone spans barely fifteen levels above the deck's 181.
    // The ramp is therefore carried on four quantities at once, each monotonic — cap height, hue
    // walking gold to pure yellow, saturation, and what luminance is left — and the cap height
    // does most of the work. Nothing leaves the one hue family: a lethal hit is the largest and
    // the yellowest, not a different colour, because three hits in three colours teach nothing.
    //
    // Every fill is above the deck's own luminance by design. A number that reads darker than the
    // board it is drawn on is a hole in the board.
    private static readonly Tone NormalLook = new()
    {
        CapCells = 0.24f,
        RiseCells = 0.64f,
        ShakeDegrees = 0f,
        Drive = 1.4f,
        FillTop = Rgb(255, 234, 58),
        FillBottom = Rgb(255, 212, 14),
    };

    private static readonly Tone HeavyLook = new()
    {
        CapCells = 0.28f,
        RiseCells = 0.7f,
        ShakeDegrees = 0f,
        Drive = 1.48f,
        FillTop = Rgb(255, 248, 36),
        FillBottom = Rgb(255, 230, 2),
    };

    private static readonly Tone CriticalLook = new()
    {
        CapCells = 0.32f,
        RiseCells = 0.76f,
        // The only tone that shakes. It is the last number a unit will ever show the player, and
        // the shake is what separates it from a large ordinary hit without spending hue on it.
        ShakeDegrees = 5.5f,
        Drive = 1.56f,
        FillTop = Rgb(255, 255, 16),
        FillBottom = Rgb(255, 245, 0),
    };

    private static TMP_FontAsset resolvedFont;
    private static Camera[] cameraScratch = new Camera[4];

    private TextMeshPro label;
    private Material faceMaterial;
    private Camera viewCamera;
    private Tone tone;
    private Vector3 origin;
    private Vector3 lead;
    private Vector3 driftUp;
    private Vector3 driftRight;
    private float rise;
    private float halfWidth;
    private float halfHeight;
    private float condense = DefaultCondense;
    private float rollSign;
    private float elapsed;
    private bool hasFace;
    private bool hasOutline;
    private bool hasShadow;

    public void Build(Vector3 spawnPosition, float amount, DamageTone requested)
    {
        tone = LookFor(requested);
        origin = spawnPosition;

        // Axes are taken once, from the camera as it stands at the hit. Recomputing them every
        // frame would make the number swim whenever the camera punches on impact.
        Quaternion view = ViewRotation();
        driftUp = view * Vector3.up;
        driftRight = view * Vector3.right;
        lead = view * Vector3.back * CameraLead;

        rollSign = RollSign(spawnPosition);
        rise = tone.RiseCells * GameLoop.cellSize;

        float cap = tone.CapCells * GameLoop.cellSize;
        BuildDigits(amount, cap);
        Rect ink = MeasureInk(cap);
        PlaceDigits(ink);

        float reach = StrokeReachPerCap * cap;
        halfWidth = ink.width * condense * 0.5f + reach;
        halfHeight = ink.height * 0.5f + reach;

        Advance(0f);
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= Life)
        {
            Destroy(gameObject);
            return;
        }

        Advance(elapsed);
    }

    private void OnDestroy()
    {
        if (faceMaterial != null)
            Destroy(faceMaterial);
        faceMaterial = null;
    }

    /// <summary>
    /// Typesets the digits on their own child, so the horizontal squeeze that condenses the face
    /// never reaches the transform the number is scaled and rolled on.
    /// </summary>
    private void BuildDigits(float amount, float cap)
    {
        GameObject digits = new("Digits", typeof(RectTransform));
        digits.transform.SetParent(transform, false);

        label = digits.AddComponent<TextMeshPro>();

        TMP_FontAsset font = ResolveFont();
        if (font != null)
            label.font = font;

        TMP_FontAsset inUse = label.font;
        condense = CondenseFor(inUse);

        label.text = Mathf.Max(1, Mathf.RoundToInt(amount)).ToString(CultureInfo.InvariantCulture);
        label.fontSize = cap / (WorldUnitsPerPoint * CapPerEm(inUse));
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.characterSpacing = TightenFor(inUse);
        label.rectTransform.sizeDelta = new Vector2(24f, 8f);
        label.rectTransform.localScale = new Vector3(condense, 1f, 1f);

        // Vertical ramp down the fill, the way a struck numeral is lit from above. The lighter end
        // is also the less saturated one, so the gradient reads as light rather than as two
        // colours. TMP converts its own vertex colours out of sRGB, so these stay authored.
        label.enableVertexGradient = true;
        label.colorGradient = new VertexGradient(
            tone.FillTop,
            tone.FillTop,
            tone.FillBottom,
            tone.FillBottom
        );

        BuildFace();

        label.ForceMeshUpdate();
        FitCapHeight(cap);
    }

    /// <summary>
    /// Instances the font material so the stroke, shadow and fade belong to this one number and
    /// never reach the shared TMP material every other label in the project draws with.
    /// </summary>
    private void BuildFace()
    {
        Material source = label.fontSharedMaterial;
        if (source == null)
            return;

        Material instance = new(source) { name = "DamageNumber (Instance)" };

        hasFace = instance.HasProperty(FaceColorId);
        hasOutline = instance.HasProperty(OutlineColorId);
        hasShadow = instance.HasProperty(UnderlayColorId);

        if (instance.HasProperty(FaceDilateId))
            instance.SetFloat(FaceDilateId, 0f);
        if (instance.HasProperty(OutlineWidthId))
            instance.SetFloat(OutlineWidthId, OutlineWidth);
        if (instance.HasProperty(OutlineSoftnessId))
            instance.SetFloat(OutlineSoftnessId, 0f);

        if (hasShadow)
        {
            // Unblurred. Softness is what turns a shadow into a smudge, and a smudge under a
            // numeral on a pale deck is worth nothing at all against the deck.
            instance.EnableKeyword("UNDERLAY_ON");
            instance.SetFloat(UnderlayOffsetXId, ShadowOffset);
            instance.SetFloat(UnderlayOffsetYId, -ShadowOffset);
            instance.SetFloat(UnderlayDilateId, ShadowDilate);
            instance.SetFloat(UnderlaySoftnessId, 0f);
        }

        instance.renderQueue = DigitQueue;

        // Handed over only once it is fully configured: TMP reserves room in the glyph quads for
        // the stroke and the shadow at the moment the material arrives, and either of them widened
        // afterwards would spread past a quad that was measured without it.
        faceMaterial = instance;
        label.fontSharedMaterial = instance;
    }

    /// <summary>
    /// Corrects the point size against the glyphs TMP produced, so the cap height is what was
    /// asked for regardless of the face's own metrics or the padding its material reserves.
    /// Everything downstream scales linearly with the point size, so one pass is exact.
    /// </summary>
    private void FitCapHeight(float cap)
    {
        Rect ink = MeasuredInk();
        if (ink.height <= 0.0001f)
            return;

        label.fontSize = Mathf.Clamp(label.fontSize * (cap / ink.height), 1f, 400f);
        label.ForceMeshUpdate();
    }

    private Rect MeasureInk(float cap)
    {
        Rect ink = MeasuredInk();
        if (ink.height > 0.0001f && ink.width > 0.0001f)
            return ink;

        // Nothing legible came back — a missing atlas, or a face without digits. The number still
        // has to be a sane shape, so fall back to the proportions two digits hold before the
        // squeeze is applied.
        float width = cap * 1.5f;
        return Rect.MinMaxRect(-width * 0.5f, 0f, width * 0.5f, cap);
    }

    /// <summary>Union of the glyphs' own boxes, in the label's own units.</summary>
    private Rect MeasuredInk()
    {
        TMP_TextInfo info = label != null ? label.textInfo : null;
        if (info == null || info.characterCount <= 0 || info.characterInfo == null)
            return default;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        bool any = false;

        int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < count; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible)
                continue;

            any = true;
            Rect glyph = GlyphInk(character);
            minX = Mathf.Min(minX, glyph.xMin);
            minY = Mathf.Min(minY, glyph.yMin);
            maxX = Mathf.Max(maxX, glyph.xMax);
            maxY = Mathf.Max(maxY, glyph.yMax);
        }

        return any ? Rect.MinMaxRect(minX, minY, maxX, maxY) : default;
    }

    /// <summary>
    /// The box one glyph actually covers.
    /// <para>
    /// A character's quad is not its ink: TMP inflates it on all four sides by however much
    /// padding the material's stroke and shadow need, and that padding grows with the effects
    /// rather than with the type. Sizing against the quad is what delivered a cap an eighth under
    /// the one that was asked for, and this material reserves eight texels of it. The glyph's own
    /// metrics are the ink; the width the bold style dilates it by is the difference between the
    /// quad and those metrics, and it is the same for every character on the line.
    /// </para>
    /// </summary>
    private static Rect GlyphInk(TMP_CharacterInfo character)
    {
        Rect quad = Rect.MinMaxRect(
            character.bottomLeft.x,
            character.bottomLeft.y,
            character.topRight.x,
            character.topRight.y
        );

        Glyph glyph = character.alternativeGlyph ?? character.textElement?.glyph;
        if (glyph == null || character.scale <= 0f)
            return quad;

        float metricWidth = glyph.metrics.width * character.scale;
        float metricHeight = glyph.metrics.height * character.scale;
        if (metricWidth <= 0f || metricHeight <= 0f)
            return quad;

        float padding = Mathf.Max(0f, (quad.height - metricHeight) * 0.5f);
        float weight = Mathf.Max(0f, (quad.width - metricWidth) * 0.5f - padding);

        // The quad makes room for that weight across but not down, so vertically the atlas lands
        // on a slightly shorter box than it was cut for and the whole glyph draws squeezed by the
        // difference. Ignoring it is worth about five percent of the cap.
        float grown = quad.height + weight * 2f;
        float squeeze = grown > 0.0001f ? quad.height / grown : 1f;
        float inkWidth = metricWidth + weight * 2f;
        float inkHeight = (metricHeight + weight * 2f) * squeeze;

        Vector2 centre = quad.center;
        return Rect.MinMaxRect(
            centre.x - inkWidth * 0.5f,
            centre.y - inkHeight * 0.5f,
            centre.x + inkWidth * 0.5f,
            centre.y + inkHeight * 0.5f
        );
    }

    /// <summary>
    /// Centres the ink itself on the number's origin. TMP centres on the advance width and the
    /// baseline, neither of which is where digits actually sit inside their own box.
    /// </summary>
    private void PlaceDigits(Rect ink)
    {
        if (label == null)
            return;

        Vector2 centre = ink.center;
        label.rectTransform.localPosition = new Vector3(-centre.x * condense, -centre.y, 0f);
    }

    private void Advance(float time)
    {
        float span = Mathf.Clamp01(time / Life);
        Vector3 position = origin + lead + driftUp * (rise * Mathf.Pow(span, RiseEase));

        float settling = 1f - Mathf.Clamp01(time / RollSeconds);
        float roll = rollSign * RollDegrees * settling * settling;
        if (tone.ShakeDegrees > 0f && time < ShakeSeconds)
        {
            float decay = 1f - time / ShakeSeconds;
            float wave = Mathf.Sin(time * ShakeCyclesPerSecond * Mathf.PI * 2f) * decay * decay;
            roll += tone.ShakeDegrees * wave;
            position += driftRight * (ShakeOffset * wave);
        }

        transform.SetPositionAndRotation(position, ViewRotation() * Quaternion.Euler(0f, 0f, roll));
        transform.localScale = Vector3.one * ScaleAt(time);
        ClampIntoFrame(ResolvedCamera());

        Paint(FadeAt(time));
    }

    /// <summary>
    /// Pushes the number back inside the frame it is being watched through. It is the one element
    /// of a hit a player has to read exactly, and a digit sliced by the frame edge is worse than
    /// no digit at all — a unit fighting on the far side of the board would otherwise carry its
    /// number half off the screen on the way up. Corners are projected rather than the centre, so
    /// the roll and the overshoot are both accounted for, and it runs every frame because the
    /// number climbs most of a cell after it is placed.
    /// </summary>
    private void ClampIntoFrame(Camera view)
    {
        if (view == null || halfWidth <= 0f || halfHeight <= 0f)
            return;

        Vector3 centre = transform.position;
        Vector3 across = transform.right * (halfWidth * transform.localScale.x);
        Vector3 up = transform.up * (halfHeight * transform.localScale.y);

        Vector3 middle = view.WorldToViewportPoint(centre);
        if (middle.z <= 0.01f)
            return;

        Vector3 rising = view.WorldToViewportPoint(centre + across + up);
        Vector3 falling = view.WorldToViewportPoint(centre + across - up);
        float insideX = Inside(
            middle.x,
            Mathf.Max(Mathf.Abs(rising.x - middle.x), Mathf.Abs(falling.x - middle.x))
        );
        float insideY = Inside(
            middle.y,
            Mathf.Max(Mathf.Abs(rising.y - middle.y), Mathf.Abs(falling.y - middle.y))
        );
        if (Mathf.Abs(insideX - middle.x) < 0.0001f && Mathf.Abs(insideY - middle.y) < 0.0001f)
            return;

        // Sliding along the camera's own right and up leaves depth untouched, so one step of the
        // perspective divide is exact rather than something to iterate on.
        Vector3 cameraRight = view.transform.right;
        Vector3 cameraUp = view.transform.up;
        float perRight = view.WorldToViewportPoint(centre + cameraRight).x - middle.x;
        float perUp = view.WorldToViewportPoint(centre + cameraUp).y - middle.y;

        Vector3 shift = Vector3.zero;
        if (Mathf.Abs(perRight) > 0.000001f)
            shift += cameraRight * ((insideX - middle.x) / perRight);
        if (Mathf.Abs(perUp) > 0.000001f)
            shift += cameraUp * ((insideY - middle.y) / perUp);

        transform.position = centre + shift;
    }

    /// <summary>
    /// Nearest place on one viewport axis that keeps the number's whole half-extent, plus a margin,
    /// inside the frame. A number too large to fit at all is centred rather than jammed to an edge.
    /// </summary>
    private static float Inside(float viewport, float halfExtent)
    {
        float low = halfExtent + ViewportMargin;
        float high = 1f - low;
        return low <= high ? Mathf.Clamp(viewport, low, high) : 0.5f;
    }

    private static float ScaleAt(float time)
    {
        if (time < GrowSeconds)
        {
            float growing = Mathf.Clamp01(time / GrowSeconds);
            return OvershootScale * (1f - (1f - growing) * (1f - growing));
        }

        if (time < GrowSeconds + SettleSeconds)
            return Mathf.Lerp(
                OvershootScale,
                1f,
                Mathf.SmoothStep(0f, 1f, (time - GrowSeconds) / SettleSeconds)
            );

        float settled = GrowSeconds + SettleSeconds;
        float held = Mathf.Clamp01((time - settled) / Mathf.Max(0.01f, Life - settled));
        return Mathf.Lerp(1f, RestScale, held);
    }

    private static float FadeAt(float time)
    {
        float leaving = Mathf.Clamp01((time - (Life - FadeSeconds)) / FadeSeconds);
        return 1f - Mathf.SmoothStep(0f, 1f, leaving);
    }

    private void Paint(float fade)
    {
        if (faceMaterial == null || !hasFace)
        {
            if (label != null)
                label.alpha = fade;
            return;
        }

        // The drive rides on the face rather than on the vertex gradient because vertex colours
        // are bytes and cannot exceed one, and one is not enough: a fill authored at pure white
        // tonemaps to 208 and this deck already sits at 181.
        faceMaterial.SetColor(FaceColorId, new Color(tone.Drive, tone.Drive, tone.Drive, fade));

        if (hasOutline)
            faceMaterial.SetColor(OutlineColorId, StrokeInk.WithAlpha(fade));

        if (hasShadow)
            faceMaterial.SetColor(UnderlayColorId, ShadowInk.WithAlpha(ShadowAlpha * fade));
    }

    private static Tone LookFor(DamageTone requested)
    {
        return requested switch
        {
            DamageTone.Heavy => HeavyLook,
            DamageTone.Critical => CriticalLook,
            _ => NormalLook,
        };
    }

    /// <summary>
    /// Which way the number tips as it settles. Hashed off the hit rather than drawn from the
    /// session's random stream, so a re-shot capture lands on the frames the last one did.
    /// </summary>
    private static float RollSign(Vector3 position)
    {
        int hash =
            Mathf.RoundToInt(position.x * 8f) * 73856093
            ^ Mathf.RoundToInt(position.z * 8f) * 19349663;
        return (hash & 1) == 0 ? -1f : 1f;
    }

    private static TMP_FontAsset ResolveFont()
    {
        if (resolvedFont == null)
        {
            resolvedFont = Resources.Load<TMP_FontAsset>(FontResourcePath);
            if (resolvedFont == null)
                resolvedFont = Resources.Load<TMP_FontAsset>(FallbackFontResourcePath);
        }

        // A null here is survivable: TextMeshPro keeps whatever default it loaded for itself, and
        // the number is merely typeset in the wrong face rather than missing.
        return resolvedFont;
    }

    /// <summary>Cap height per em, from the face itself where it reports one.</summary>
    private static float CapPerEm(TMP_FontAsset font)
    {
        if (font == null)
            return CapHeightPerEm;

        float pointSize = font.faceInfo.pointSize;
        float capLine = font.faceInfo.capLine;
        return pointSize > 0f && capLine > 0f ? capLine / pointSize : CapHeightPerEm;
    }

    private static float TightenFor(TMP_FontAsset font)
    {
        float pointSize = font != null ? font.faceInfo.pointSize : 0f;
        return pointSize > 0f ? TightenEm * pointSize : DefaultTighten;
    }

    /// <summary>
    /// How far the digits have to be squeezed to reach the reference proportion, measured off the
    /// face rather than assumed. Digits are tabular in any face worth setting a number in, so one
    /// advance against the cap line describes the whole set.
    /// </summary>
    private static float CondenseFor(TMP_FontAsset font)
    {
        if (font == null || font.characterLookupTable == null)
            return DefaultCondense;

        float capLine = font.faceInfo.capLine;
        if (capLine <= 0f)
            return DefaultCondense;

        if (!font.characterLookupTable.TryGetValue('0', out TMP_Character zero) || zero == null)
            return DefaultCondense;

        float advance = zero.glyph != null ? zero.glyph.metrics.horizontalAdvance : 0f;
        if (advance <= 0f)
            return DefaultCondense;

        return Mathf.Clamp(TargetDigitAdvancePerCap * capLine / advance, MinCondense, 1f);
    }

    private Quaternion ViewRotation()
    {
        Camera view = ResolvedCamera();
        return view != null
            ? view.transform.rotation
            : Quaternion.Euler(BoardPitchDegrees, 0f, 0f);
    }

    private Camera ResolvedCamera()
    {
        if (viewCamera == null)
            viewCamera = PresentingCamera(gameObject.layer);
        return viewCamera;
    }

    /// <summary>
    /// The camera the number is actually watched through: the board's own, unless something enabled
    /// draws over the top of it. The capture rig films the board through a second camera parked
    /// closer in, and a number fitted to a frame nobody is looking through is not fitted at all.
    /// </summary>
    private static Camera PresentingCamera(int layer)
    {
        Camera best = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (best == null)
            best = Camera.main;

        int count = Camera.allCamerasCount;
        if (count <= 0)
            return best;

        if (cameraScratch.Length < count)
            cameraScratch = new Camera[count];
        Camera.GetAllCameras(cameraScratch);

        for (int i = 0; i < count; i++)
        {
            Camera candidate = cameraScratch[i];
            if (candidate == null || candidate.cameraType != CameraType.Game)
                continue;
            if ((candidate.cullingMask & (1 << layer)) == 0)
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }

        return best;
    }

    /// <summary>Authors a swatch in the sRGB values a palette is picked in.</summary>
    private static Color Rgb(int red, int green, int blue)
    {
        return new Color(red / 255f, green / 255f, blue / 255f, 1f);
    }
}
