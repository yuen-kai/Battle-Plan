using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour
{
    public UnitData unitData;

    // NetworkVariables (not ClientRpcs) so health/alive state survives fog NetworkHide/NetworkShow:
    // NGO drops object-scoped RPCs for clients the object is hidden from, but resyncs
    // NetworkVariables on NetworkShow.
    private NetworkVariable<float> currentHealth = new();
    private NetworkVariable<bool> isAlive = new(true);

    private float damageTakenMultiplier = 1f;
    private readonly NetworkVariable<bool> damageReductionActive = new(false);

    private GuardOrbVisual guardOrb;

    private Transform unitCanvas;
    private Transform healthBar;
    private Transform healthFill;
    private UnityEngine.UI.Image healthFillImage;
    private float unitCanvasHeight;

    private const float TypicalMaxHealth = 100f;

    /// <summary>
    /// The air the bar keeps above its own unit on screen, given as world units of lift per unit of
    /// distance from the lens. A slope rather than a length, so it holds the same share of the frame
    /// at any range and any field of view. Sized to clear a whole unit silhouette — a 3.1-unit body
    /// seen from 73 degrees above — with a little left over.
    /// </summary>
    private const float MinCanvasScreenGap = 0.07f;

    /// <summary>Read-only HP accessor for dev tooling/tests (server-authoritative value on host).</summary>
    public float CurrentHealth => currentHealth.Value;
    public float MaxHealth => unitData != null ? Mathf.Max(0f, unitData.maxHealth) : 0f;
    public bool IsAlive => isAlive.Value;
    public bool HasDamageReduction => damageReductionActive.Value;

    public override void OnNetworkSpawn()
    {
        unitCanvas = transform.Find("UnitCanvas");
        healthBar = unitCanvas != null ? unitCanvas.Find("HealthBar") : null;
        healthFill = healthBar != null ? healthBar.Find("HealthFill") : null;
        healthFillImage =
            healthFill != null ? healthFill.GetComponent<UnityEngine.UI.Image>() : null;
        unitCanvasHeight =
            unitCanvas is RectTransform canvasRect
                ? canvasRect.anchoredPosition.y * transform.localScale.y
                : 0f;

        currentHealth.OnValueChanged += OnHealthChanged;
        isAlive.OnValueChanged += OnAliveChanged;
        damageReductionActive.OnValueChanged += OnDamageReductionChanged;

        if (IsServer)
        {
            isAlive.Value = true;
            currentHealth.Value = unitData.maxHealth;
            damageTakenMultiplier = 1f;
            damageReductionActive.Value = false;
        }

        RefreshGuardPresentation(damageReductionActive.Value);

        UpdateMaxHealthScale();
        UpdateHealthFill(currentHealth.Value);

        // A hidden unit that died before NetworkShow must immediately stay absent on this client.
        if (!IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isAlive.OnValueChanged -= OnAliveChanged;
        damageReductionActive.OnValueChanged -= OnDamageReductionChanged;
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsClient || unitCanvas == null)
            return;

        Camera view = GameLoop.ViewCamera;
        if (view == null)
            return;

        unitCanvas.forward = view.transform.forward;
        unitCanvas.position = CanvasPosition(view.transform);
    }

    /// <summary>
    /// Where the bar has to sit to be readable from the board camera.
    /// <para>
    /// The camera looks down at 73 degrees from just short of the host's own back row, so the near
    /// rank — which is the local player's crew, and row zero is where it deploys — is seen from
    /// almost directly overhead and from the far side of its own vertical axis. Height in world Y
    /// then projects *down* the screen instead of up it: authored between 1.3 and 2 units above the
    /// unit, the bar came out underneath the unit wearing it, tangled in its own silhouette, for
    /// exactly the crew the player watches most. The far rank is seen at a slant and had no such
    /// problem, which is why only friendly bars read badly.
    /// </para>
    /// <para>
    /// The authored height is kept as the per-unit intent it is, and topped up along the camera's
    /// own up axis until the bar clears its unit by <see cref="MinCanvasScreenGap"/> on screen.
    /// Lifting along that axis leaves depth untouched, so a single correction lands exactly on the
    /// gap asked for, and it carries the bar away from the lens rather than towards it — nothing
    /// new can come between the two.
    /// </para>
    /// </summary>
    private Vector3 CanvasPosition(Transform lens)
    {
        Vector3 unitPosition = transform.position;
        Vector3 canvasPosition = unitPosition + Vector3.up * unitCanvasHeight;

        Vector3 toUnit = unitPosition - lens.position;
        Vector3 toCanvas = canvasPosition - lens.position;
        float unitDepth = Vector3.Dot(toUnit, lens.forward);
        float canvasDepth = Vector3.Dot(toCanvas, lens.forward);
        if (unitDepth <= 0f || canvasDepth <= 0f)
            return canvasPosition;

        float screenGap =
            Vector3.Dot(toCanvas, lens.up) / canvasDepth - Vector3.Dot(toUnit, lens.up) / unitDepth;

        return screenGap >= MinCanvasScreenGap
            ? canvasPosition
            : canvasPosition + lens.up * ((MinCanvasScreenGap - screenGap) * canvasDepth);
    }

    public void ApplyDamageReductionForRound(float multiplier)
    {
        if (!IsServer || !isAlive.Value)
            return;

        damageTakenMultiplier = Mathf.Min(damageTakenMultiplier, Mathf.Clamp01(multiplier));
        damageReductionActive.Value = damageTakenMultiplier < 1f;
    }

    public void ClearDamageReduction()
    {
        damageTakenMultiplier = 1f;
        if (IsServer)
            damageReductionActive.Value = false;
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer || !isAlive.Value)
            return;

        // The tutorial sandbox teaches; it does not kill. Hits still land and still read on the
        // health bar, but neither crew can be eliminated, so a fumbled dodge cannot end the lesson
        // script early or hand a first-time player a defeat.
        float floor = TutorialSession.IsActive ? 1f : 0f;
        currentHealth.Value = Mathf.Max(floor, currentHealth.Value - damage * damageTakenMultiplier);
        if (currentHealth.Value > 0f)
        {
            GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);
            return;
        }

        isAlive.Value = false;
        GetComponent<Movement>()?.ClearTemporaryMoveSpeedBoost();
        ClearDamageReduction();
        GameLoop.Instance?.DisableUnitCard(gameObject);
        GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);

        // Leave the NetworkObject active through this frame's network update so the final
        // NetworkVariable values can be sent before round-end arbitration.
        StartCoroutine(DeactivateOnServerNextFrame());
    }

