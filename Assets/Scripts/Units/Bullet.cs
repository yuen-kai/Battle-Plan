using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
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
        // Paint the shot before the server-only guard below switches this component off on clients.
        ApplyTeamPresentation();
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
        if (
            hitObject.CompareTag(enemyTeam)
            && !IsPathBlockedBySmoke(hitObject.transform.position)
        )
        {
            float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }

        NetworkHelper.Instance.Despawn(gameObject);
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
