using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AreaLock : Ability
{
    float abilityTime = 3;
    float rotationSpeed = 720f; // Degrees per second
    private LineRenderer laserLine;
    public GameObject superBulletBlue;
    public GameObject superBulletRed;
    float damageMultiplier = 2f;
    float delayForDodge = 0.5f;

    float initialWidth = 0.2f;
    float finalWidth = 0.5f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {

        transform.GetComponent<Movement>().PauseMovement();
        transform.GetComponent<Movement>().moving = false;

        transform.GetComponent<Shooting>().PauseShooting();

        yield return new WaitForSeconds(delayForDodge);

        Vector3 startPosition = transform.position = PlanMovement.GetNearestGridCell(transform.position) + Helper.heightOffset(transform); //snap to nearest cell
        Vector3 targetPosition = abilitySquare + Helper.heightOffset(transform);
        CreateLaserLine(startPosition, targetPosition);

        yield return StartCoroutine(transform.GetComponent<Movement>().RotateToFaceTarget(targetPosition, rotationSpeed));

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

    private void CreateLaserLine(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 finalEnd = end + direction * 50f;

        // Stop if there is a wall in the way
        if (Physics.Raycast(start, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls")))
        {
            finalEnd = hit.point;
        }

        GameObject laserObject = new GameObject("LaserLine");
        laserLine = laserObject.AddComponent<LineRenderer>();
        laserLine.material = new Material(Shader.Find("Sprites/Default"));
        laserLine.startColor = Color.red;
        laserLine.endColor = Color.red;
        laserLine.startWidth = 0.1f;
        laserLine.endWidth = 0.1f;
        laserLine.positionCount = 2;
        laserLine.SetPosition(0, start);
        laserLine.SetPosition(1, finalEnd);
        laserObject.transform.parent = transform;
    }

    private bool CheckForCrossingTarget(Vector3 start, Vector3 end)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        Vector3 direction = (end - start).normalized;

        if (Physics.Raycast(start, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls", enemyTeam)))
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
        // Create and fire a super damage bullet at the target
        Vector3 direction = (target.transform.position - transform.position).normalized;
        float bulletSpeed = direction.magnitude * 40f; // Example speed, adjust as needed

        //// Assuming there's a bullet prefab and shooting system
        //if (transform.GetComponent<Shooting>() != null)
        //{
        //    transform.GetComponent<Shooting>().FireBullet(spread: 0, bulletSpeed: bulletSpeed, backstabMultiplier: 2f, range: 10f, backstabAngle: 0f, bulletPrefab: gameObject.tag == "BlueTeam" ? superBulletBlue : superBulletRed); //guaranteed 2x damage hit
        //}

        StartCoroutine(AnimateLaserRush(bulletSpeed, target));
    }

    IEnumerator cleanup()
    {
        Destroy(laserLine?.gameObject);
        transform.GetComponent<Shooting>().stillShooting = false;
        yield return new WaitForSeconds(1f); //moment to regain composure
        if (transform.GetComponent<Shooting>().allowShooting)
        {
            transform.GetComponent<Shooting>().StartShooting();
        }
    }

    private IEnumerator AnimateLaserRush(float speed, GameObject target)
    {
        if (laserLine == null) yield break;

        float distance = Vector3.Distance(transform.position, target.transform.position);
        float rushDuration = distance / (speed * GameLoop.cellSize);

        // Setup laser segments
        int segments = 20;
        laserLine.positionCount = segments + 1;

        Vector3 startPos = transform.position;
        Vector3 endPos = target.transform.position;

        // Execute rush animation
        yield return StartCoroutine(AnimateRushEffect(rushDuration, segments, startPos, endPos));

        StartCoroutine(CreateExplosionEffect(target.transform.position));
        StartCoroutine(Camera.main.GetComponent<CameraEffects>().CameraShake());

        target.GetComponent<Health>()?.TakeDamage(transform.GetComponent<Shooting>().unitData.damage * damageMultiplier);

        // Execute cleanup animation
        yield return StartCoroutine(AnimateCleanupEffect(segments, endPos));
        StartCoroutine(cleanup());
    }

    private IEnumerator AnimateRushEffect(float rushDuration, int segments, Vector3 startPos, Vector3 endPos)
    {
        float elapsed = 0f;

        // Create animation curve for rush effect (peaks at middle, tapers at ends)
        AnimationCurve rushCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.1f, 1f),
            new Keyframe(0.9f, 1f),
            new Keyframe(1f, 0f)
        );

        while (elapsed < rushDuration && laserLine != null)
        {
            float progress = elapsed / rushDuration;

            // Update line positions
            for (int i = 0; i <= segments; i++)
            {
                float segmentProgress = (float)i / segments;
                Vector3 segmentPosition = Vector3.Lerp(startPos, endPos, segmentProgress);
                laserLine.SetPosition(i, segmentPosition);
            }

            // Create rush effect by modifying width based on progress
            AnimationCurve widthCurve = new AnimationCurve();
            for (int i = 0; i <= segments; i++)
            {
                float segmentProgress = (float)i / segments;
                float rushPosition = progress - segmentProgress;

                // Width is maximum when rush passes through this segment
                float width = initialWidth;
                if (rushPosition >= -0.1f && rushPosition <= 0.1f)
                {
                    float curveValue = rushCurve.Evaluate(Mathf.Abs(rushPosition) * 10f);
                    width = Mathf.Lerp(initialWidth, finalWidth, curveValue);
                }

                widthCurve.AddKey(segmentProgress, width);
            }

            laserLine.widthCurve = widthCurve;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator AnimateCleanupEffect(int segments, Vector3 endPos)
    {
        float cleanupDuration = 0.2f;
        float cleanupElapsed = 0f;

        while (cleanupElapsed < cleanupDuration && laserLine != null)
        {
            float cleanupProgress = cleanupElapsed / cleanupDuration;

            // Gradually reduce width back to initial
            float currentWidth = Mathf.Lerp(finalWidth, initialWidth, cleanupProgress);
            laserLine.widthCurve = AnimationCurve.Linear(0f, currentWidth, 1f, currentWidth);

            // Fade out color
            float alpha = Mathf.Lerp(1f, 0.5f, cleanupProgress);
            Color cleanupColor = new Color(laserLine.startColor.r, laserLine.startColor.g, laserLine.startColor.b, alpha);
            laserLine.startColor = cleanupColor;
            laserLine.endColor = cleanupColor;

            cleanupElapsed += Time.deltaTime;
            yield return null;
        }

        // Ensure final position is set
        laserLine.SetPosition(segments, endPos);
    }

    private IEnumerator CreateExplosionEffect(Vector3 explosionCenter)
    {
        float explosionDuration = 0.8f;
        int particleCount = 12;

        // Create explosion particles
        List<GameObject> particles = new List<GameObject>();
        List<Vector3> particleVelocities = new List<Vector3>();

        for (int i = 0; i < particleCount; i++)
        {
            GameObject particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            particle.transform.position = explosionCenter;
            particle.transform.localScale = Vector3.one * 0.2f;

            // Set particle color based on team
            Renderer particleRenderer = particle.GetComponent<Renderer>();
            particleRenderer.material = new Material(Shader.Find("Sprites/Default"));
            particleRenderer.material.color = gameObject.tag == "BlueTeam" ? Color.cyan : Color.red;

            // Remove collider to prevent physics interference
            Destroy(particle.GetComponent<Collider>());

            // Calculate random velocity direction
            float angle = (360f / particleCount) * i + Random.Range(-15f, 15f);
            Vector3 direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 velocity = direction * Random.Range(8f, 12f);

            particles.Add(particle);
            particleVelocities.Add(velocity);
        }

        float elapsed = 0f;

        while (elapsed < explosionDuration)
        {
            float progress = elapsed / explosionDuration;

            for (int i = 0; i < particles.Count; i++)
            {
                if (particles[i] != null)
                {
                    // Move particle outward
                    particles[i].transform.position += particleVelocities[i] * Time.deltaTime;

                    // Fade out particle
                    float alpha = Mathf.Lerp(1f, 0f, progress);
                    Color particleColor = particles[i].GetComponent<Renderer>().material.color;
                    particleColor.a = alpha;
                    particles[i].GetComponent<Renderer>().material.color = particleColor;

                    // Shrink particle over time
                    float scale = Mathf.Lerp(0.2f, 0f, progress);
                    particles[i].transform.localScale = Vector3.one * scale;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Clean up particles
        foreach (GameObject particle in particles)
        {
            if (particle != null)
                Destroy(particle);
        }
    }
}