#if UNITY_EDITOR
    // Staging a wounded unit is a drop in HP like any other, so it would otherwise throw a damage
    // number and a hit reaction on the first frame of a shot that has not started yet.
    private bool devSuppressImpact;

    /// <summary>
    /// Dev-only: sets current HP directly so a capture rig can stage a wounded unit.
    /// <para>
    /// This deliberately bypasses <see cref="TakeDamage"/>. Reaching a low HP the honest way would
    /// mean killing and respawning between staged shots, and a kill runs round-end arbitration —
    /// wiping a crew to set up a shot would end the match the rig is filming inside. It clamps
    /// above zero so it can never be the thing that kills a unit; only real damage does that.
    /// </para>
    /// </summary>
    public void DevSetHealth(float value)
    {
        if (!IsServer || !isAlive.Value)
            return;
        devSuppressImpact = true;
        currentHealth.Value = Mathf.Clamp(value, 1f, MaxHealth);
    }
#endif

    /// <summary>
    /// Server-only revival for respawn-enabled modes. Restores health and transient
    /// movement/shooting state without resetting the unit's ability cooldown.
    /// </summary>
    public bool RespawnAt(Vector3 position, Quaternion rotation)
    {
        if (!IsServer || isAlive.Value)
            return false;

        gameObject.SetActive(true);
        transform.SetPositionAndRotation(position, rotation);
        GetComponent<Ability>()?.ResetForRespawn();

        Movement movement = GetComponent<Movement>();
        if (movement != null)
        {
            movement.PauseMovement();
            movement.ClearTemporaryMoveSpeedBoost();
            movement.moving = false;
        }
        ClearDamageReduction();

        Shooting shooting = GetComponent<Shooting>();
        shooting?.PauseShooting();

        Transform alert = transform.Find("UnitCanvas/Alert");
        if (alert != null)
            alert.gameObject.SetActive(false);

        currentHealth.Value = unitData.maxHealth;
        isAlive.Value = true;
        GetComponent<AnimationHandler>()?.PlayAnimation("Idle");
        GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);
        return true;
    }

    private IEnumerator DeactivateOnServerNextFrame()
    {
        yield return null;
        if (IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        UpdateHealthFill(newValue);

#if UNITY_EDITOR
        if (devSuppressImpact)
        {
            devSuppressImpact = false;
            return;
        }
#endif

        // Impact frame on every peer; NetworkVariable callbacks also fire after fog NetworkShow
        // resync, but only react to an actual decrease.
        if (newValue >= previousValue || !gameObject.activeInHierarchy)
            return;

        float damage = previousValue - newValue;
        float severity = damage / Mathf.Max(1f, MaxHealth);

        // A lethal hit's body belongs to OnAliveChanged, which throws it clear rather than rocking
        // it back onto its feet; running both would stamp the same contact twice on one frame.
        if (newValue > 0f)
            HitReaction.Play(gameObject, HitOrigin(), severity);

        DamagePopup.Spawn(transform.position, damage, ToneFor(severity, newValue <= 0f));
    }

    /// <summary>
    /// Where a hit came from, for knockback direction only — never for anything authoritative.
    /// <para>
    /// Damage reaches this peer as a <c>NetworkVariable</c> callback carrying a number and nothing
    /// else, and the two honest ways to learn the attacker — widening <see cref="TakeDamage"/> or
    /// replicating the source — are both new authoritative state bought for a visual. The unit's
    /// own facing is the nearest thing already replicated: <see cref="Shooting"/> turns a unit to
    /// look at whatever it is engaging, so in a firefight the return fire is coming from in front
    /// of it, and where it is not the direction is merely arbitrary rather than wrong.
    /// </para>
    /// </summary>
    private Vector3 HitOrigin() => transform.position + transform.forward * GameLoop.cellSize;

    /// <summary>
    /// How loudly the damage number reads. Forty percent of a health bar is the same line this
    /// file already drew between a graze and a real hit; a killing blow always gets the loudest
    /// badge, because it is the last thing that unit will ever show the player.
    /// </summary>
    private static DamageTone ToneFor(float severity, bool lethal)
    {
        if (lethal || severity >= 0.7f)
            return DamageTone.Critical;
        return severity >= 0.4f ? DamageTone.Heavy : DamageTone.Normal;
    }

    private void OnDamageReductionChanged(bool previousValue, bool newValue)
    {
        RefreshGuardPresentation(newValue);
    }

    private void RefreshGuardPresentation(bool active)
    {
        if (active && guardOrb == null)
            guardOrb = GuardOrbVisual.Attach(gameObject);
        if (guardOrb != null)
            guardOrb.SetGuarded(active);
    }

    private void OnAliveChanged(bool previousValue, bool newValue)
    {
        // A landing throws the deck outward from a point of contact and leaves a rider standing in
        // the middle of it; a death opens the deck under a unit and takes it down. Those are the
        // two most important reads on the board and they must not share a silhouette, so nothing
        // here travels outward and nothing here is warm. Spawned as independent objects so they
        // outlive the deactivation below.
        if (previousValue && !newValue)
        {
            // Viewer-relative, matching the unit body and its tracers. The team read belongs to
            // the body going down — it is the unit's own paint, and it is the last of it anyone
            // will see. The hole itself is the same cold colour for both crews.
            Color teamColor = TeamPalette.BrightForViewer(
                GameLoop.IsTeamFriendlyToLocalPlayer(
                    gameObject.CompareTag("BlueTeam")
                        ? GameLoop.HostTeamIndex
                        : GameLoop.OpponentTeamIndex
                )
            );
            HitReaction.PlayDeath(gameObject, HitOrigin(), teamColor);
            DeathCollapse.Spawn(transform.position);
        }

        // The host/server controls its active state directly. Death hides the remote object;
        // fog-authorized respawn observers are reactivated after visibility is resolved.
        if (!IsServer && !newValue)
            gameObject.SetActive(false);
    }

    private void UpdateHealthFill(float health)
    {
        if (healthFill == null)
            return;

        float fraction = Mathf.Clamp(health / unitData.maxHealth, 0f, 1f);
        healthFill.localScale = new Vector3(fraction, 1f, 1f);

        // Same ramp and same thresholds as the HUD card. These two bars show one number, and
        // before this one of them was permanently green while the other was permanently red.
        if (healthFillImage != null)
            healthFillImage.color = TeamPalette.ForHealthFraction(fraction);
    }

    private void UpdateMaxHealthScale()
    {
        if (healthBar == null)
            return;

        healthBar.localScale = new Vector3(
            Mathf.Clamp(unitData.maxHealth / TypicalMaxHealth, 0f, 1f),
            1f,
            1f
        );
    }
}

