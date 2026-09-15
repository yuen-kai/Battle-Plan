using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class SuppressingFire : Ability
{
    private const float WindUpSeconds = 0.8f;

    private const int BarrageShots = 40;
    private const float SecondsBetweenShots = 0.10f;

    private const float RecoverySeconds = 1.4f;

    /// <summary>Half-angle of the barrage fan, matching <see cref="Shooting.FireBullet"/> spread.</summary>
    public const float BarrageSpreadDegrees = 20f;

    private const float BarrageEdgeWeight = 0.3f;
    private const float BarrageCenterWeight = 0.4f;

    private const int ConePreviewSegments = 24;
    private const float ConePreviewHeight = 0.26f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || data == null)
            yield break;

        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        Vector2Int targetCell = GridSystem.ConvertToGridCoords(abilitySquare);
        if (
            !GridSystem.TryGetAdjacentDirection(casterCell, targetCell, out Vector2Int direction)
        )
        {
            Debug.LogWarning(
                $"[SuppressingFire] {name} could not resolve a barrage direction from "
                    + $"{abilitySquare} (caster at {casterCell}); aborting. Check this unit's "
                    + "UnitData has selectAbilityDirection and abilityFixedDistance set."
            );
            yield break;
        }

        Vector3 direction3D = new(direction.x, 0f, direction.y);

        movement.PauseMovement();
        BeginInterruptibleAbilityAction();

        yield return movement.RotateToFaceTarget(
            transform.position + direction3D * GameLoop.cellSize,
            data.rotationSpeed
        );

        yield return new WaitForSeconds(WindUpSeconds);

        BarrageFxClientRpc(transform.position, direction3D);

        SpreadZoneWeights barrageZones = new(
            BarrageEdgeWeight,
            BarrageCenterWeight,
            BarrageEdgeWeight
        );

        for (int shot = 0; shot < BarrageShots; shot++)
        {
            shooting.FireBullet(
                direction3D,
                BarrageSpreadDegrees,
                barrageZones,
                ignoreAdjacentWalls: true
            );

            yield return new WaitForSeconds(SecondsBetweenShots);
        }

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    /// <summary>
    /// Far tip of the barrage cone along the chosen aim, level with the caster — same envelope the
    /// planning preview and dodge telegraph draw. Range matches the unit's ordinary bullet reach.
    /// </summary>
    public static Vector3 ResolveConeAimPoint(GameObject unit, Vector3 selectedSquare)
    {
        if (unit == null)
            return selectedSquare;

        UnitData data = unit.GetComponent<Movement>()?.unitData;
        float rangeCells = data != null ? data.bulletRange : 0f;
        Vector3 origin = unit.transform.position;
        Vector3 aim = new(selectedSquare.x, origin.y, selectedSquare.z);
        Vector3 direction = aim - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f || rangeCells <= 0f)
            return origin;

        return origin + direction.normalized * (rangeCells * GameLoop.cellSize);
    }

    /// <summary>
    /// Draws the barrage envelope as a ground fan from <paramref name="origin"/> along
    /// <paramref name="flatDirection"/>. Shared by the planning preview and the dodge telegraph.
    /// </summary>
    public static GameObject CreateConePreview(
        Transform parent,
        Vector3 origin,
        Vector3 flatDirection,
        Color color,
        float rangeCells,
        string objectName = "AbilityPreviewCone",
        float halfSpreadDegrees = BarrageSpreadDegrees
    )
    {
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 1e-6f || rangeCells <= 0f)
            return null;

        flatDirection.Normalize();

        GameObject host = new(objectName);
        host.transform.SetParent(parent, false);
        host.transform.position = new Vector3(origin.x, ConePreviewHeight, origin.z);
        host.transform.rotation = Quaternion.LookRotation(flatDirection, Vector3.up);

        MeshFilter filter = host.AddComponent<MeshFilter>();
        MeshRenderer renderer = host.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Shader coneShader = Shader.Find("BattlePlan/VisionCone");
        Material material =
            coneShader != null
                ? new Material(coneShader)
                : new Material(Shader.Find("Sprites/Default"));
        Color fill = color;
        fill.a = Mathf.Clamp01(color.a * 0.45f);
        if (coneShader != null)
            material.SetColor("_ConeColor", fill);
        else
            material.color = fill;
        renderer.material = material;

        filter.sharedMesh = BuildConeMesh(halfSpreadDegrees, rangeCells);
        return host;
    }

    public static Mesh BuildConeMesh(float halfSpreadDegrees, float rangeCells)
    {
        float range = Mathf.Max(0.01f, rangeCells * GameLoop.cellSize);
        int segments = ConePreviewSegments;
        Vector3[] vertices = new Vector3[segments + 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0f);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(-halfSpreadDegrees, halfSpreadDegrees, t);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            vertices[i + 1] = direction * range;
            uvs[i + 1] = new Vector2(t, 1f);
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        Mesh mesh = new() { name = "SuppressingFireCone" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    [ClientRpc]
    private void BarrageFxClientRpc(Vector3 origin, Vector3 direction)
    {
        Color muzzle = AbilityJuice.Alarm;
        Vector3 muzzlePosition = origin + direction.normalized * (GameLoop.cellSize * 0.4f);

        ImpactCore.Spawn(muzzlePosition, muzzle, 1.1f, 1.4f);
        CameraEffects.Instance?.CameraShakeClientRpc(
            BarrageShots * SecondsBetweenShots,
            0.02f
        );
    }
}
