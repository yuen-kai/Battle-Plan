using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How often the game is allowed to draw.
///
/// Battle Plan is turn-based: for most of a match the board is a still image waiting on a decision,
/// and drawing that image 120 times a second costs a laptop exactly what drawing something that
/// moves would. Nothing here asks for a high frame rate — there is no aiming, no camera control, and
/// the fastest thing on screen is a unit walking a cell — so the frame rate is pinned at something a
/// tactics game cannot tell the difference at, and dropped much further when the window is not even
/// being looked at.
///
/// Three levers, because they cover different things:
/// <list type="bullet">
/// <item><b>vSync interval</b> halves the frame rate on a high-refresh display while still drawing
/// every frame it presents. Nothing downstream can tell — a screenshot, a studio camera — which is
/// why this is the one used while the window is in front.</item>
/// <item><b>Render frame interval</b> skips drawing entirely for frames nobody will see. Cheaper
/// still, but it leaves the previous frame in the framebuffer, so it is only used unfocused, and
/// <see cref="HoldFullRate"/> exists for the dev tools that read that framebuffer.</item>
/// <item><b>The idle rate</b> halves the presented rate again while the board is a still image and
/// the player is not touching it — which, in a turn-based game, is most of a match. It runs through
/// the vSync interval rather than the render frame interval on purpose: every frame is still drawn,
/// so nothing goes stale while the opponent's moves and the round clock keep arriving.</item>
/// </list>
///
/// <see cref="Application.targetFrameRate"/> is set alongside them as a floor rather than as the
/// mechanism: the Editor ignores it outright, and so does any platform presenting on vSync.
/// </summary>
public sealed class FrameRatePolicy : MonoBehaviour
{
    /// <summary>
    /// What a match is paced at. A round is decided in planning and watched in execution; neither
    /// reads better above this, and the drag that draws a route is comfortable at it.
    /// </summary>
    public const int FocusedFrameRate = 60;

    /// <summary>
    /// Unfocused. Low enough to idle the GPU, not zero: a backgrounded client still has a match
    /// running on it, and <c>runInBackground</c> is on precisely so it keeps answering.
    /// </summary>
    public const int UnfocusedFrameRate = 10;

    /// <summary>
    /// What planning is paced at. Nothing on the board moves while a round is being decided — the
    /// most that happens is a highlight following the pointer — so the half rate is spent on an
    /// image that is, frame to frame, the same image. Deliberately a halving and not a stop: a
    /// stopped frame rate means a stale framebuffer, and planning is exactly when the opponent's
    /// moves and the round clock are still arriving.
    /// </summary>
    public const int IdleFrameRate = 30;

    /// <summary>
    /// How long after the last input the full rate is held. A route is dragged, not clicked, and
    /// the drag has to stay smooth across the gaps between individual mouse events.
    /// </summary>
    private const float InputCoastSeconds = 0.75f;

    private static bool holdFullRate;

    private Vector3 lastMousePosition;
    private float lastInputTime = float.NegativeInfinity;
    private int appliedVSyncCount = -1;
    private int appliedTargetFrameRate = -1;
    private int appliedRenderFrameInterval = -1;

    public static FrameRatePolicy Instance { get; private set; }

    /// <summary>
    /// Set while something else owns the frame pacing outright. The GPU cost probe is the only
    /// caller: it has to free the frame rate to measure anything, and a focus change landing
    /// mid-measurement would otherwise re-cap the run half way through it.
    /// </summary>
    public static bool Suspended { get; set; }

    /// <summary>
    /// Held by anything that needs every frame actually drawn into the framebuffer — the E2E
    /// screenshots are the only callers. Studio captures render their own camera explicitly, which
    /// no frame-rate policy can skip, so they need nothing.
    /// </summary>
    public static bool HoldFullRate
    {
        get => holdFullRate;
        set
        {
            if (holdFullRate == value)
                return;
            holdFullRate = value;
            Instance?.Apply();
        }
    }

    internal static void ClearRuntimeState()
    {
        holdFullRate = false;
        Suspended = false;
        Instance = null;
    }

    internal static void Create()
    {
        if (Instance != null)
            return;

        GameObject holder = new(nameof(FrameRatePolicy));
        DontDestroyOnLoad(holder);
        Instance = holder.AddComponent<FrameRatePolicy>();
    }

    /// <summary>
    /// How many vblanks to wait between presented frames, to land near <paramref name="targetFrameRate"/>.
    /// A pure function of the two rates, so it can be checked without a display. Clamped to what
    /// Unity accepts; never zero, which would mean presenting without waiting at all.
    /// </summary>
    public static int ResolveVSyncCount(double refreshRate, int targetFrameRate)
    {
        if (refreshRate <= 0d || targetFrameRate <= 0)
            return 1;
        return Mathf.Clamp(Mathf.RoundToInt((float)(refreshRate / targetFrameRate)), 1, 4);
    }