/// <summary>
/// What a death leaves on the board: a hole where the unit stood, and the deck closing over it.
/// <para>
/// Every other impact in the game throws a front outward from a point, which is precisely why a
/// death that spawned a shockwave read as an arrival instead of a loss. Nothing here expands past
/// the footprint of the thing that stopped existing — about a third of the ground a landing's dust
/// front covers — and nothing here is warm.
/// </para>
/// <para>
/// The deck sits near luminance 182 of 255 and a grade with contrast +14 crushes the shadows, so
/// the throat floor is authored down at luminance 6: an actual negative rather than the mid-grey
/// clod this used to be. What makes a flat card read as depth is where the light sits. A pit seen
/// at 73 degrees shows its far inner wall as a tall lit crescent under the far rim, its near rim as
/// an unlit overhang, and its deepest point below the middle rather than at it. Lighting the middle
/// instead — which is what this did — is a glow inside a stain.
/// </para>
/// <para>
/// The colour is the other half of the separation. A landing owns warm orange embers at hue 33; this
/// owns cold cyan a hundred and fifty degrees away, and owns it in area rather than as a rim. It is
/// authored so blue clips first and red never does: without that red floor a fully saturated cyan
/// caps at luminance 201 however hard it is driven, which is how a hole this bright managed to put
/// only twenty-three pixels over the line that counts.
/// </para>
/// <para>Purely local and visual. Runs on every peer from the alive-state callback.</para>
/// </summary>
static class DeathCollapse
{
    /// <summary>
    /// The body's clock, read by <c>HitReaction</c>'s corpse so the hole and the thing falling
    /// into it cannot drift apart. Sized in captured frames: the deck is filmed at 30Hz, so the
    /// sink is five to six frames and the half-submerged hold is three.
    /// </summary>
    internal const float SinkSeconds = 0.17f;

    /// <inheritdoc cref="SinkSeconds"/>
    internal const float SubmergedSeconds = 0.1f;

    /// <inheritdoc cref="SinkSeconds"/>
    internal const float SwallowSeconds = 0.15f;

