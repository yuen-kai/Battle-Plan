using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A shot in flight. Bullets are no longer network objects: the server instantiates an authoritative
/// one that resolves damage, and every peer instantiates its own visual-only copy from a single
/// <c>FireBulletClientRpc</c>. Travel time is gameplay here - at three cells per second a shot is
/// airborne long enough for its target to walk out of it - so the server still simulates a real
/// projectile rather than resolving the hit at the muzzle.
/// </summary>
public class Bullet : MonoBehaviour
{
    private static readonly HashSet<Bullet> activeServerBullets = new();

    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f; // Angle in degrees from forward direction to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    // How the shot is painted for whoever is watching it: own team first, enemy second, the same
    // order Unit uses for its team indicators.
    public List<Material> teamMaterials;

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    /// <summary>
    /// Only the server's copy applies damage. Every other copy is a tracer that flies the same path
    /// and stops on the same geometry, purely so the shot is visible.
    /// </summary>
    private bool isAuthoritative;

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

    /// <summary>
    /// Arms the shot. Called immediately after instantiation rather than from Awake, because the
    /// damage figures and the team it may hit are only known to the caller.
    /// </summary>
    public void Initialize(
        Vector3 velocity,
        float shotDamage,
        float shotBackstabMultiplier,
        float shotRange,
        float shotBackstabAngle,
        string damageableTeam,
        bool authoritative
    )
    {
        damage = shotDamage;
        backstabMultiplier = shotBackstabMultiplier;
        range = shotRange;
        backstabAngle = shotBackstabAngle;
        enemyTeam = damageableTeam;
        isAuthoritative = authoritative;
        startPosition = transform.position;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
            body.linearVelocity = velocity;

        ApplyTeamPresentation();

        if (authoritative)
            activeServerBullets.Add(this);
    }

    /// <summary>
    /// Colours the shot from the side of the board it is watched from. Both players read their own
    /// crew as blue, so a shot that kept the colour its prefab was authored in would come out as
    /// the wrong side's fire on the client's screen — every unit there is already drawn from the
    /// client's perspective.
    /// </summary>
    private void ApplyTeamPresentation()
    {
        Renderer bulletRenderer = GetComponentInChildren<Renderer>();
        if (bulletRenderer == null || teamMaterials == null || teamMaterials.Count < 2)
            return;

        bulletRenderer.sharedMaterial = GameLoop.IsTeamFriendlyToLocalPlayer(
            GetShooterTeamIndex(enemyTeam)
        )
            ? teamMaterials[0]
            : teamMaterials[1];
    }

    private void OnDestroy()
    {
        activeServerBullets.Remove(this);
    }

    void Update()
    {
        if (IsPathBlockedBySmoke(transform.position))
        {
            Destroy(gameObject);
            return;
        }

        if (Vector3.Distance(startPosition, transform.position) > range || timeElapsed > maxLifetime)
        {
            Destroy(gameObject);
            return;
        }
        timeElapsed += Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        GameObject hitObject = other.gameObject;
        if (
            isAuthoritative
            && hitObject.CompareTag(enemyTeam)
            && !IsPathBlockedBySmoke(hitObject.transform.position)
        )
        {
            float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }

        Destroy(gameObject);
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
