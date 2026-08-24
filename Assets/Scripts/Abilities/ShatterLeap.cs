using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ShatterLeap : Ability
{
    private const float AbilityTime = 2.1f;

    private const float RecoverySeconds = 1.2f;

    private const float ArcApexHeight = 5f;

    [SerializeField]
    private float damage = 60f;

    private Vector3? returnPositionOnInterrupt;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        return AbilityTrajectory.BuildLob(
            transform.position,
            GetLandingPosition(targetSquare),
            ArcApexHeight,
            points
        )
            ? AbilityPathKind.Lob
            : AbilityPathKind.None;
    }

    public override bool TryGetCasterDestination(
        Vector3 targetSquare,
        UnitData data,
        out Vector2Int destinationCell
    )
    {
        destinationCell = GridSystem.ConvertToGridCoords(targetSquare);
        return true;
    }

    private Vector3 GetLandingPosition(Vector3 abilitySquare)
    {
        return abilitySquare + Helper.heightOffset(transform);
    }

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        Collider unitCollider = GetComponent<Collider>();
        if (unitCollider != null)
            unitCollider.enabled = true;
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 1.6f)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Collider unitCollider = GetComponent<Collider>();
        if (movement == null || unitCollider == null)
            yield break;

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = GetLandingPosition(abilitySquare);

        movement.PauseMovement();
        BeginInterruptibleAbilityAction();
        returnPositionOnInterrupt = startPosition;
        movement.moving = true;
        unitCollider.enabled = false;

        LaunchFxClientRpc(targetPosition, AreaRadius, AbilityTime);

        float elapsed = 0f;

        while (elapsed < AbilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / AbilityTime;

            transform.position = AbilityTrajectory.SampleLob(
                startPosition,
                targetPosition,
                ArcApexHeight,
                progress
            );

            yield return null;
        }
        transform.position = targetPosition;

        CameraEffects.Instance?.CameraShakeClientRpc(0.4f, 0.07f);
        ImpactFxClientRpc(targetPosition, AreaRadius);
        ApplyLandingDamage(FindEnemiesInLandingRadius(targetPosition, AreaRadius));

        unitCollider.enabled = true;
        movement.moving = false;
        returnPositionOnInterrupt = null;

        yield return new WaitForSeconds(RecoverySeconds);

        GameLoop.Instance?.HoldReturnFireWindow();
        CompleteInterruptibleAbilityAction();
    }

    protected override void OnAbilityInterrupted()
    {
        if (returnPositionOnInterrupt is Vector3 startPosition)
            transform.position = startPosition;

        returnPositionOnInterrupt = null;

        Collider unitCollider = GetComponent<Collider>();
        if (unitCollider != null)
            unitCollider.enabled = true;

        Movement movement = GetComponent<Movement>();
        if (movement != null)
            movement.moving = false;
    }

    public List<GameObject> FindEnemiesInLandingRadius(Vector3 landingPosition, float areaRadius)
    {
        List<GameObject> found = new();
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        Physics.SyncTransforms();

        Collider[] enemiesInRange = Physics.OverlapSphere(
            landingPosition,
            areaRadius * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemy in enemiesInRange)
        {
            Vector3 directionToEnemy = (enemy.transform.position - landingPosition).normalized;
            float distanceToEnemy = Vector3.Distance(landingPosition, enemy.transform.position);
            if (
                Physics.Raycast(
                    landingPosition,
                    directionToEnemy,
                    out RaycastHit hit,
                    distanceToEnemy,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
            )
            {
                if (hit.collider != enemy)
                    continue;
            }

            found.Add(enemy.gameObject);
        }
        return found;
    }

    public void ApplyLandingDamage(List<GameObject> targets)
    {
        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;
            target.GetComponent<Health>()?.TakeDamage(damage);
        }
    }

    [ClientRpc]
    private void LaunchFxClientRpc(
        Vector3 landingPosition,
        float areaRadius,
        float flightSeconds
    )
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);

        AbilityWindup.Play(
            null,
            landingPosition,
            teamColor,
            flightSeconds,
            WindupShape.Disc,
            areaRadius * GameLoop.cellSize
        );

        ShatterLeapTrail.Attach(transform, teamColor, flightSeconds);
    }

    [ClientRpc]
    private void ImpactFxClientRpc(Vector3 landingPosition, float areaRadius)
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);
        float blast = areaRadius * GameLoop.cellSize;

        ImpactShockwave.Spawn(
            landingPosition,
            teamColor,
            blast,
            0.5f,
            withLightPop: true,
            groundDust: 0.7f
        );

        DebrisBurst.Spawn(landingPosition, teamColor, blast * 0.55f, 14);
    }
}

