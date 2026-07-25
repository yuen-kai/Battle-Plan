using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class AreaLock : Ability
{
    public const float AbilityDamage = 130f;

    float abilityTime = 3;
    float rotationSpeed = 720f; // Degrees per second
    public GameObject superBulletBlue;
    public GameObject superBulletRed;
    float delayForDodge = 0.5f;

    // Threat-red laser regardless of team: it reads as "danger line" to both players.
    static Color LaserGlow => FXPalette.Red;

    private BeamVFX serverBeam;
    private BeamVFX clientBeam;

    /// <summary>Client-local telegraph discs for the armed line. Presentation only.</summary>
    private System.Collections.Generic.List<GroundTelegraph> armedCells;

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        foreach (BeamVFX beam in GetComponentsInChildren<BeamVFX>(includeInactive: true))
        {
            beam.gameObject.SetActive(false);
            Destroy(beam.gameObject);
        }
        serverBeam = null;
        clientBeam = null;
        AbilityFX.AreaLockRelease(armedCells);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        GameLoop.Instance?.ForceRevealToEnemyTeams(
            gameObject,
            delayForDodge + abilityTime + 1.5f
        );

        transform.GetComponent<Movement>().PauseMovement();
        transform.GetComponent<Movement>().moving = false;

        transform.GetComponent<Shooting>().PauseShooting();

        yield return new WaitForSeconds(delayForDodge);

        Vector3 startPosition = transform.position =
            GridSystem.GetNearestGridCell(transform.position) + Helper.heightOffset(transform); //snap to nearest cell
        Vector3 targetPosition = abilitySquare + Helper.heightOffset(transform);

        Vector3 beamEnd = ComputeBeamEnd(startPosition, targetPosition);
        serverBeam = BeamVFX.Create(transform, startPosition, beamEnd, LaserGlow);
        serverBeam.Pulse();
        ShowLaserClientRpc(startPosition, beamEnd);

        yield return StartCoroutine(
            transform.GetComponent<Movement>().RotateToFaceTarget(targetPosition, rotationSpeed)
        );

        float elapsed = 0f;
        while (elapsed < abilityTime && !CheckForCrossingTarget(startPosition, targetPosition))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= abilityTime)
        {
            StartCoroutine(cleanup());
        }
    }

    /// <summary>Beam runs past the target square until it hits a wall (or 50 units).</summary>
    private static Vector3 ComputeBeamEnd(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 finalEnd = end + direction * 50f;
        if (
            Physics.Raycast(
                start,
                direction,
                out RaycastHit hit,
                Mathf.Infinity,
                LayerMask.GetMask("Walls")
            )
        )
        {
            finalEnd = hit.point;
        }
        return finalEnd;
    }

    [ClientRpc]
    private void ShowLaserClientRpc(Vector3 start, Vector3 end)
    {
        CombatAudio.AreaLockArmed(gameObject);

        // Anticipation (§9.3): a disc on every cell the line crosses, so a player can count the
        // cells they have to leave. Runs on the host too — the host renders the server beam but
        // has no discs of its own.
        AbilityFX.AreaLockRelease(armedCells);
        armedCells = AbilityFX.AreaLockArm(start, end);

        if (IsServer)
            return; // host already renders the server beam

        if (clientBeam != null)
            Destroy(clientBeam.gameObject);
        clientBeam = BeamVFX.Create(transform, start, end, LaserGlow);
        clientBeam.Pulse();
    }

    [ClientRpc]
    private void HideLaserClientRpc()
    {
        CombatAudio.AreaLockEnded(gameObject);
        AbilityFX.AreaLockRelease(armedCells);
        if (clientBeam != null)
        {
            StartCoroutine(clientBeam.FadeOut(0.2f));
            clientBeam = null;
        }
    }

    private bool CheckForCrossingTarget(Vector3 start, Vector3 end)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        Vector3 direction = (end - start).normalized;

        if (
            Physics.Raycast(
                start,
                direction,
                out RaycastHit hit,
                Mathf.Infinity,
                LayerMask.GetMask("Walls", enemyTeam)
            )
        )
        {
            GameObject target = hit.collider.gameObject;
            if (target.tag == enemyTeam)
            {
                if (
                    GameLoop.Instance != null
                    && GameLoop.Instance.DoesWorldSegmentCrossActiveSmoke(
                        start,
                        target.transform.position
                    )
                )
                {
                    return false;
                }

                FireSuperDamageBullet(target);
                return true;
            }
        }
        return false;
    }

    private void FireSuperDamageBullet(GameObject target)
    {
        Vector3 direction = (target.transform.position - transform.position).normalized;
        float bulletSpeed = direction.magnitude * 40f;

        StartCoroutine(AnimateLaserRush(bulletSpeed, target));
    }

    IEnumerator cleanup()
    {
        if (serverBeam != null)
        {
            StartCoroutine(serverBeam.FadeOut(0.2f));
            serverBeam = null;
        }
        HideLaserClientRpc();
        transform.GetComponent<Shooting>().stillShooting = false;
        yield return new WaitForSeconds(1f); //moment to regain composure
        if (transform.GetComponent<Shooting>().allowShooting)
        {
            transform.GetComponent<Shooting>().StartShooting();
        }
    }

    private IEnumerator AnimateLaserRush(float speed, GameObject target)
    {
        if (serverBeam == null)
            yield break;

        Vector3 startPos = transform.position;
        Vector3 endPos = target.transform.position;
        float distance = Vector3.Distance(startPos, endPos);
        float rushDuration = distance / (speed * GameLoop.cellSize);

        // Shorten the beam to the victim and surge into them, mirrored on clients
        serverBeam.SetPositions(startPos, endPos);
        StartRushClientRpc(rushDuration, startPos, endPos);

        yield return StartCoroutine(serverBeam.Rush(rushDuration));

        // Impact on every peer, host included, through one RPC — the shake and the shockwave used
        // to be a second broadcast and a host-only duplicate. HitFlash still rides Health's
        // NetworkVariable callback when the damage below replicates.
        //
        // Lethality has to be decided here: a client cannot know the shot killed until the health
        // value replicates, by which point the hitstop would land after the death.
        Health victimHealth = target.GetComponent<Health>();
        bool lethal = victimHealth != null && victimHealth.CurrentHealth <= AbilityDamage;
        ImpactFxClientRpc(endPos, lethal);

        victimHealth?.TakeDamage(AbilityDamage);

        StartCoroutine(cleanup());
    }

    [ClientRpc]
    private void StartRushClientRpc(float rushDuration, Vector3 startPos, Vector3 endPos)
    {
        CombatAudio.AreaLockFired(gameObject, endPos);
        if (IsServer || clientBeam == null)
            return;
        clientBeam.SetPositions(startPos, endPos);
        StartCoroutine(clientBeam.Rush(rushDuration));
    }

    [ClientRpc]
    private void ImpactFxClientRpc(Vector3 impactPoint, bool lethal)
    {
        CombatAudio.AreaLockImpact(gameObject, impactPoint);
        // Victim is left null: the white flash on the body comes from Health's replicated health
        // change, which already fires on every peer and is correct even through a fog reveal.
        AbilityFX.AreaLockImpact(impactPoint, null, lethal);
    }
}
