using UnityEngine;

/// <summary>
/// Everything a projectile does visually on its own: the flash it leaves at the muzzle, the chip
/// it knocks off a wall, and — for the grenade — the fuse counting down (ArtDirection §8.1–§8.5).
///
/// Added to the projectile prefabs by Battle Plan ▸ FX ▸ Build Projectile Prefabs, and a
/// plain MonoBehaviour on purpose: hanging a second NetworkBehaviour off a network-registered
/// prefab is a networking change, and none of this needs one. Both the spawn and the despawn are
/// already replicated events that every peer observes, so the effect rides on them for free with
/// no RPC and no new state.
///
/// Fog (§9.5): projectiles are spawned with observers on both peers and deliberately pierce fog,
/// exactly as tracers and telegraphs do. These effects sit on the projectile's own path and
/// therefore disclose nothing it does not. Only the muzzle *light* is visibility-gated — see
/// MuzzleFlashFX.
/// </summary>
public sealed class ProjectileFX : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public enum ProjectileKind
    {
        Bullet,
        Sniper,
        Grenade,
        SmokeCanister,
    }

    /// <summary>Name of the grenade's emissive fuse child, created by the prefab builder.</summary>
    public const string FuseDotChildName = "FuseDot";

    /// <summary>How close to cover a despawn must be to count as an impact rather than expiry.</summary>
    public const float WallContactRadius = 0.45f;

    // Grenade fuse blink, §8.3: a square-ish 0→1→0 ramping from 2 Hz to 9 Hz across the 1 s arc.
    public const float FuseStartHz = 2f;
    public const float FuseEndHz = 9f;
    public const float FuseRampSeconds = 1f;

    [Tooltip("Which §8 projectile this is. Set by the prefab builder, not by hand.")]
    public ProjectileKind kind = ProjectileKind.Bullet;

    private static readonly Collider[] wallProbe = new Collider[4];

    private Renderer fuseRenderer;
    private MaterialPropertyBlock properties;
    private int shooterTeamIndex = -1;
    private float livedFor;
    private bool sceneIsLive;

    private void Start()
    {
        sceneIsLive = true;
        shooterTeamIndex = ResolveShooterTeam();

        if (kind == ProjectileKind.Bullet || kind == ProjectileKind.Sniper)
            MuzzleFlashFX.Fire(transform.position, shooterTeamIndex);

        // Refused over the four-shadow cap, which is the whole point of asking rather than
        // spawning: a Shotgunner burst keeps its tracers and drops the extra shadows.
        ContactShadowFX.TryTrack(transform, ContactShadowDiameterFor(kind));

        if (kind == ProjectileKind.Grenade)
        {
            // Searched as a descendant rather than a direct child. The builder parents FuseDot under
            // the body mesh, so a Find on the root returned null and Update bailed every frame,
            // leaving the fuse lit at its authored emission instead of blinking.
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name != FuseDotChildName)
                    continue;

                fuseRenderer = child.GetComponent<Renderer>();
                properties = new MaterialPropertyBlock();
                break;
            }
        }
    }

    private void Update()
    {
        if (fuseRenderer == null)
            return;

        livedFor += Time.deltaTime;
        float ramp = Mathf.Clamp01(livedFor / FuseRampSeconds);
        float hertz = Mathf.Lerp(FuseStartHz, FuseEndHz, ramp);

        // Square-ish rather than sinusoidal: a fuse blinks, it does not breathe.
        float phase = Mathf.Repeat(livedFor * hertz, 1f);
        float lit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - Mathf.Abs(phase - 0.25f) * 5f));

        fuseRenderer.GetPropertyBlock(properties);
        properties.SetColor(EmissionColorId, FXPalette.MuzzleCore * lit);
        fuseRenderer.SetPropertyBlock(properties);
    }

    /// <summary>
    /// Contact shadows are sized off the projectile, not off a single constant: a grenade arcing
    /// overhead needs a shadow you can read the arc from, a pellet needs a dot.
    /// </summary>
    private static float ContactShadowDiameterFor(ProjectileKind projectile)
    {
        return projectile switch
        {
            ProjectileKind.Grenade => FXPalette.ContactShadowDiameter * 2f,
            ProjectileKind.SmokeCanister => FXPalette.ContactShadowDiameter * 1.6f,
            ProjectileKind.Sniper => FXPalette.ContactShadowDiameter * 1.2f,
            _ => FXPalette.ContactShadowDiameter,
        };
    }

    private void OnDestroy()
    {
        ContactShadowFX.Release(transform);

        // Scene teardown and play-mode exit also destroy this object; neither is an impact.
        if (!sceneIsLive || !gameObject.scene.isLoaded || GameLoop.Instance == null)
            return;
        if (kind != ProjectileKind.Bullet && kind != ProjectileKind.Sniper)
            return;

        // A round that ends its life next to hard cover or a raised shield stopped there. Probing
        // on despawn rather than asking the server means no new RPC and no new replicated state —
        // the despawn is already an event every peer sees.
        int blockers = LayerMask.GetMask(
            "Walls",
            Shield.BlueShieldLayerName,
            Shield.RedShieldLayerName
        );
        int touching = Physics.OverlapSphereNonAlloc(
            transform.position,
            WallContactRadius,
            wallProbe,
            blockers,
            QueryTriggerInteraction.Ignore
        );
        if (touching == 0)
            return;

        CombatFX.WallImpact(transform.position, shooterTeamIndex);

        for (int index = 0; index < touching; index++)
        {
            Transform shield = wallProbe[index] != null ? wallProbe[index].transform : null;
            if (shield != null && shield.GetComponent<ShieldFX>() != null)
            {
                ShieldFX.Deflect(shield, transform.position, shooterTeamIndex);
                break;
            }
        }
    }

    /// <summary>
    /// Which side fired this. Read off the team the round can damage, the same pairing
    /// Bullet.ApplyTeamPresentation uses — it is fixed by the prefab, so it is already correct on
    /// a client on the frame the projectile appears.
    /// </summary>
    private int ResolveShooterTeam()
    {
        return TryGetComponent(out Bullet bullet)
            ? Bullet.GetShooterTeamIndex(bullet.enemyTeam)
            : -1;
    }
}