sealed class ShatterLeapTrail : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";
    private const string FallbackShaderName = "Sprites/Default";

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

    private const float TailSeconds = 0.24f;

    private const int MaxSamples = 64;

    private const float MinSampleSpacing = 0.05f;

    private const float HeadWidth = 0.42f;
    private const float TailWidth = 0.05f;

    private Transform rider;
    private Renderer riderRenderer;
    private LineRenderer line;
    private Material trailMaterial;
    private readonly List<Vector3> samples = new(MaxSamples);
    private readonly List<float> sampleTimes = new(MaxSamples);
    private float flightEndsAt;

    public static void Attach(Transform rider, Color color, float flightSeconds)
    {
        if (rider == null || flightSeconds <= 0f)
            return;

        GameObject host = new("ShatterLeapTrail");
        ShatterLeapTrail trail = host.AddComponent<ShatterLeapTrail>();
        trail.rider = rider;
        trail.flightEndsAt = Time.time + flightSeconds;
        trail.Build(color);
    }

    private void Build(Color color)
    {
        Shader trailShader = Shader.Find(BeamShaderName);
        if (trailShader == null)
            trailShader = Shader.Find(FallbackShaderName);
        if (trailShader == null)
        {
            Debug.LogWarning($"[ShatterLeapTrail] {BeamShaderName} not found; no trail will be drawn");
            Destroy(gameObject);
            return;
        }

        trailMaterial = new Material(trailShader)
        {
            name = "Shatter Leap Trail (Runtime)",
            hideFlags = HideFlags.DontSave,
        };
        if (trailMaterial.HasProperty(GlowColorId))
        {
            trailMaterial.SetColor(GlowColorId, AbilityJuice.Hot(color, 1.7f));
            trailMaterial.SetColor(CoreColorId, AbilityJuice.HotCore);
        }

        line = gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = trailMaterial;
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.widthMultiplier = HeadWidth;
        line.widthCurve = AnimationCurve.EaseInOut(0f, TailWidth / HeadWidth, 1f, 1f);
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        riderRenderer = rider.GetComponentInChildren<Renderer>(true);
    }

    private void LateUpdate()
    {
        if (line == null)
            return;

        float now = Time.time;
        bool flying = rider != null && now < flightEndsAt;

        if (flying)
        {
            Vector3 head = rider.position;
            if (
                samples.Count == 0
                || (samples[^1] - head).sqrMagnitude > MinSampleSpacing * MinSampleSpacing
            )
            {
                if (samples.Count >= MaxSamples)
                {
                    samples.RemoveAt(0);
                    sampleTimes.RemoveAt(0);
                }
                samples.Add(head);
                sampleTimes.Add(now);
            }
        }

        DropStaleSamples(now);

        if (!flying && samples.Count < 2)
        {
            Destroy(gameObject);
            return;
        }

        if (riderRenderer != null)
            line.forceRenderingOff = riderRenderer.forceRenderingOff;

        if (samples.Count < 2)
        {
            line.positionCount = 0;
            return;
        }

        line.positionCount = samples.Count;
        for (int index = 0; index < samples.Count; index++)
            line.SetPosition(index, samples[index]);

        float alpha = flying
            ? 1f
            : Mathf.Clamp01(1f - (now - flightEndsAt) / Mathf.Max(0.0001f, TailSeconds));
        line.startColor = new Color(1f, 1f, 1f, alpha * 0.12f);
        line.endColor = new Color(1f, 1f, 1f, alpha);
    }

    private void DropStaleSamples(float now)
    {
        int stale = 0;
        while (stale < sampleTimes.Count && now - sampleTimes[stale] > TailSeconds)
            stale++;

        if (stale <= 0)
            return;

        samples.RemoveRange(0, stale);
        sampleTimes.RemoveRange(0, stale);
    }

    private void OnDestroy()
    {
        if (trailMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(trailMaterial);
        else
            DestroyImmediate(trailMaterial);
        trailMaterial = null;
    }
}
