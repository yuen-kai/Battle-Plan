using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The sound of the execution phase, built entirely from things that already replicate.
///
/// <para><b>Why the bullet is the hook.</b> <see cref="Shooting"/> runs on the server and disables
/// itself everywhere else, so a client never sees a trigger pull directly. What it does see is the
/// projectile: bullets are NetworkObjects and spawn on every peer. So the muzzle report is played
/// from the bullet's spawn, and the shooter is identified by the unit standing at that position —
/// the bullet is spawned at <c>shooter.transform.position</c>, and units are always at least one
/// 2.7-unit cell apart, so the match is unambiguous. That costs no bandwidth at all, where a
/// per-shot RPC would add one message per bullet with a ten-pellet shotgun burst as the worst case.</para>
///
/// <para><b>Why that is also fog-correct.</b> A shooter hidden by fog is not spawned on this
/// client, so there is nothing to resolve and nothing plays. On the host — which cannot hide
/// objects from itself — the unit is present, and the explicit visibility gate in
/// <see cref="BattlePlanAudio.PlayAt"/> is what stops the report. The two paths fail in the same
/// safe direction.</para>
///
/// <para>Damage and death come from <see cref="Health"/>'s NetworkVariable callbacks, which fire on
/// exactly the peers the unit is visible to, for the same reason the hit flash does.</para>
/// </summary>
public static class CombatAudio
{
    /// <summary>How close a round has to pass one of your units to be heard going by.</summary>
    private const float WhizzRadius = 1.5f;
    private const float ShooterMatchRadius = 1.35f;
    private const float LongReloadThreshold = 1.6f;

    private class TrackedBullet
    {
        public Transform transform;
        public bool whizzed;
    }

    private static readonly List<TrackedBullet> tracked = new();
    private static readonly Dictionary<Unit, int> shotsSinceReload = new();
    private static readonly List<(float dueAt, Vector3 position, GameObject unit, AudioCueId cue)> pendingReloads = new();
    private static readonly List<GameObject> localUnitCache = new();
    private static float localUnitCacheRefreshedAt = -1f;
    private static readonly Dictionary<Transform, Vector2Int> lastStepCell = new();
    private static readonly List<Transform> stepCleanup = new();

    /// <summary>Called from <see cref="Bullet.OnNetworkSpawn"/> on every peer.</summary>
    public static void BulletSpawned(Bullet bullet)
    {
        if (bullet == null || !Application.isPlaying)
            return;

        BattlePlanAudio.EnsureInstance();
        tracked.Add(new TrackedBullet { transform = bullet.transform });

        GameObject shooter = ResolveShooter(bullet.transform.position, bullet.enemyTeam);
        UnitData data = shooter != null ? shooter.GetComponent<Movement>()?.unitData : null;
        if (data == null)
            return;

        BattlePlanAudio.PlayAt(WeaponCueFor(data), bullet.transform.position, shooter);
        TrackMagazine(shooter, data);
    }

    /// <summary>Called from <see cref="Health"/> when replicated HP drops.</summary>
    public static void DamageTaken(GameObject unit, float lost, float maxHealth)
    {
        if (unit == null)
            return;

        bool friendly = AudioVisibilityGate.IsLocalTeamUnit(unit);
        // A heavy hit reads as armour giving way rather than another body thump.
        bool heavy = maxHealth > 0f && lost >= maxHealth * 0.35f;
        AudioCueId cue = friendly
            ? AudioCueId.DamageTaken
            : (heavy ? AudioCueId.ImpactArmor : AudioCueId.ImpactBody);

        BattlePlanAudio.PlayAt(cue, unit.transform.position, unit);
    }

    /// <summary>Called from <see cref="Health"/> when a unit's replicated alive flag clears.</summary>
    public static void UnitEliminated(GameObject unit)
    {
        if (unit == null)
            return;

        BattlePlanAudio.PlayAt(AudioCueId.UnitEliminated, unit.transform.position, unit);
        Unit identity = unit.GetComponent<Unit>();
        if (identity != null)
            shotsSinceReload.Remove(identity);
    }

    public static void UnitSpawned(GameObject unit)
    {
        if (unit != null)
            BattlePlanAudio.PlayAt(AudioCueId.UnitSpawn, unit.transform.position, unit);
    }

    // === Area Lock ==========================================================
    // The beam's three client-visible moments — armed, fired, gone — arrive as separate RPCs, so
    // the loop handle and the did-it-fire flag are parked here rather than in the ability.

    private static readonly Dictionary<GameObject, AudioLoopHandle> areaLockCharges = new();
    private static readonly HashSet<GameObject> areaLockFired = new();