    /// <summary>The presented rate for a board being watched rather than decided.</summary>
    public static int ResolveVSyncCount(double refreshRate) =>
        ResolveVSyncCount(refreshRate, FocusedFrameRate);

    /// <summary>
    /// Whether a phase has something moving in it. Execution walks units across cells and the dodge
    /// window is timed; everything else is a still board waiting on a person. Anything unrecognised
    /// is treated as still, which costs it half its frames and never a correct one.
    /// </summary>
    public static bool PhaseAnimates(string phase) => phase is "executing" or "dodging";

    /// <summary>
    /// How many frames to skip drawing between the ones that are drawn, to land near a target rate.
    /// Never zero: one means "draw every frame".
    /// </summary>
    public static int ResolveRenderFrameInterval(double refreshRate, int targetFrameRate)
    {
        if (refreshRate <= 0d || targetFrameRate <= 0)
            return 1;
        return Mathf.Max(1, Mathf.RoundToInt((float)(refreshRate / targetFrameRate)));
    }

    private void OnEnable()
    {
        Instance = this;
        // A quality change re-applies that level's own vSync count, which is not ours.
        GameSettings.Changed += OnSettingsChanged;
        Forget();
        Apply();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= OnSettingsChanged;
        if (Instance == this)
            Instance = null;
    }

    private void OnApplicationFocus(bool hasFocus) => Apply();

    private void OnSettingsChanged(GameSettings settings)
    {
        // The level just wrote its own vSync count over ours, so what we last applied is no longer
        // what is set and the guard in Apply would skip putting it back.
        Forget();
        Apply();
    }

    /// <summary>Drops the record of what is applied, so the next Apply writes all three again.</summary>
    private void Forget()
    {
        appliedVSyncCount = -1;
        appliedTargetFrameRate = -1;
        appliedRenderFrameInterval = -1;
    }

    private void Update()
    {
        if (SawInput())
            lastInputTime = Time.unscaledTime;
        Apply();
    }

    /// <summary>
    /// Whether the player is touching anything this frame. Pointer movement counts on its own: the
    /// board highlights whatever is under the cursor, so a mouse crossing it is a moving image even
    /// with no button held.
    /// </summary>
    private bool SawInput()
    {
        Vector3 mousePosition = Input.mousePosition;
        bool moved = mousePosition != lastMousePosition;
        lastMousePosition = mousePosition;
        return moved
            || Input.anyKey
            || Input.touchCount > 0
            || Input.mouseScrollDelta.sqrMagnitude > 0f;
    }

    /// <summary>
    /// The rate a frame is owed. Full while something is moving or the player is mid-gesture, half
    /// while the board is a still image nobody is touching, and far less than either when the
    /// window is behind something. Pure, so every branch can be checked without a display, a match,
    /// or a mouse.
    /// </summary>
    public static int ResolveTargetFrameRate(
        bool inFront,
        bool holdFullRate,
        string phase,
        float secondsSinceInput
    )
    {
        if (!inFront)
            return UnfocusedFrameRate;
        if (holdFullRate || PhaseAnimates(phase))
            return FocusedFrameRate;
        return secondsSinceInput < InputCoastSeconds ? FocusedFrameRate : IdleFrameRate;
    }

    private void Apply()
    {
        if (Suspended)
            return;

        double refreshRate = RefreshRate;
        bool inFront = HoldFullRate || Application.isFocused;
        int targetFrameRate = ResolveTargetFrameRate(
            inFront,
            HoldFullRate,
            GameLoop.currentPhase,
            Time.unscaledTime - lastInputTime
        );

        int vSyncCount = inFront ? ResolveVSyncCount(refreshRate, targetFrameRate) : 1;
        int renderFrameInterval = inFront
            ? 1
            : ResolveRenderFrameInterval(refreshRate, UnfocusedFrameRate);

        // Every frame asks; only a change writes. Assigning vSyncCount is not free enough to do
        // sixty times a second for nothing.
        if (vSyncCount != appliedVSyncCount)
        {
            QualitySettings.vSyncCount = vSyncCount;
            appliedVSyncCount = vSyncCount;
        }
        if (targetFrameRate != appliedTargetFrameRate)
        {
            Application.targetFrameRate = targetFrameRate;
            appliedTargetFrameRate = targetFrameRate;
        }
        if (renderFrameInterval != appliedRenderFrameInterval)
        {
            OnDemandRendering.renderFrameInterval = renderFrameInterval;
            appliedRenderFrameInterval = renderFrameInterval;
        }
    }

    private static double RefreshRate
    {
        get
        {
            double rate = Screen.currentResolution.refreshRateRatio.value;
            return rate > 1d ? rate : FocusedFrameRate;
        }
    }
}
