using UnityEngine;

/// <summary>
/// The 0.06 s star at the barrel (ArtDirection §8.5). A four-point star mask on a camera-facing
/// additive quad, plus a point light borrowed from FXLightPool — and the light is genuinely
/// optional: the Shotgunner's ten-pellet burst will exhaust the four-light budget inside one
/// frame, and when it does the quad still draws.
///
/// Fog: the quad follows the projectile's own visibility rule. A bullet is a NetworkObject with
/// observers on both peers and already pierces fog by design ("bullets and telegraphs
/// deliberately pierce fog", §9.5), and the flash sits exactly where the bullet's first frame is,
/// so it discloses nothing the tracer does not. The *light* is gated on local-team cell
/// visibility, because a point light washes across surrounding geometry and would reveal the
/// shape of a room the player has not been shown. That asymmetry is deliberate.
/// </summary>
public sealed class MuzzleFlashFX : MonoBehaviour
{
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

    public const float Lifetime = 0.06f;
    public const float StartSize = 0.55f;
    public const float EndSize = 0.80f;

    public const float LightRange = 3f;
    public const float LightIntensity = 6f;
    public const float LightLifetime = 0.05f;

    /// <summary>How far the white-hot core is pulled toward the team colour. §8.5 says 35%.</summary>
    public const float TeamTintAmount = 0.35f;

    private Renderer quadRenderer;
    private MaterialPropertyBlock properties;
    private float elapsed;

    /// <summary>
    /// Fires a flash at the muzzle. <paramref name="teamIndex"/> is the *shooter's* team, resolved
    /// through the viewer-relative palette so the local player's own fire always reads blue.
    /// </summary>
    public static void Fire(Vector3 position, int teamIndex)
    {
        GameObject spawned = FXAssets.Spawn(FXAssets.MuzzleFlash, position, Quaternion.identity);
        if (spawned == null)
            return;

        Color teamGlow = FXPalette.TeamGlow(teamIndex);
        MuzzleFlashFX flash = spawned.GetComponent<MuzzleFlashFX>();
        if (flash == null)
            flash = spawned.AddComponent<MuzzleFlashFX>();
        flash.Begin(Color.Lerp(FXPalette.CoreSoft, teamGlow, TeamTintAmount));

        if (FXPalette.IsVisibleToLocalTeam(position))
        {
            FXLightPool.TryFlash(
                position,
                FXPalette.TeamSrgb(teamIndex),
                LightRange,
                LightIntensity,
                LightLifetime
            );
        }
    }

    private void Begin(Color glow)
    {
        quadRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
        quadRenderer.GetPropertyBlock(properties);
        properties.SetColor(GlowColorId, glow);
        quadRenderer.SetPropertyBlock(properties);
        transform.localScale = Vector3.one * StartSize;
        FaceCamera();
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        transform.localScale = Vector3.one * Mathf.Lerp(StartSize, EndSize, elapsed / Lifetime);
        FaceCamera();
    }

    private void FaceCamera()
    {
        Camera viewCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (viewCamera == null)
            viewCamera = Camera.main;
        if (viewCamera != null)
            transform.rotation = viewCamera.transform.rotation;
    }
}