    public static void AreaLockArmed(GameObject caster)
    {
        if (caster == null || areaLockCharges.ContainsKey(caster))
            return;

        areaLockFired.Remove(caster);
        areaLockCharges[caster] = BattlePlanAudio.PlayLoop(
            AudioCueId.AreaLockCharge,
            caster.transform
        );
    }

    public static void AreaLockFired(GameObject caster, Vector3 endPoint)
    {
        if (caster != null)
            areaLockFired.Add(caster);
        BattlePlanAudio.PlayAt(AudioCueId.AreaLockFire, endPoint, caster);
    }

    public static void AreaLockImpact(GameObject caster, Vector3 impactPoint)
    {
        BattlePlanAudio.PlayAt(AudioCueId.AreaLockImpact, impactPoint, caster);
    }

    /// <summary>
    /// Area Lock can run its full window without anything crossing the ray. Ending on silence
    /// reads as a bug, so an unfired beam gets a short unresolved de-charge instead.
    /// </summary>
    public static void AreaLockEnded(GameObject caster)
    {
        if (caster == null)
            return;

        if (areaLockCharges.Remove(caster, out AudioLoopHandle charge))
            charge.Stop(0.18f);
        if (!areaLockFired.Remove(caster))
            BattlePlanAudio.PlayAt(AudioCueId.AreaLockExpire, caster.transform.position, caster);
    }

    public static void ShieldStateChanged(GameObject unit, bool raised)
    {
        if (unit != null)
        {
            BattlePlanAudio.PlayAt(
                raised ? AudioCueId.ShieldRaise : AudioCueId.ShieldDrop,
                unit.transform.position,
                unit
            );
        }
    }

    /// <summary>Magazine tracking and in-flight rounds do not survive a round boundary.</summary>
    public static void RoundChanged()
    {
        tracked.Clear();
        shotsSinceReload.Clear();
        pendingReloads.Clear();
        lastStepCell.Clear();
        foreach (AudioLoopHandle charge in areaLockCharges.Values)
            charge.Stop(0.1f);
        areaLockCharges.Clear();
        areaLockFired.Clear();
    }

    /// <summary>Ticked once per frame by <see cref="BattlePlanAudio"/>.</summary>
    public static void Tick()
    {
        TickWhizz();
        TickReloads();
        TickFootsteps();
    }

    /// <summary>
    /// Confirms, on the local client only, that a dive was actually queued for each unit the player
    /// moved during the dodge window. This runs in the submit callback before the paths go to the
    /// server, so it is pure local feedback on the player's own input and says nothing about the
    /// opposing team. A unit whose path is just its start cell declined the dive and stays silent.
    /// </summary>
    public static void LocalDivesSubmitted(PathsDict paths)
    {
        if (paths == null)
            return;

        foreach (KeyValuePair<GameObject, (bool, List<Vector3>)> entry in paths)
        {
            GameObject unit = entry.Key;
            List<Vector3> route = entry.Value.Item2;
            if (unit == null || route == null || route.Count < 2)
                continue;

            BattlePlanAudio.PlayAt(AudioCueId.UnitDive, unit.transform.position, unit);
        }
    }

    // === footsteps ==========================================================

