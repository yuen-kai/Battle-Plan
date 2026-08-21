using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A unit's ability clock, cut into the base plate it is already standing on.
///
/// <para>The cooldown was previously legible in exactly one place: a number on that unit's HUD
/// card. A player looking at the board to decide where to move has to look away from the board,
/// find the right card among five, read a digit and look back — for information that belongs to
/// a unit they are already looking at. The plate is the only surface that is on-screen exactly
/// when its unit is, so the clock lives there: one slot per round of the ability's cooldown,
/// filling clockwise, unbroken and breathing when the ability is available.</para>
///
/// <para>Built at runtime rather than authored on <c>Unit.prefab</c>, matching
/// <see cref="SpeedBoostIndicatorVisual"/> — every unit gets it without five prefab variants
/// having to agree about a child object, and it costs nothing on a unit with no ability.</para>
/// </summary>
public sealed class AbilityStatusRing : MonoBehaviour
{
    public const string GameObjectName = "AbilityStatusRing";
    public const string ShaderName = "BattlePlan/AbilityRing";

    // Sized against the plate the dial is cut into. The team puck is 1.755 across and its dark rim
    // 2.015, so a band centred at 0.8 sits on the puck's outer face with plate colour still reading
    // inside it and the rim still reading outside. It also stays clear of Shield Rush's 2.15 ring,
    // which is the only other thing that ever draws at this radius.
    private const float QuadSize = 2f;
    private const float Radius = 0.8f;
    private const float Thickness = 0.15f;
    private const float SlotGap = 0.085f;

    // Just proud of the plate: the dial darkens the puck it is engraved into, so it has to win the
    // depth test against that puck without visibly floating above it.
    private const float GroundOffset = 0.035f;

    // The floor the dial may never sink below, whatever plate its unit is standing on.
    //
    // A move or ability range paves the cells it covers with MoveOverlayCell, whose plane is opaque
    // and writes depth at y 0.2, with a translucent highlight over it at 0.227. Every unit's base
    // plate tops out under that — 0.156, and only 0.1 on the Ramrod — so a dial that sits on
    // the plate is depth-rejected outright the moment a range is shown, which is the one moment a
    // player is deciding what to spend the round on. Above both, the dial survives; the difference
    // from the plate is under a tenth of a unit and does not read as float at the board's camera.
    private const float RangeOverlayClearance = 0.245f;

    private const float RechargeSlotsPerSecond = 1.6f;
    private const float ReadyBlendPerSecond = 2.5f;
    private const float FlashDecayPerSecond = 1.7f;
    private const float Settled = 0.001f;

    private static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

    // Pushed past one so a charged slot glows rather than merely being painted, but only just: the
    // slot replaces the plate underneath it rather than adding to it, so the amber does not have to
    // out-shout an emissive ultramarine puck to stay amber. The head opens the top of the same
    // ramp — the leading edge of a filling slot is the hottest thing on the plate while it moves.
    //
    // Converted rather than assigned. Material.SetColor hands the raw floats to the shader, and the
    // palette states display sRGB, so passing the token straight through lifts the green channel by
    // half and the ability amber comes out as a flat yellow that belongs to nothing else on screen.
    private static readonly Color ChargeColor = (
        TeamPalette.AbilityBright.linear * 1.5f
    ).WithAlpha(1f);
    private static readonly Color HeadColor = (
        Color.Lerp(TeamPalette.AbilityBright, Color.white, 0.5f).linear * 2.4f
    ).WithAlpha(1f);

    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int SlotsId = Shader.PropertyToID("_Slots");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int GapId = Shader.PropertyToID("_Gap");
    private static readonly int ChargeColorId = Shader.PropertyToID("_ChargeColor");
    private static readonly int HeadColorId = Shader.PropertyToID("_HeadColor");
    private static readonly int ReadyId = Shader.PropertyToID("_Ready");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");

    private Material dialMaterial;
    private Mesh dialMesh;
    private Transform dial;
    private Renderer dialRenderer;

