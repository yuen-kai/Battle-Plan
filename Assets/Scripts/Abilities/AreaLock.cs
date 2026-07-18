using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class AreaLock : Ability
{
    float abilityTime = 3;
    float rotationSpeed = 720f; // Degrees per second
    public GameObject superBulletBlue;
    public GameObject superBulletRed;
    float damageMultiplier = 2f;
    float delayForDodge = 0.5f;

    // Threat-red laser regardless of team: it reads as "danger line" to both players.
    static readonly Color LaserGlow = new(1f, 0.14f, 0.25f);

    private BeamVFX serverBeam;
    private BeamVFX clientBeam;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
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

        // Impact: shockwave on every peer; HitFlash fires everywhere via Health's
        // NetworkVariable callback when the damage below replicates.
        ImpactFxClientRpc(endPos);
        ImpactShockwave.Spawn(endPos, LaserGlow, 3f);
        CameraEffects.Instance?.CameraShakeClientRpc();

        target
            .GetComponent<Health>()
            ?.TakeDamage(transform.GetComponent<Shooting>().unitData.damage * damageMultiplier);

        StartCoroutine(cleanup());
    }

    [ClientRpc]
    private void StartRushClientRpc(float rushDuration, Vector3 startPos, Vector3 endPos)
    {
        if (IsServer || clientBeam == null)
            return;
        clientBeam.SetPositions(startPos, endPos);
        StartCoroutine(clientBeam.Rush(rushDuration));
    }

    [ClientRpc]
    private void ImpactFxClientRpc(Vector3 impactPoint)
    {
        if (IsServer)
            return; // host spawns its shockwave directly in AnimateLaserRush
        ImpactShockwave.Spawn(impactPoint, LaserGlow, 3f);
    }
}
