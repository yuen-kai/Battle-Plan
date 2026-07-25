using System.Collections;
using UnityEngine;

/// <summary>
/// Shield Rush's hold and deflection beats (ArtDirection §9.3). Attached to the shield transform
/// the first time the replicated shield state is applied, so it comes up on every peer including
/// one that only learns about the shield after a fog reveal.
///
/// The shield face is NOT scaled. §9.3 asks for a 0.2 → 1.0 Y scale on it, but the face carries
/// the BoxCollider that Shield.TryExpandShieldFootprint sizes to the blocking width, and scaling
/// that for a quarter of a second would move a hit window. The rise is delivered by the rim quad
/// scaling and the face's alpha instead; the collider is never touched.
/// </summary>
public sealed class ShieldFX : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // === CONSTANTS (§9.3, Shield Rush) ===
    public const float RaiseSeconds = 0.25f;
    public const float LowerSeconds = 0.24f;
    public const float HoldAlpha = 0.2f;
    public const float DeflectAlpha = 0.55f;
    public const float DeflectRiseSeconds = 0.06f;
    public const float DeflectFallSeconds = 0.14f;
    public const float RimRingWidth = 0.1f;
    public const float RimIntensity = 2.5f;
    public const float RimStartScaleY = 0.2f;
    public const float DeflectShockwaveRadius = 0.28f;
    public const float DeflectShockwaveDuration = 0.16f;

    /// <summary>Overshoot of --bp-ease-snap, the curve §9.3 asks the face to rise on.</summary>
    private const float SnapOvershoot = 1.12f;

    private const string RimChildName = "ShieldRim";

    private Renderer faceRenderer;
    private Renderer rimRenderer;
    private Transform rim;
    private MaterialPropertyBlock faceProperties;
    private MaterialPropertyBlock rimProperties;
    private Coroutine stateRoutine;
    private Coroutine deflectRoutine;
    private Color teamGlow = Color.white;
    private Color teamBase = Color.white;
    private float currentAlpha;

    /// <summary>
    /// Applies the raise or lower sequence. Called from the shield's replicated state change, so
    /// it is already running on every peer and needs no RPC of its own.
    /// </summary>
    public static void SetActive(Transform shield, int teamIndex, bool active)
    {
        if (shield == null)
            return;

        ShieldFX fx = shield.GetComponent<ShieldFX>();
        if (fx == null)
            fx = shield.gameObject.AddComponent<ShieldFX>();
        fx.Apply(teamIndex, active);
    }

    /// <summary>A round stopping on the shield: sparks at the contact point and a flare-up.</summary>
    public static void Deflect(Transform shield, Vector3 hitPoint, int shooterTeamIndex)
    {
        ParticleBurstFX.Sparks(
            hitPoint,
            FXPalette.TeamSrgb(shooterTeamIndex),
            ParticleBurstFX.ShieldBlockSparks
        );

        ShieldFX fx = shield != null ? shield.GetComponent<ShieldFX>() : null;
        if (fx == null)
            return;

        ImpactShockwave.Spawn(
            hitPoint,
            fx.teamGlow,
            DeflectShockwaveRadius,
            DeflectShockwaveDuration,
            withLightPop: false
        );
        if (fx.deflectRoutine != null)
            fx.StopCoroutine(fx.deflectRoutine);
        fx.deflectRoutine = fx.StartCoroutine(fx.FlareUp());
    }

    private void Apply(int teamIndex, bool active)
    {
        EnsureParts(teamIndex);

        if (stateRoutine != null)
            StopCoroutine(stateRoutine);
        stateRoutine = StartCoroutine(active ? Raise() : Lower());
    }

    private void EnsureParts(int teamIndex)
    {
        teamGlow = FXPalette.TeamGlow(teamIndex);
        teamBase = FXPalette.TeamSrgb(teamIndex);

        if (faceRenderer == null)
        {
            faceRenderer = GetComponent<Renderer>();
            faceProperties = new MaterialPropertyBlock();
        }

        if (rim != null)
            return;

        Transform existing = transform.Find(RimChildName);
        if (existing != null)
        {
            rim = existing;
        }
        else
        {
            GameObject spawned = FXAssets.Spawn(
                FXAssets.GroundGlow,
                transform.position,
                transform.rotation
            );
            if (spawned == null)
                return;

            // The builder's telegraph component drives its own lifetime; the rim is held by the
            // shield instead, so it must not come along.
            GroundTelegraph telegraph = spawned.GetComponent<GroundTelegraph>();
            if (telegraph != null)
                Destroy(telegraph);

            spawned.name = RimChildName;
            rim = spawned.transform;
            rim.SetParent(transform, worldPositionStays: false);
            rim.localPosition = Vector3.zero;
            rim.localRotation = Quaternion.identity;
            // Matched to the shield face in the face's own local space, nudged clear of it so the
            // two coplanar surfaces cannot z-fight.
            rim.localScale = Vector3.one;
            rim.localPosition = new Vector3(0f, 0f, -0.01f);
        }

        rimRenderer = rim.GetComponent<Renderer>();
        rimProperties = new MaterialPropertyBlock();
        PushRim(0f, RimIntensity);
    }

    private IEnumerator Raise()
    {
        float elapsed = 0f;
        while (elapsed < RaiseSeconds)
        {
            float progress = Mathf.Clamp01(elapsed / RaiseSeconds);
            SetFaceAlpha(Mathf.Lerp(0f, HoldAlpha, progress));
            PushRim(Snap(progress), RimIntensity);
            elapsed += Time.deltaTime;
            yield return null;
        }
        SetFaceAlpha(HoldAlpha);
        PushRim(1f, RimIntensity);

        // Impact frame: the shield is up and the deck knows about it.
        ImpactShockwave.Spawn(
            transform.position,
            teamGlow,
            AbilityFX.ShieldRaiseShockwaveRadius,
            AbilityFX.ShieldRaiseShockwaveDuration
        );
        stateRoutine = null;
    }

    private IEnumerator Lower()
    {
        float startAlpha = currentAlpha;
        float elapsed = 0f;
        while (elapsed < LowerSeconds)
        {
            float progress = Mathf.Clamp01(elapsed / LowerSeconds);
            SetFaceAlpha(Mathf.Lerp(startAlpha, 0f, progress));
            PushRim(1f - progress, RimIntensity * (1f - progress));
            elapsed += Time.deltaTime;
            yield return null;
        }
        SetFaceAlpha(0f);
        PushRim(0f, 0f);
        stateRoutine = null;
    }

    private IEnumerator FlareUp()
    {
        float elapsed = 0f;
        while (elapsed < DeflectRiseSeconds)
        {
            SetFaceAlpha(Mathf.Lerp(HoldAlpha, DeflectAlpha, elapsed / DeflectRiseSeconds));
            elapsed += Time.deltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < DeflectFallSeconds)
        {
            SetFaceAlpha(Mathf.Lerp(DeflectAlpha, HoldAlpha, elapsed / DeflectFallSeconds));
            elapsed += Time.deltaTime;
            yield return null;
        }
        SetFaceAlpha(HoldAlpha);
        deflectRoutine = null;
    }

    /// <summary>--bp-ease-snap: overshoots then settles, so the shield lands rather than grows.</summary>
    private static float Snap(float progress)
    {
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float overshoot = Mathf.Sin(progress * Mathf.PI) * (SnapOvershoot - 1f);
        return Mathf.Lerp(RimStartScaleY, 1f, eased) + overshoot;
    }

    private void SetFaceAlpha(float alpha)
    {
        currentAlpha = alpha;
        if (faceRenderer == null)
            return;

        faceRenderer.GetPropertyBlock(faceProperties);
        faceProperties.SetColor(
            BaseColorId,
            new Color(teamBase.r, teamBase.g, teamBase.b, alpha)
        );
        faceRenderer.SetPropertyBlock(faceProperties);
    }

    private void PushRim(float scaleY, float intensity)
    {
        if (rim == null || rimRenderer == null)
            return;

        Vector3 scale = rim.localScale;
        rim.localScale = new Vector3(scale.x, Mathf.Max(0.001f, scaleY), scale.z);

        rimRenderer.GetPropertyBlock(rimProperties);
        rimProperties.SetColor(GlowColorId, teamGlow);
        rimProperties.SetFloat(RingWidthId, RimRingWidth);
        rimProperties.SetFloat(IntensityId, intensity);
        rimRenderer.SetPropertyBlock(rimProperties);
    }
}