    /// <summary>How far into the deck the body's feet go before it is held, in world units.</summary>
    internal const float SinkDepth = 0.46f;

    /// <summary>
    /// What is left of the body's height and width once the deck has it. At 42 percent of 1.7
    /// units the crushed body stands 0.71 high and its feet are 0.46 down, which leaves it a
    /// little under two-thirds submerged for the three frames it is held there.
    /// </summary>
    internal const float CrushHeight = 0.42f;

    /// <inheritdoc cref="CrushHeight"/>
    internal const float CrushSpread = 1.22f;

    /// <summary>
    /// The one colour a death owns, in linear rgb. Blue clips first, green follows and red never
    /// does; graded, that lands at hue 183 degrees, saturation 0.65 and luminance 212, which is a
    /// hundred and fifty degrees off the warm orange a landing throws and bright enough to be
    /// seen. A cyan with no red in it cannot exceed luminance 201 whatever it is multiplied by,
    /// so the red term is what buys the brightness rather than any extra intensity.
    /// </summary>
    internal static readonly Color VoidLight = new(0.46f, 3.8f, 4.95f, 1f);

    internal static void Spawn(Vector3 position)
    {
        GameObject root = new("DeathCollapse");
        root.transform.position = new Vector3(position.x, 0f, position.z);
        root.AddComponent<DeathCollapseRunner>().Build();
    }
}

/// <summary>Drives one collapse: the hole, the light in it, and the deck falling into it.</summary>
sealed class DeathCollapseRunner : MonoBehaviour
{
    // Unit scale, not blast scale. A Pogo landing's plates cover three and a half square cells;
    // this is the size of the thing that stopped existing, and that gap does as much work
    // separating the two events as anything either of them is made of.
    const float HoleCells = 1.35f;

    // Headroom on the quad for the fissures to run out into. The hole's own outline is driven
    // against the figure above and never against the quad's edge, which would cut it straight.
    const float HoleQuadPad = 2.15f;
    const float HoleRadiusCap = 0.52f;

    // How far the fissures reach past the hole, as a fraction of the quad. The longest runs to
    // just under two hole radii, which is what stops the footprint having a circle anywhere on
    // its boundary. They are also the only saturated colour that costs the void none of its
    // black, because they run across floor the hole never darkens.
    const float CrackLength = 0.9f;

    // Under the fog overlay tiles at y 0.05, so the hole reads as part of the deck rather than as
    // something lying on it. Clear of Aftermath's 0.028 so two marks on one tile cannot fight.
    const float HoleHeight = 0.034f;

    // The money frame runs from OpenSeconds to PeakSeconds: the hole is at full width, the light
    // in it is at full strength and clipping, and the body is half in the deck. Three captured
    // frames, so a 30Hz sample cannot step over it. Both edge down by a few percent across the
    // hold, which is what keeps the strip picker's peak on the first of the three rather than the
    // last — the panel four frames earlier is then still the unit standing on a cracking deck.
    const float OpenSeconds = DeathCollapse.SinkSeconds;
    const float PeakSeconds = OpenSeconds + DeathCollapse.SubmergedSeconds;
    const float HoldShrink = 0.97f;
    const float HoldGlow = 0.93f;

    // The light beats the hole: the deck splits, the shaft lights two frames later, and the black
    // only wins once the throat has drained. Draining finishes a frame before the strip cuts.
    const float LightSeconds = 0.075f;
    const float DrainStart = 0.1f;
    const float DrainEnd = 0.185f;

    // Nothing fades. The hole leaves by closing across itself from a flank that walks around the
    // rim while the deck takes bites out of what is left, so no two captured frames share a
    // silhouette on the way out.
    const float CloseSeconds = 0.74f;
    const float CloseGain = 1.34f;
    const float CloseSpin = 4.2f;
    const float LifeSeconds = PeakSeconds + CloseSeconds;

    // Radians per second on the torn boundary. Fast enough that the outline is measurably
    // different in every captured frame — the previous rate left eight consecutive frames sharing
    // 96 percent of their silhouette, which is the frozen-decal failure this whole piece was
    // rejected for.
    const float PhaseRate = 22f;

    // One dominant plate, two mid, four small: five to one across the set, because an even spread
    // of same-sized fragments reads as confetti however dark it is.
    const int ShardCount = 7;
    const float ShardSinkSeconds = 0.1f;

