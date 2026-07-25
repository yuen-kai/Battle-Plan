using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    private static readonly HashSet<Bullet> activeServerBullets = new();

    /// <summary>
    /// The child the prefab builder draws a projectile's body on. Named here rather than twice, so
    /// the renderer BattlePlanFXPrefabBuilder authors and the renderer repainted below are the same
    /// object by construction instead of by coincidence.
    /// </summary>
    public const string BodyChildName = "Mesh";

    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f; // Angle in degrees from forward direction to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    // How the tracer is painted for whoever is watching it: own team first, enemy second, the same
    // order Unit uses for its team indicators.
    public List<Material> teamMaterials;

    /// <summary>
    /// The same viewer-relative pair in the same slot order, for the slug itself. A shot is drawn
    /// in two parts on two renderers — the tracer on the root, the body on
    /// <see cref="BodyChildName"/> — and §8.1 gives the bullet a team-coloured body, so both have
    /// to swap or a client sees its own fire as a red slug behind a blue tracer.
    ///
    /// EMPTY ON THE SNIPER ROUND, DELIBERATELY. §8.2 authors that lance in --bp-ink on both sides
    /// of the board: it is the round that one-shots, and on a bright deck the most alarming thing
    /// you can draw is a black lance. At 0.09 wide it is also the narrowest host in the game and
    /// the one projectile that clears the deck unaided rather than leaning on its contour, so ink
    /// is the robust choice as well as the dramatic one. Its team identity comes from the trail.
    /// Do not populate this to "match the bullet" — that trades the read for a consistency that
    /// §8.2 explicitly does not want.
    /// </summary>
    public List<Material> bodyTeamMaterials;

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    public static int ActiveServerBulletCount => activeServerBullets.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveBullets()
    {
        activeServerBullets.Clear();
    }

    /// <summary>
    /// The team that fired this shot, read off the team it is able to damage. Which prefab a shot
    /// spawns from is fixed by the shooter's real team — the prefab also carries which shields the
    /// round passes through — so this pairing holds on every peer with nothing replicated, and a
    /// bullet can be coloured on the frame it appears rather than a tick later.
    /// </summary>
    public static int GetShooterTeamIndex(string damageableTeam)
    {
        return GameLoop.GetEnemyTeamIndex(GameLoop.GetTeamIndex(damageableTeam));
    }

    public override void OnNetworkSpawn()
    {
        // Paint and sound the shot before the server-only guard below switches this component off
        // on clients. Both are local presentation and must happen on every peer.
        ApplyTeamPresentation();
        CombatAudio.BulletSpawned(this);
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        activeServerBullets.Add(this);
        startPosition = transform.position;
    }

    /// <summary>
    /// Colours the shot from the side of the board it is watched from. Both players read their own
    /// crew as blue, so a shot that kept the colour its prefab was authored in would come out as
    /// the wrong side's fire on the client's screen — every unit there is already drawn from the
    /// client's perspective.
    ///
    /// Both drawn parts, because only one of them used to swap: GetComponentInChildren reaches the
    /// root's TrailRenderer first, so the tracer flipped and the body kept whatever its prefab was
    /// authored as. That is right for the sniper, whose body is ink on both sides, and backwards
    /// for the bullet, which is where a client saw a red slug trailing a blue tracer.
    ///
    /// Local presentation on every peer: two renderer fields, no state, nothing replicated.
    /// </summary>
    private void ApplyTeamPresentation()
    {
        bool ownFire = GameLoop.IsTeamFriendlyToLocalPlayer(GetShooterTeamIndex(enemyTeam));
        Repaint(GetComponentInChildren<Renderer>(), teamMaterials, ownFire);
        Repaint(FindBodyRenderer(transform), bodyTeamMaterials, ownFire);
    }

    /// <summary>
    /// The renderer carrying a projectile's body, or null when it has none. Static and public so
    /// the prefab builder's pre-save check and its edit-mode test resolve the body exactly the way
    /// this does, rather than each naming the child it hopes to find.
    /// </summary>
    public static Renderer FindBodyRenderer(Transform projectileRoot)
    {
        Transform body = projectileRoot != null ? projectileRoot.Find(BodyChildName) : null;
        return body != null ? body.GetComponent<Renderer>() : null;
    }

    /// <summary>
    /// Swaps one renderer onto its own side of a viewer-relative pair. A missing or incomplete pair
    /// is the authored "this renderer is not team-coloured" state and is left exactly as drawn.
    /// </summary>
    private static void Repaint(Renderer target, List<Material> viewerRelativePair, bool ownFire)
    {
        if (target == null || viewerRelativePair == null || viewerRelativePair.Count < 2)
            return;

        target.sharedMaterial = ownFire ? viewerRelativePair[0] : viewerRelativePair[1];
    }

    public override void OnNetworkDespawn()
    {
        activeServerBullets.Remove(this);
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        activeServerBullets.Remove(this);
    }

    void Update()
    {
        if (IsPathBlockedBySmoke(transform.position))
        {
            NetworkHelper.Instance.Despawn(gameObject);
            return;
        }

        if (Vector3.Distance(startPosition, transform.position) > range || timeElapsed > maxLifetime)
        {
            NetworkHelper.Instance.Despawn(gameObject);
            return;
        }
        timeElapsed += Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        GameObject hitObject = other.gameObject;
        bool hitUnit =
            hitObject.CompareTag(enemyTeam) && !IsPathBlockedBySmoke(hitObject.transform.position);
        if (hitUnit)
        {
            float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }

        // A hit on a unit already sounds itself through replicated health, so only the two cases
        // that leave no other trace are relayed: a round stopped by a shield, and one that buries
        // itself in cover.
        if (!hitUnit)
        {
            Vector3 contactPoint =
                other.contactCount > 0 ? other.GetContact(0).point : transform.position;
            GameLoop.Instance?.BroadcastImpactAudio(
                contactPoint,
                IsShieldCollider(hitObject)
                    ? GameLoop.ImpactAudioKind.ShieldBlock
                    : GameLoop.ImpactAudioKind.Surface
            );
        }

        NetworkHelper.Instance.Despawn(gameObject);
    }

    /// <summary>
    /// Shields are colliders on their own per-team layers rather than damage interceptors, so a
    /// blocked round is identified by the layer it stopped on.
    /// </summary>
    private static bool IsShieldCollider(GameObject hitObject)
    {
        int layer = hitObject.layer;
        return layer == LayerMask.NameToLayer(Shield.BlueShieldLayerName)
            || layer == LayerMask.NameToLayer(Shield.RedShieldLayerName);
    }

    private bool IsPathBlockedBySmoke(Vector3 destination)
    {
        return GameLoop.Instance != null
            && GameLoop.Instance.DoesWorldSegmentCrossActiveSmoke(startPosition, destination);
    }

    private bool CheckBackstab(GameObject hitObject)
    {
        Vector3 targetForward = hitObject.transform.forward;
        Vector3 bulletDirection = (hitObject.transform.position - startPosition).normalized;
        float angle = Vector3.Angle(targetForward, -bulletDirection);
        return angle >= backstabAngle;
    }
}