    /// <summary>
    /// One step per cell entered, derived by watching replicated transforms rather than by hooking
    /// movement. Units are moved by <see cref="Movement"/> on the server and their positions
    /// replicate, so every peer sees the same crossings without a single extra message; a unit
    /// hidden by fog is not spawned here at all, and on the host the visibility gate in
    /// <see cref="BattlePlanAudio.PlayAt"/> catches it instead.
    /// </summary>
    private static void TickFootsteps()
    {
        if (!Application.isPlaying || GameLoop.Instance == null)
            return;

        stepCleanup.Clear();
        foreach (KeyValuePair<Transform, Vector2Int> entry in lastStepCell)
        {
            if (entry.Key == null || !entry.Key.gameObject.activeInHierarchy)
                stepCleanup.Add(entry.Key);
        }
        foreach (Transform stale in stepCleanup)
            lastStepCell.Remove(stale);

        NetworkManager manager = NetworkManager.Singleton;
        if (manager?.SpawnManager == null)
            return;

        foreach (NetworkObject netObj in manager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || !netObj.gameObject.activeInHierarchy)
                continue;

            Unit unit = netObj.GetComponent<Unit>();
            if (unit == null)
                continue;

            Health health = netObj.GetComponent<Health>();
            if (health != null && !health.IsAlive)
                continue;

            Transform key = netObj.transform;
            Vector2Int cell = GridSystem.ConvertToGridCoords(
                GridSystem.GetNearestGridCell(netObj.gameObject)
            );

            if (!lastStepCell.TryGetValue(key, out Vector2Int previous))
            {
                lastStepCell[key] = cell;
                continue;
            }

            if (previous == cell)
                continue;

            lastStepCell[key] = cell;
            BattlePlanAudio.PlayAt(AudioCueId.UnitStep, key.position, netObj.gameObject);
        }
    }

    // === bullets passing your crew ==========================================

    private static void TickWhizz()
    {
        if (tracked.Count == 0)
            return;

        RefreshLocalUnits();
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            TrackedBullet bullet = tracked[i];
            if (bullet.transform == null)
            {
                tracked.RemoveAt(i);
                continue;
            }
            if (bullet.whizzed)
                continue;

            Vector3 position = bullet.transform.position;
            foreach (GameObject unit in localUnitCache)
            {
                if (unit == null)
                    continue;

                Vector3 delta = unit.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude > WhizzRadius * WhizzRadius)
                    continue;

                bullet.whizzed = true;
                // Deliberately fog-piercing: "you are being shot from over there" with no cell
                // given away. The tracer that produced it is already visible through fog.
                BattlePlanAudio.PlayAt(AudioCueId.BulletWhizz, position);
                break;
            }
        }
    }

    private static void RefreshLocalUnits()
    {
        if (Time.unscaledTime - localUnitCacheRefreshedAt < 0.25f)
            return;

        localUnitCacheRefreshedAt = Time.unscaledTime;
        localUnitCache.Clear();

        NetworkManager manager = NetworkManager.Singleton;
        if (manager?.SpawnManager == null || GameLoop.Instance == null)
            return;

        int localTeam = GameLoop.Instance.LocalTeamIndex;
        foreach (NetworkObject netObj in manager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null)
                continue;
            Unit identity = netObj.GetComponent<Unit>();
            if (identity != null && identity.TeamIndex == localTeam && netObj.gameObject.activeInHierarchy)
                localUnitCache.Add(netObj.gameObject);
        }
    }

    // === magazines ==========================================================

    private static void TrackMagazine(GameObject shooter, UnitData data)
    {
        Unit identity = shooter.GetComponent<Unit>();
        if (identity == null || data.magazineSize <= 0)
            return;

        shotsSinceReload.TryGetValue(identity, out int fired);
        fired++;
        if (fired < data.magazineSize)
        {
            shotsSinceReload[identity] = fired;
            return;
        }

        shotsSinceReload[identity] = 0;
        // The magazine is empty, so the reload the server is about to run is predictable from
        // replicated data alone. Scheduling it locally keeps a very audible tell — a unit standing
        // there unable to shoot — free of network traffic.
        pendingReloads.Add(
            (
                Time.time + 0.12f,
                shooter.transform.position,
                shooter,
                data.reloadTime >= LongReloadThreshold
                    ? AudioCueId.WeaponReloadLong
                    : AudioCueId.WeaponReloadShort
            )
        );
    }

    private static void TickReloads()
    {
        for (int i = pendingReloads.Count - 1; i >= 0; i--)
        {
            var pending = pendingReloads[i];
            if (Time.time < pending.dueAt)
                continue;

            pendingReloads.RemoveAt(i);
            if (pending.unit == null || !pending.unit.activeInHierarchy)
                continue;

            BattlePlanAudio.PlayAt(pending.cue, pending.unit.transform.position, pending.unit);
        }
    }

    // === shooter identification =============================================

    private static GameObject ResolveShooter(Vector3 muzzle, string damageableTeam)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager?.SpawnManager == null)
            return null;

        int shooterTeam = Bullet.GetShooterTeamIndex(damageableTeam);
        GameObject best = null;
        float bestDistance = ShooterMatchRadius * ShooterMatchRadius;

        foreach (NetworkObject netObj in manager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null)
                continue;
            Unit identity = netObj.GetComponent<Unit>();
            if (identity == null || identity.TeamIndex != shooterTeam)
                continue;

            Vector3 delta = netObj.transform.position - muzzle;
            delta.y = 0f;
            float distance = delta.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = netObj.gameObject;
            }
        }
        return best;
    }

    /// <summary>
    /// One voice per archetype. Keyed on stats rather than on the unit's display name so a roster
    /// rename cannot silently drop a weapon back to the default.
    /// </summary>
    public static AudioCueId WeaponCueFor(UnitData data)
    {
        if (data == null)
            return AudioCueId.WeaponRifle;
        if (data.targetLockDuration > 0f)
            return AudioCueId.WeaponSniper;
        if (data.magazineSize >= 8 && data.timeBetweenShots <= 0.05f)
            return AudioCueId.WeaponShotgun;
        if (data.moveDist >= 5)
            return AudioCueId.WeaponSmg;
        if (data.bulletRange >= 8)
            return AudioCueId.WeaponRifle;
        return AudioCueId.WeaponPistol;
    }
}