    static readonly int FloorColorId = Shader.PropertyToID("_FloorColor");
    static readonly int RubbleColorId = Shader.PropertyToID("_RubbleColor");
    static readonly int WallColorId = Shader.PropertyToID("_WallColor");
    static readonly int LipColorId = Shader.PropertyToID("_LipColor");
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
    static readonly int GlowId = Shader.PropertyToID("_Glow");
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int ThroatId = Shader.PropertyToID("_Throat");
    static readonly int WallSoftId = Shader.PropertyToID("_WallSoft");
    static readonly int CrestAtId = Shader.PropertyToID("_CrestAt");
    static readonly int LipWidthId = Shader.PropertyToID("_LipWidth");
    static readonly int CreviceScaleId = Shader.PropertyToID("_CreviceScale");
    static readonly int VeinScaleId = Shader.PropertyToID("_VeinScale");
    static readonly int VeinId = Shader.PropertyToID("_Vein");
    static readonly int CrackId = Shader.PropertyToID("_Crack");
    static readonly int CrackGlowId = Shader.PropertyToID("_CrackGlow");
    static readonly int CrackLengthId = Shader.PropertyToID("_CrackLength");
    static readonly int CloseId = Shader.PropertyToID("_Close");
    static readonly int CloseAxisId = Shader.PropertyToID("_CloseAxis");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int RagId = Shader.PropertyToID("_Rag");
    static readonly int RagCountId = Shader.PropertyToID("_RagCount");
    static readonly int ErodeId = Shader.PropertyToID("_Erode");
    static readonly int GrainId = Shader.PropertyToID("_Grain");
    static readonly int GrainScaleId = Shader.PropertyToID("_GrainScale");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int PhaseId = Shader.PropertyToID("_Phase");
    static readonly int ShadowToneId = Shader.PropertyToID("_ShadowTone");
    static readonly int LitToneId = Shader.PropertyToID("_LitTone");
    static readonly int SinkId = Shader.PropertyToID("_Sink");
    static readonly int FacetsId = Shader.PropertyToID("_Facets");
    static readonly int LightDirId = Shader.PropertyToID("_LightDir");

    // Linear rgb, and every one of them measured through the shipped grade rather than guessed:
    // contrast +14 crushes the shadows hard, so these land at luminance 6 down the throat, 44 on
    // the rubble the light catches, 11 on the wall rock and 48 along the broken lip, against a deck
    // at 182. The gap between the throat and the rubble is the point: a nine-pixel window anywhere
    // in the dark half has to find both of them in it, or the interior measures flat however good
    // the silhouette is.
    static readonly Vector4 HoleFloor = new(0.0062f, 0.0082f, 0.0104f, 1f);
    static readonly Vector4 HoleRubble = new(0.0195f, 0.0375f, 0.053f, 1f);
    static readonly Vector4 HoleWall = new(0.0062f, 0.0105f, 0.0148f, 1f);
    static readonly Vector4 HoleLip = new(0.029f, 0.0385f, 0.0455f, 1f);
    static readonly Vector4 ShardShadow = new(0.0075f, 0.0105f, 0.013f, 1f);
    static readonly Vector4 ShardLit = new(0.03f, 0.036f, 0.042f, 1f);

    // The light in the hole, in three steps. The wall colour carries the area: blue clips, green
    // very nearly does and red sits at 0.46, which is what lifts a saturated cyan off its own
    // luminance ceiling of 201 and up to 212 while it is still at saturation 0.65. The throat
    // colour is the dim spill left in the floor, and the glint is the one small place all three
    // channels go — a couple of hundred pixels of white inside a hundred times as many that are
    // bright and still coloured.
    static readonly Vector4 VoidWall = new(0.46f, 3.8f, 4.95f, 1f);
    static readonly Vector4 VoidThroat = new(0.02f, 0.135f, 0.95f, 1f);
    static readonly Vector4 VoidGlint = new(3.2f, 4.6f, 4.4f, 1f);

    // What the hole throws up onto the pieces above it, and only onto the edge of them that is
    // turned down into it. It is bounce, and it has to leave the plates reading as silhouettes
    // against the shaft rather than as pale chips floating in it.
    static readonly Vector4 ShardBounce = new(0.02f, 0.62f, 1.3f, 1f);

    // The pit itself, in rim radii and fractions of the rim. A cylindrical pit seen at 73 degrees
    // draws its own floor as the rim ellipse slid down the screen by its depth; 0.88 is deep
    // enough that the far inner wall is a tall crescent rather than a hairline, which is where all
    // of the bright saturated colour lives.
    const float ThroatDepth = 0.88f;
    const float WallSoftness = 0.09f;
    const float CrestStart = 0.3f;
    const float NearLipWidth = 0.16f;

    // Relief pitch, in uv. At this camera one uv unit is 635 screen pixels, so 168 puts a lump
    // every 22 pixels and the finest octave inside the nine-pixel window the interior is read in.
    const float ReliefScale = 168f;
    const float CreviceScale = 74f;
    const float VeinScale = 41f;

    // One apparent key direction for every piece, so the plates read as one material under one sky
    // rather than as unrelated sprites.
    static readonly Vector2 KeyLight = new(0.48f, 0.86f);

    static Mesh sharedQuad;

    sealed class Shard
    {
        public Transform Transform;
        public Material Material;
        public Vector3 From;
        public Vector3 To;
        public float Width;
        public float Delay;
        public float FallSeconds;
        public float Roll;
        public float RollRate;
        public bool Retired;
    }

    Transform cameraTransform;
    Material holeMaterial;
    Shard[] shards;

    float cell = 2.7f;
    float holeDiameter;
    float holeQuadSize;
    float holePhase;
    float closeAngle;
    float elapsed;
    float lifetime;

