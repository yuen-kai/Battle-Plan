using UnityEngine;

/// <summary>
/// Re-alpha a palette entry at the call site, so a shape that needs its own opacity does not have
/// to restate the hue and drift from the token.
/// </summary>
public static class PaletteColorExtensions
{
    public static Color WithAlpha(this Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}

/// <summary>
/// The one place a colour with gameplay meaning is written down.
///
/// Before this existed the same two teams were six different blues and six different reds — the
/// unit material, the projectile, the death ring, the HUD flash, the hill, the map preview and the
/// USS tokens each carried their own literal. A player cannot learn "red is the enemy" when red is
/// a different red in every system, so every one of those call sites now reads from here.
///
/// Values are display sRGB, matching how Unity serialises a material colour property and how the
/// USS tokens in <c>TacticalToyboxTokens.uss</c> state the same colours. When a value changes here
/// it must change in that file too; the pairs are named identically so the two stay greppable.
///
/// Two axes that are easy to confuse:
/// <list type="bullet">
/// <item><b>Viewer-relative</b> (<see cref="Friendly"/> / <see cref="Enemy"/>) — "mine" and
/// "theirs". Both seats read their own crew as blue. Almost everything wants this.</item>
/// <item><b>Absolute</b> (<see cref="ForTeamIndex"/>) — team 0 and team 1 regardless of who is
/// watching. Only correct for something both players must agree on the identity of, which in
/// practice is the King of the Hill pad.</item>
/// </list>
/// </summary>
public static class TeamPalette
{
    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out Color parsed) ? parsed : Color.magenta;

    // ------------------------------------------------------------------ team identity
    // The shipped ultramarine and crimson, at full chroma. A muted pair was tried and reverted:
    // it measured better on two axes nobody looks at (luminance parity between the teams, and the
    // red's value separation from the deck) and worse on the ones that matter — it lost CIE76
    // distance under colour-vision deficiency (96 against 123) and dropped the enemy card's
    // border below the 3:1 it needs to be identifiable. It also simply read as washed out on a
    // bright board, which is the whole reason these are saturated in the first place.
    //
    // The one real cost of this pair: the red is close to the deck in luminance (1.75:1), so red
    // units separate from the board by hue where blue units separate by value as well.

    /// <summary>The local player's crew. Unit bodies, their VFX, and friendly HUD accents.</summary>
    public static readonly Color Friendly = Hex("#2F43F5");

    /// <summary>The opposing crew.</summary>
    public static readonly Color Enemy = Hex("#F83547");

    /// <summary>
    /// Lifted for anything that has to carry against the dark HUD or emit on the bright board:
    /// label text, emission, tracers, rings. Fully saturated and kept in the core hues rather
    /// than drifting to cyan the way the old per-system literals did; both clear AA on
    /// <c>--toy-surface</c>.
    /// </summary>
    public static readonly Color FriendlyBright = Hex("#6E7BFF");

    /// <inheritdoc cref="FriendlyBright"/>
    public static readonly Color EnemyBright = Hex("#FF6B7A");

    /// <summary>Pressed and held states for a team-coloured fill.</summary>
    public static readonly Color FriendlyDeep = Hex("#0B22EB");

    /// <inheritdoc cref="FriendlyDeep"/>
    public static readonly Color EnemyDeep = Hex("#ED081D");

    // ------------------------------------------------------------------ board signals
    // Every one of these sits on the deck (#A8B3B8, relative luminance 0.44), which is the middle
    // of the board's value range. That middle is why the old signals were invisible: amber, peach
    // and mid-blue all land within 1.2:1 of the deck, so they differed in hue only. These separate
    // by value first and carry hue second.

    /// <summary>
    /// Reachable cells. A cool veil that darkens the deck rather than a mid-blue that matches it,
    /// and deliberately not the team blue — movement is a place, not a person.
    /// </summary>
    public static readonly Color MoveRange = new(0.059f, 0.498f, 0.549f, 0.55f);

    /// <summary>
    /// Cells an ability can be aimed at. Warm against <see cref="MoveRange"/>'s cool at the same
    /// value, which mirrors the amber the unit card already uses for ability mode. The two are
    /// mutually exclusive modes, so they need to be unmistakable rather than simultaneously
    /// legible: CIE76 34 apart at worst across all three colour-vision simulations.
    /// </summary>
    public static readonly Color AbilityRange = new(0.541f, 0.353f, 0.039f, 0.55f);

