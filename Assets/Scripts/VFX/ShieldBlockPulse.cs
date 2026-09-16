using UnityEngine;

/// <summary>
/// What a shield does when something is stopped by it: a wave leaves the point of contact and
/// travels outward across the slab, firing the barrier's lattice ring by ring as it goes.
///
/// It used to be a flat tint over the whole slab, which said that a block had happened but not
/// where — and where is the only part a player can act on. A wave that starts at the contact point
/// tells them which side of the shield is under fire, and it tells the shooter that this particular
/// shot was the one that landed, which a shield-wide flash cannot do when two units are firing.
///
/// Drawn as an additive overlay on a copy of the slab's own mesh rather than by tinting the shield's
/// material, so the barrier keeps its own shading and nothing has to be restored afterwards. The
/// shield material writes no depth, so the overlay sits on it without offset or z-fighting.
///
/// Three waves run at once and the oldest slot is taken next, because a Sentinel under fire or
/// caught in ArcSurge's fan is hit several times inside one wave's third of a second.
/// </summary>
public sealed class ShieldBlockPulse : MonoBehaviour
{
    public const string ShieldChildName = "Shield";

    private const string OverlayName = "ShieldBlockWave";
    private const int Slots = 3;

    // The crest crosses the whole slab over this beat, and the charge behind it is gone inside
    // another quarter second. A raised slab is three and a half cells wide, so the crossing is
    // deliberately unhurried: any faster and the crest is off the far end before the eye has found
    // it, leaving the rest of the wave's life as a dim wash.
    private const float TravelSeconds = 1.3f;
    private const float FadeSeconds = 0.26f;

    // Barely any. The crest is the readable part of the effect, so it is worth keeping on the slab
    // for as long as the wave lasts rather than throwing it past the corners early.
    private const float ReachOvershoot = 1.05f;

    // One event, one wave. An explosion reports itself through every layer of its own burst and
    // again over the wire if it blocked damage, and each of those arrives on the same frame at very
    // nearly the same spot on the slab. Without this they stack into one wave three times as bright
    // and burn out the lattice that makes the hit readable.
    private const float MergeSeconds = 0.06f;
    private const float MergeMetres = 0.9f;