    private int slots = 1;
    private float targetCharge;
    private float displayedCharge;
    private float readyBlend;
    private float flash;

    /// <summary>Rounds of cooldown the dial is divided into.</summary>
    public int Slots => slots;

    /// <summary>Slots currently lit, mid-animation included.</summary>
    public float DisplayedCharge => displayedCharge;

    /// <summary>Whether the dial has settled into its unbroken, available state.</summary>
    public bool IsReady => readyBlend >= 1f - Settled;

    /// <summary>
    /// Slots a unit's dial should be showing. Cooldown counts down to zero as the ability comes
    /// back, and the dial counts up as it fills, so the two are complements rather than the same
    /// number: an ability three rounds out on a four-round cooldown shows one slot, not three.
    /// </summary>
    public static int ChargedSlots(int remainingRounds, int configuredRounds)
    {
        int total = Mathf.Max(0, configuredRounds);
        return total - Mathf.Clamp(remainingRounds, 0, total);
    }

    /// <summary>
    /// Attaches the dial to <paramref name="unitTransform"/>, or returns the one already on it.
    /// Returns null when the shader is missing, which costs the unit its dial and nothing else.
    /// </summary>
    public static AbilityStatusRing Create(Transform unitTransform)
    {
        if (unitTransform == null)
            return null;

        Transform existing = unitTransform.Find(GameObjectName);
        if (existing != null && existing.TryGetComponent(out AbilityStatusRing existingRing))
            return existingRing;

        Shader dialShader = Shader.Find(ShaderName);
        if (dialShader == null)
        {
            Debug.LogWarning(
                $"[AbilityStatusRing] {ShaderName} shader not found; ability charge dial disabled."
            );
            return null;
        }

        // Host fog cannot NetworkHide server-owned objects, so GameLoop suppresses their existing
        // renderers locally. Inherit that state at construction: a dial built for a unit that is
        // currently hidden would otherwise draw an ability clock for an enemy nobody can see.
        bool forceRenderingOff = false;
        foreach (Renderer unitRenderer in unitTransform.GetComponentsInChildren<Renderer>(true))
        {
            if (unitRenderer.forceRenderingOff)
            {
                forceRenderingOff = true;
                break;
            }
        }

        GameObject ringObject = new(GameObjectName) { layer = unitTransform.gameObject.layer };
        ringObject.transform.SetParent(unitTransform, worldPositionStays: false);

        AbilityStatusRing ring = ringObject.AddComponent<AbilityStatusRing>();
        ring.Build(unitTransform, dialShader);
        ring.SetForceRenderingOff(forceRenderingOff);
        return ring;
    }

    /// <summary>
    /// Points the dial at a unit's cooldown. <paramref name="animate"/> is false for state that
    /// was already true before this client was looking — a spawn, or a unit coming back out of
    /// fog — where sweeping up from empty would report a recharge that already happened.
    /// </summary>
    public void SetCharge(int remainingRounds, int configuredRounds, bool animate)
    {
        slots = Mathf.Max(1, configuredRounds);
        targetCharge = ChargedSlots(remainingRounds, configuredRounds);

        if (!animate)
        {
            displayedCharge = targetCharge;
            readyBlend = targetCharge >= slots ? 1f : 0f;
            flash = 0f;
        }

        Push();
    }

    public void SetForceRenderingOff(bool forceRenderingOff)
    {
        if (dialRenderer != null)
            dialRenderer.forceRenderingOff = forceRenderingOff;
    }