    /// <summary>
    /// What an ability is about to do, shown to both players. The same amber as
    /// <see cref="AbilityRange"/> because it is the same concept one step later — it replaces
    /// three near-miss oranges that used to mean this.
    /// </summary>
    public static readonly Color AbilityTelegraph = new(0.541f, 0.353f, 0.039f, 1f);

    /// <summary>
    /// The same ability amber lifted to emission, for the charge dial under a unit and anything
    /// else that carries the ability language as light on the board rather than as ink on the
    /// deck. <see cref="AbilityRange"/> is picked to darken the deck it lies on and is far too low
    /// in value to glow; this is its bright sibling, the way <see cref="FriendlyBright"/> is
    /// <see cref="Friendly"/>'s. It is deliberately the accent the HUD already spends on an
    /// ability-mode card (<c>--toy-primary</c>), so the dial on the board and the card that
    /// commands it are one colour and not two near-miss oranges. That the health ramp's warning
    /// step is this same hex is a coincidence of the theme having exactly one warm accent.
    /// </summary>
    public static readonly Color AbilityBright = Hex("#F18F01");

    /// <summary>
    /// An unclaimed hill. Neutral bone rather than the old amber: unclaimed should not borrow the
    /// colour that means "your next action", and it leaves amber free for the ability language.
    /// Reuses the board's existing cream so it belongs to the diorama.
    /// </summary>
    public static readonly Color HillUnclaimed = Hex("#E8E2D4");

    /// <summary>
    /// A route ending somewhere it cannot stop. Shares the enemy red on purpose — both mean "this
    /// will not go the way you want" — but it is now that exact red rather than a sixth one.
    /// </summary>
    public static readonly Color RouteBlocked = Hex("#C43530");

    // ------------------------------------------------------------------ health
    // A ramp, so the bar says how bad it is and not merely how much is left. The old bar was
    // danger red at full health on both teams, which meant the fill only ever encoded length.

    /// <summary>Above <see cref="HealthWarningThreshold"/>.</summary>
    public static readonly Color HealthHigh = Hex("#5CC87A");

    /// <summary>Between the two thresholds.</summary>
    public static readonly Color HealthWarning = Hex("#F18F01");

    /// <summary>At or below <see cref="HealthCriticalThreshold"/>.</summary>
    public static readonly Color HealthCritical = Hex("#BA3A2D");

    public const float HealthWarningThreshold = 0.6f;
    public const float HealthCriticalThreshold = 0.3f;

    // ------------------------------------------------------------------ HUD wash
    // Full-screen tints behind a phase change. Alphas are the load-bearing part; the hues only
    // have to agree with the team pair above.

    public static readonly Color FlashFriendly = new(0.227f, 0.373f, 0.851f, 0.16f);
    public static readonly Color FlashEnemy = new(0.769f, 0.208f, 0.188f, 0.16f);
    public static readonly Color FlashNeutral = new(0.945f, 0.561f, 0.004f, 0.14f);

    // ------------------------------------------------------------------ accessors

    /// <summary>Viewer-relative: what this team looks like on the local player's screen.</summary>
    public static Color ForViewer(bool friendly) => friendly ? Friendly : Enemy;

    /// <inheritdoc cref="ForViewer(bool)"/>
    public static Color BrightForViewer(bool friendly) => friendly ? FriendlyBright : EnemyBright;

    /// <summary>
    /// Absolute: team 0 is blue and team 1 is red on both screens. Only for state both players
    /// must name the same way, which is the hill and nothing else.
    /// </summary>
    public static Color ForTeamIndex(int teamIndex) => teamIndex == 0 ? Friendly : Enemy;

    /// <summary>Where a bar of this fraction sits on the ramp.</summary>
    public static Color ForHealthFraction(float fraction)
    {
        if (fraction <= HealthCriticalThreshold)
            return HealthCritical;
        return fraction <= HealthWarningThreshold ? HealthWarning : HealthHigh;
    }

    /// <summary>
    /// USS class suffix for the same ramp, so the HUD card and the world-space bar cannot drift
    /// apart. Pairs with the <c>unit-card__health-fill--*</c> rules in <c>GameHUD.uss</c>.
    /// </summary>
    public static string HealthClassSuffix(float fraction)
    {
        if (fraction <= HealthCriticalThreshold)
            return "critical";
        return fraction <= HealthWarningThreshold ? "warning" : "high";
    }
}