    public void Build()
    {
        Camera boardCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (boardCamera == null)
            boardCamera = Camera.main;
        cameraTransform = boardCamera != null ? boardCamera.transform : null;

        cell = Mathf.Max(0.5f, GameLoop.cellSize);

        BuildHole();
        BuildShards();

        lifetime = holeMaterial != null ? LifeSeconds : 0f;
        if (shards != null)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                Shard shard = shards[i];
                if (shard == null)
                    continue;
                lifetime = Mathf.Max(
                    lifetime,
                    shard.Delay + shard.FallSeconds + ShardSinkSeconds
                );
            }
        }

        if (lifetime <= 0.01f)
        {
            Destroy(gameObject);
            return;
        }

        // Posed before the first frame is drawn; an unsized quad is a one-metre white square.
        Step(0f);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Step(elapsed);
    }

    void Step(float time)
    {
        StepHole(time);
        StepShards(time);
    }

    void BuildHole()
    {
        MeshRenderer renderer = CreateQuad(
            "Hole",
            Shader.Find("BattlePlan/DeathMark"),
            out holeMaterial
        );
        if (renderer == null)
            return;

        holeDiameter = HoleCells * cell;
        holeQuadSize = holeDiameter * HoleQuadPad;
        holePhase = Random.Range(0f, 20f);

        // Yawed to the viewer rather than randomly. The shader reads +v as "away from the camera
        // along the ground" and shades the far wall against the near overhang from it; that
        // asymmetry is the only thing making a flat card read as a hole rather than a stain.
        Vector3 alongView = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        alongView.y = 0f;
        float yaw =
            alongView.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(alongView.x, alongView.z) * Mathf.Rad2Deg
                : 0f;

        Transform quad = renderer.transform;
        quad.localPosition = Vector3.up * HoleHeight;
        quad.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
        quad.localScale = Vector3.one * holeQuadSize;

        closeAngle = Random.Range(0f, Mathf.PI * 2f);

        holeMaterial.SetVector(FloorColorId, HoleFloor);
        holeMaterial.SetVector(RubbleColorId, HoleRubble);
        holeMaterial.SetVector(WallColorId, HoleWall);
        holeMaterial.SetVector(LipColorId, HoleLip);
        holeMaterial.SetVector(GlowColorId, VoidWall);
        holeMaterial.SetVector(CoreColorId, VoidThroat);
        holeMaterial.SetVector(GlintColorId, VoidGlint);
        holeMaterial.SetFloat(OpacityId, 1f);
        holeMaterial.SetFloat(CrackLengthId, CrackLength);
        holeMaterial.SetFloat(RagId, Random.Range(0.32f, 0.4f));
        holeMaterial.SetFloat(RagCountId, Random.Range(2.6f, 3.6f));
        holeMaterial.SetFloat(ThroatId, ThroatDepth);
        holeMaterial.SetFloat(WallSoftId, WallSoftness);
        holeMaterial.SetFloat(CrestAtId, CrestStart);
        holeMaterial.SetFloat(LipWidthId, NearLipWidth);
        holeMaterial.SetFloat(GrainId, 1f);
        holeMaterial.SetFloat(GrainScaleId, ReliefScale);
        holeMaterial.SetFloat(CreviceScaleId, CreviceScale);
        holeMaterial.SetFloat(VeinScaleId, VeinScale);
        holeMaterial.SetFloat(VeinId, 0.7f);
        holeMaterial.SetFloat(SeedId, Random.Range(0f, 20f));
        holeMaterial.SetFloat(ErodeId, -0.7f);
    }

    void BuildShards()
    {
        Shader shader = Shader.Find("BattlePlan/DeathShard");
        if (shader == null)
            return;

        float holeRadius = holeDiameter * 0.5f;
        float startAngle = Random.Range(0f, Mathf.PI * 2f);
        Shard[] built = new Shard[ShardCount];

        for (int i = 0; i < ShardCount; i++)
        {
            MeshRenderer renderer = CreateQuad("Shard", shader, out Material material);
            if (renderer == null)
                break;

            // Golden-angle stepping with jitter: no axis for the eye to lock onto, and no gap wide
            // enough to read as a missing tooth.
            float angle = startAngle + i * 2.39996f + Random.Range(-0.5f, 0.5f);
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            float width =
                i == 0 ? 0.6f
                : i < 3 ? Random.Range(0.28f, 0.36f)
                : Random.Range(0.11f, 0.17f);
            width *= cell;

            // These are plates of the deck itself, so they start on the lip and go inward and
            // down. Outward is the landing's direction and a death may not borrow it. They stay
            // near the rim they broke off: a plate that travels to the middle of the shaft ends up
            // a grey chip lying across the one surface carrying the light.
            float lip = Random.Range(0.72f, 0.96f) * holeRadius;

            // Spread right across the sink and the hold: the hole is at full width for three
            // captured frames and something has to be different in each of them.
            float delay = i == 0 ? 0f : Random.Range(0.02f, 0.24f);

            Shard shard = new()
            {
                Transform = renderer.transform,
                Material = material,
                From = outward * lip + Vector3.up * Random.Range(0.06f, 0.26f),
                To = outward * (lip * Random.Range(0.62f, 0.86f)) + Vector3.up * (width * 0.12f),
                Width = width,
                Delay = delay,
                FallSeconds = Random.Range(0.14f, 0.26f),
                Roll = Random.Range(0f, 360f),
                RollRate = Random.Range(-300f, 300f),
            };

            material.SetVector(ShadowToneId, ShardShadow);
            material.SetVector(LitToneId, ShardLit);
            material.SetVector(GlowColorId, ShardBounce);
            material.SetFloat(GlowId, 0f);
            material.SetFloat(OpacityId, 1f);
            material.SetFloat(RadiusId, 0.8f);
            material.SetFloat(RagId, Random.Range(0.3f, 0.46f));
            material.SetFloat(RagCountId, Random.Range(2.4f, 3.8f));
            material.SetFloat(FacetsId, Random.Range(0.42f, 0.68f));
            material.SetFloat(SeedId, Random.Range(0f, 20f));
            material.SetFloat(SinkId, -0.05f);

            shard.Transform.localPosition = shard.From;
            shard.Transform.localScale = Vector3.zero;
            built[i] = shard;
        }

        shards = built;
    }

    void StepHole(float time)
    {
        if (holeMaterial == null)
            return;

        float diameter;
        float close = 0f;
        float erode;

        if (time < OpenSeconds)
        {
            // Torn open under the body over five captured frames. Close to linear in radius, which
            // means the area roughly quadruples between one frame and the next — the whole reason
            // this used to read as a decal is that it arrived complete.
            diameter = holeDiameter * Mathf.Pow(time / OpenSeconds, 0.85f);
            erode = -0.7f;
        }
        else if (time < PeakSeconds)
        {
            diameter =
                holeDiameter
                * Mathf.Lerp(1f, HoldShrink, Mathf.InverseLerp(OpenSeconds, PeakSeconds, time));
            erode = -0.7f;
        }
        else
        {
            float closing = Mathf.Clamp01((time - PeakSeconds) / CloseSeconds);
            diameter = holeDiameter * HoldShrink;
            // One flank of the deck slides back across the hole ahead of the other, and which
            // flank that is walks around the rim as it goes, while what is left has pieces taken
            // out of it. Outline, area and centroid are all different in every frame instead of
            // shrinking as one shape.
            close = CloseGain * Mathf.Pow(closing, 0.85f);
            erode = Mathf.Lerp(-0.7f, 1.25f, Mathf.Pow(closing, 0.72f));
        }

        float turn = closeAngle + close * CloseSpin;
        holeMaterial.SetVector(
            CloseAxisId,
            new Vector4(Mathf.Cos(turn), Mathf.Sin(turn), 0f, 0f)
        );
        holeMaterial.SetFloat(RadiusId, Mathf.Clamp(diameter / holeQuadSize, 0f, HoleRadiusCap));
        holeMaterial.SetFloat(CloseId, close);
        holeMaterial.SetFloat(ErodeId, erode);
        // The torn boundary is never the same shape twice, and never for two frames running.
        holeMaterial.SetFloat(PhaseId, holePhase + time * PhaseRate);
        holeMaterial.SetFloat(GlowId, GlowAt(time));
        holeMaterial.SetFloat(FillId, FillAt(time));
        holeMaterial.SetFloat(CrackId, CrackAt(time));
        holeMaterial.SetFloat(CrackGlowId, CrackGlowAt(time));
    }

    void StepShards(float time)
    {
        if (shards == null)
            return;

        Quaternion facing =
            cameraTransform != null ? cameraTransform.rotation : Quaternion.Euler(73f, 0f, 0f);
        Vector3 forward = facing * Vector3.forward;
        float bounce = GlowAt(time);

        for (int i = 0; i < shards.Length; i++)
        {
            Shard shard = shards[i];
            if (shard == null || shard.Retired || shard.Transform == null || shard.Material == null)
                continue;

            float age = time - shard.Delay;
            if (age <= 0f)
            {
                shard.Transform.localScale = Vector3.zero;
                continue;
            }

            float sinking = Mathf.Clamp01((age - shard.FallSeconds) / ShardSinkSeconds);
            if (sinking >= 1f)
            {
                shard.Retired = true;
                shard.Transform.gameObject.SetActive(false);
                continue;
            }

            // A plate tipping off a lip drops rather than glides: the inward travel eases out
            // while the fall accelerates, so it is steepest as it goes under.
            float fall = Mathf.Clamp01(age / shard.FallSeconds);
            Vector3 position = Vector3.Lerp(shard.From, shard.To, 1f - Mathf.Pow(1f - fall, 2f));
            position.y = Mathf.Lerp(shard.From.y, shard.To.y, fall * fall);
            position.y -= sinking * shard.Width * 0.9f;

            float roll = shard.Roll + shard.RollRate * Mathf.Min(age, shard.FallSeconds + 0.06f);

            shard.Transform.localPosition = position;
            shard.Transform.localRotation = Quaternion.AngleAxis(roll, forward) * facing;
            shard.Transform.localScale =
                Vector3.one * (shard.Width * Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(age / 0.045f)));

            shard.Material.SetFloat(SinkId, Mathf.Lerp(-0.05f, 1.1f, sinking));
            shard.Material.SetFloat(GlowId, bounce);
            // Counter-rolled, so a tumbling piece does not take the sky with it.
            shard.Material.SetVector(LightDirId, RotatedKeyLight(roll));
        }
    }

    /// <summary>
    /// The light in the hole. It arrives with the tear rather than after it — the deck splits and
    /// the shaft is lit two frames later — holds through the frames the body is half-submerged,
    /// then goes out with the close. Light, opaque material and saturated colour all peak on the
    /// same frames, which is the one thing an impact cannot get wrong.
    /// </summary>
    static float GlowAt(float time)
    {
        if (time <= 0.02f)
            return 0f;
        if (time < LightSeconds)
            return Mathf.Pow(Mathf.InverseLerp(0.02f, LightSeconds, time), 0.65f);
        if (time < PeakSeconds)
            return Mathf.Lerp(1f, HoldGlow, Mathf.InverseLerp(LightSeconds, PeakSeconds, time));

        float leaving = Mathf.InverseLerp(PeakSeconds, PeakSeconds + CloseSeconds * 0.72f, time);
        return Mathf.Lerp(HoldGlow, 0f, Mathf.Pow(leaving, 0.85f));
    }

    /// <summary>
    /// How full of light the throat still is. The black mass is what erases the corpse, and while
    /// the corpse is the whole point of the panel the black has to wait: the shaft is brim-full
    /// for the first three captured frames, drains over the next three, and is a void by the frame
    /// the strip cuts. That is worth five frames of a visibly crushing body against a lit floor
    /// instead of two, and it costs the money frame nothing, because by then the fill is gone.
    /// </summary>
    static float FillAt(float time)
    {
        if (time < DrainStart)
            return 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DrainStart, DrainEnd, time));
    }

    /// <summary>
    /// How far the fissures have run. They are the first thing on the board — the deck fails a
    /// frame before it opens — and the deck knits back over them as it closes, each one at its own
    /// rate, so the footprint loses its outline in pieces.
    /// </summary>
    static float CrackAt(float time)
    {
        if (time < 0.07f)
            return Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0f, 0.07f, time));
        if (time < PeakSeconds)
            return 1f;
        return 1f - Mathf.InverseLerp(PeakSeconds, PeakSeconds + CloseSeconds, time);
    }

    /// <summary>
    /// The fissures flash as they tear, drop back while the hole is still opening so the opening
    /// owns the frame, then come up with the pool. Colour has to hold area at the peak, and five
    /// lit fissures running out across the deck are what carries it past the hole's own edge.
    /// </summary>
    static float CrackGlowAt(float time)
    {
        if (time < 0.05f)
            return 0.7f;
        if (time < 0.11f)
            return Mathf.Lerp(0.7f, 0.35f, Mathf.InverseLerp(0.05f, 0.11f, time));
        if (time < OpenSeconds)
            return Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(0.11f, OpenSeconds, time));
        if (time < PeakSeconds)
            return 1f;
        return Mathf.Lerp(1f, 0f, Mathf.InverseLerp(PeakSeconds, PeakSeconds + CloseSeconds * 0.8f, time));
    }

    /// <summary>
    /// The key direction expressed in the card's own uv frame, so rolling a shard to break the
    /// stamped-sprite read does not also roll where the light is coming from.
    /// </summary>
    static Vector4 RotatedKeyLight(float rollDegrees)
    {
        float radians = -rollDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector4(
            KeyLight.x * cos - KeyLight.y * sin,
            KeyLight.x * sin + KeyLight.y * cos,
            0f,
            0f
        );
    }

    MeshRenderer CreateQuad(string quadName, Shader shader, out Material material)
    {
        material = null;
        Mesh mesh = SharedQuad();
        if (shader == null || mesh == null)
        {
            Debug.LogWarning($"[DeathCollapse] missing shader or quad mesh for '{quadName}'");
            return null;
        }

        GameObject quad = new(quadName);
        quad.transform.SetParent(transform, false);
        quad.AddComponent<MeshFilter>().sharedMesh = mesh;

        material = new Material(shader);

        MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return renderer;
    }

    /// <summary>
    /// Built by hand rather than borrowed from a primitive, so a death never creates a collider
    /// that could answer a line-of-sight raycast on its way to being destroyed. Unity may unload it
    /// between scenes; the null check rebuilds it.
    /// </summary>
    static Mesh SharedQuad()
    {
        if (sharedQuad != null)
            return sharedQuad;

        sharedQuad = new Mesh { name = "DeathCollapseQuad" };
        sharedQuad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
        };
        sharedQuad.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        sharedQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        // Flat bounds make billboards pop in and out of the frustum as they roll.
        sharedQuad.bounds = new Bounds(Vector3.zero, Vector3.one);
        return sharedQuad;
    }

    void OnDestroy()
    {
        if (holeMaterial != null)
            Destroy(holeMaterial);
        holeMaterial = null;

        if (shards == null)
            return;
        for (int i = 0; i < shards.Length; i++)
        {
            if (shards[i] != null && shards[i].Material != null)
                Destroy(shards[i].Material);
        }
        shards = null;
    }
}