    private void LateUpdate()
    {
        // The dial is counted, so it stays locked to the board while the unit under it turns.
        if (dial != null)
            dial.rotation = FlatRotation;

        float step = Time.deltaTime;
        bool wasFull = displayedCharge >= slots - Settled;

        // Spending is instant and recharging is not. A dial that swept back down would spend two
        // seconds animating the one transition the player already watched themselves order.
        displayedCharge =
            targetCharge < displayedCharge
                ? targetCharge
                : Mathf.MoveTowards(displayedCharge, targetCharge, RechargeSlotsPerSecond * step);

        bool full = displayedCharge >= slots - Settled;
        if (full && !wasFull)
            flash = 1f;

        readyBlend = Mathf.MoveTowards(readyBlend, full ? 1f : 0f, ReadyBlendPerSecond * step);
        flash = Mathf.MoveTowards(flash, 0f, FlashDecayPerSecond * step);
        Push();
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(dialMaterial);
        DestroyRuntimeObject(dialMesh);
    }

    private void Build(Transform unitTransform, Shader dialShader)
    {
        PlaceOnBasePlate(unitTransform);

        dialMesh = CreateQuadMesh();
        dialMaterial = new Material(dialShader)
        {
            name = "Ability Charge Dial (Runtime)",
            hideFlags = HideFlags.DontSave,
        };
        dialMaterial.SetFloat(QuadSizeId, QuadSize);
        dialMaterial.SetFloat(RadiusId, Radius);
        dialMaterial.SetFloat(ThicknessId, Thickness);
        dialMaterial.SetFloat(GapId, SlotGap);
        dialMaterial.SetColor(ChargeColorId, ChargeColor);
        dialMaterial.SetColor(HeadColorId, HeadColor);

        GameObject dialObject = new("Dial") { layer = gameObject.layer };
        dialObject.transform.SetParent(transform, worldPositionStays: false);
        dialObject.transform.localScale = new Vector3(QuadSize, QuadSize, 1f);
        dial = dialObject.transform;
        dial.rotation = FlatRotation;

        MeshFilter meshFilter = dialObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = dialMesh;
        dialRenderer = dialObject.AddComponent<MeshRenderer>();
        dialRenderer.sharedMaterial = dialMaterial;
        dialRenderer.shadowCastingMode = ShadowCastingMode.Off;
        dialRenderer.receiveShadows = false;
        dialRenderer.lightProbeUsage = LightProbeUsage.Off;
        dialRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Push();
    }

    /// <summary>
    /// Cancels the unit's own scale so the dial is a fixed world size on every prefab variant, and
    /// lifts it clear of both the base plate and the range overlay. A unit does not stand on the
    /// board, it stands on its own puck, so a quad laid on the board plane is buried by the very
    /// plate this is engraving; and the plate in turn is paved over whenever a range is shown.
    /// The dial takes whichever of the two is higher, so it is engraved when it can be and legible
    /// when it cannot.
    /// </summary>
    private void PlaceOnBasePlate(Transform unitTransform)
    {
        Vector3 unitScale = unitTransform.lossyScale;
        float scaleX = Mathf.Max(Mathf.Abs(unitScale.x), 0.001f);
        float scaleY = Mathf.Max(Mathf.Abs(unitScale.y), 0.001f);
        float scaleZ = Mathf.Max(Mathf.Abs(unitScale.z), 0.001f);

        float abovePlate = UnitBasePlate.ClearanceAboveOrigin(unitTransform, GroundOffset);
        float aboveOverlay = UnitBasePlate.BoardClearanceAboveOrigin(
            unitTransform,
            RangeOverlayClearance
        );

        transform.localPosition = new Vector3(
            0f,
            Mathf.Max(abovePlate, aboveOverlay) / scaleY,
            0f
        );
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(1f / scaleX, 1f / scaleY, 1f / scaleZ);
    }

    private void Push()
    {
        if (dialMaterial == null)
            return;

        dialMaterial.SetFloat(SlotsId, slots);
        dialMaterial.SetFloat(ChargeId, displayedCharge);
        dialMaterial.SetFloat(ReadyId, readyBlend);
        dialMaterial.SetFloat(FlashId, flash);
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new()
        {
            name = "Ability Charge Dial Quad (Runtime)",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeObject);
        else
            DestroyImmediate(runtimeObject);
    }
}