    private static readonly Color FallbackHue = new(0.34f, 0.76f, 0.94f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int PulseColorId = Shader.PropertyToID("_PulseColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int FaceScaleId = Shader.PropertyToID("_FaceScale");
    private static readonly int CellId = Shader.PropertyToID("_Cell");
    private static readonly int BandId = Shader.PropertyToID("_Band");
    private static readonly int WakeId = Shader.PropertyToID("_Wake");
    private static readonly int BloomReachId = Shader.PropertyToID("_BloomReach");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int[] PulseIds =
    {
        Shader.PropertyToID("_Pulse0"),
        Shader.PropertyToID("_Pulse1"),
        Shader.PropertyToID("_Pulse2"),
    };

    private readonly Vector2[] contacts = new Vector2[Slots];
    private readonly float[] startedAt = new float[Slots];

    private MeshRenderer overlayRenderer;
    private Material overlayMaterial;
    private Vector2 faceScale;
    private float reach;

    public static void Play(GameObject shieldOwner, Vector3 impactPoint)
    {
        Transform shield = ResolveRaisedShield(shieldOwner);
        if (shield == null)
            return;

        if (!shield.TryGetComponent(out ShieldBlockPulse blockPulse))
            blockPulse = shield.gameObject.AddComponent<ShieldBlockPulse>();
        blockPulse.Begin(impactPoint);
    }

    public static Transform ResolveRaisedShield(GameObject shieldOwner)
    {
        if (shieldOwner == null)
            return null;

        Transform shield = shieldOwner.transform.Find(ShieldChildName);
        return shield != null && shield.gameObject.activeInHierarchy ? shield : null;
    }

    private void Awake()
    {
        for (int slot = 0; slot < Slots; slot++)
            startedAt[slot] = float.NegativeInfinity;
    }

    private void Begin(Vector3 impactPoint)
    {
        if (!isActiveAndEnabled || !EnsureOverlay())
            return;

        Vector2 contact = ToFace(impactPoint);

        // The oldest wave is the one worth losing, unless one of them is already this same hit.
        int oldest = 0;
        for (int slot = 0; slot < Slots; slot++)
        {
            if (
                Time.time - startedAt[slot] <= MergeSeconds
                && (contacts[slot] - contact).sqrMagnitude <= MergeMetres * MergeMetres
            )
            {
                return;
            }
            if (startedAt[slot] < startedAt[oldest])
                oldest = slot;
        }

        contacts[oldest] = contact;
        startedAt[oldest] = Time.time;

        overlayRenderer.enabled = true;
        WriteWaves();
    }

    /// <summary>
    /// A world contact point in metres across the slab's face, measured from its centre. Clamped
    /// onto the slab: a caller that hands over a body's centre rather than a surface point should
    /// still start its wave at the nearest part of the shield, not off the edge of it.
    /// </summary>
    private Vector2 ToFace(Vector3 impactPoint)
    {
        Vector3 local = transform.InverseTransformPoint(impactPoint);
        return new Vector2(
            Mathf.Clamp(local.x, -0.5f, 0.5f) * faceScale.x,
            Mathf.Clamp(local.y, -0.5f, 0.5f) * faceScale.y
        );
    }

    private bool EnsureOverlay()
    {
        Vector3 scale = transform.lossyScale;
        Vector2 face = new(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        if (face.x <= 0.0001f || face.y <= 0.0001f)
            return false;

        if (overlayRenderer != null && faceScale == face)
            return true;

        if (overlayRenderer == null && !BuildOverlay())
            return false;

        faceScale = face;
        reach = Mathf.Sqrt(face.x * face.x + face.y * face.y) * ReachOvershoot;

        overlayMaterial.SetVector(FaceScaleId, new Vector4(face.x, face.y, 0f, 0f));
        // Finer than the face alone would want: the camera only ever sees a shallow ribbon of the
        // slab, and a lattice coarse enough to read square on resolves into two rows of noise there.
        overlayMaterial.SetFloat(CellId, face.y * 0.105f);
        overlayMaterial.SetFloat(BandId, face.y * 0.24f);
        overlayMaterial.SetFloat(BloomReachId, face.y * 0.13f);
        // Measured against the crossing rather than against the slab's height, so the charge behind
        // the crest covers a share of the whole run rather than a fixed distance. Kept to a fifth
        // of it: the slab is three and a half cells long and the camera sees it as a shallow
        // ribbon, so a wake any longer than this lights the whole barrier at once and the crest
        // stops being something the eye can follow along it.
        overlayMaterial.SetFloat(WakeId, reach * 0.2f);
        return true;
    }

    private bool BuildOverlay()
    {
        Shader shader = Shader.Find("BattlePlan/ShieldBlockPulse");
        Mesh mesh = TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (shader == null || mesh == null)
        {
            Debug.LogWarning(
                $"[ShieldBlockPulse] {name} has no slab mesh or the pulse shader is missing; "
                    + "blocks will not show a wave."
            );
            return false;
        }

        overlayMaterial = new Material(shader)
        {
            name = "Shield Block Wave (Runtime)",
            hideFlags = HideFlags.DontSave,
        };

        Color hue = ShieldHue();
        overlayMaterial.SetVector(PulseColorId, (Vector4)hue.linear);
        // Short of white on purpose. A crest driven to white over a slab this size reads as a
        // specular blowout rather than a charge, and the hue is the only thing that says shield.
        overlayMaterial.SetVector(CoreColorId, (Vector4)Color.Lerp(hue, Color.white, 0.42f).linear);
        overlayMaterial.SetFloat(SeedId, Random.Range(0f, 40f));

        GameObject overlay = new(OverlayName);
        overlay.transform.SetParent(transform, worldPositionStays: false);
        overlay.AddComponent<MeshFilter>().sharedMesh = mesh;

        overlayRenderer = overlay.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = overlayMaterial;
        overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        overlayRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        overlayRenderer.enabled = false;
        return true;
    }

    /// <summary>
    /// The barrier's own hue, so a team-tinted shield throws a wave in its own colour instead of a
    /// stock blue one. Taken at full value: the slab's colour is authored at the alpha it is seen
    /// through, and light added over it has no alpha to be seen through.
    /// </summary>
    private Color ShieldHue()
    {
        Material slab = TryGetComponent(out MeshRenderer slabRenderer)
            ? slabRenderer.sharedMaterial
            : null;
        if (slab == null || !slab.HasProperty(BaseColorId))
            return FallbackHue;

        Color authored = slab.GetColor(BaseColorId);
        float peak = Mathf.Max(authored.r, Mathf.Max(authored.g, authored.b));
        return peak <= 0.01f ? FallbackHue : new Color(
            authored.r / peak,
            authored.g / peak,
            authored.b / peak
        );
    }

    private void Update()
    {
        if (overlayRenderer == null || !overlayRenderer.enabled)
            return;

        if (!WriteWaves())
            overlayRenderer.enabled = false;
    }

    /// <summary>Hands every slot's current front and envelope to the shader; false once all are spent.</summary>
    private bool WriteWaves()
    {
        float life = TravelSeconds + FadeSeconds;
        bool anyLive = false;

        for (int slot = 0; slot < Slots; slot++)
        {
            float age = Time.time - startedAt[slot];
            if (age < 0f || age >= life)
            {
                overlayMaterial.SetVector(PulseIds[slot], Vector4.zero);
                continue;
            }

            // Decelerating, which is what reads as a shock passing through a solid rather than a
            // circle being drawn on it — but only gently, or the crest is off the end of a
            // three-and-a-half-cell slab before the player's eye has found it.
            float front = reach * Mathf.Pow(Mathf.Clamp01(age / TravelSeconds), 0.78f);
            float envelope =
                age <= TravelSeconds
                    ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f, (age - TravelSeconds) / FadeSeconds);

            overlayMaterial.SetVector(
                PulseIds[slot],
                new Vector4(contacts[slot].x, contacts[slot].y, front, envelope)
            );
            anyLive = true;
        }

        return anyLive;
    }

    private void OnDisable()
    {
        for (int slot = 0; slot < Slots; slot++)
            startedAt[slot] = float.NegativeInfinity;
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (overlayMaterial != null)
            Destroy(overlayMaterial);
    }
}
