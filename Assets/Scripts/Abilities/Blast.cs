using System.Collections.Generic;
using UnityEngine;

public static class Blast
{
    private const float ShelterSurfaceMargin = 0.05f;

    public static void Explode(
        Vector3 origin,
        float worldRadius,
        string damageableTeam,
        int shieldMask,
        float damage,
        System.Action fx,
        bool destroysWalls = false,
        bool authoritative = true,
        HashSet<GameObject> alreadyHit = null
    )
    {
        fx?.Invoke();
        if (!authoritative)
            return;

        foreach (
            GameObject target in FindTargets(
                origin,
                worldRadius,
                damageableTeam,
                shieldMask,
                alreadyHit
            )
        )
        {
            target.GetComponent<Health>()?.TakeDamage(damage);
        }

        if (destroysWalls)
            DestroyWalls(origin, worldRadius, shieldMask);
    }

    public static List<GameObject> FindTargets(
        Vector3 origin,
        float worldRadius,
        string damageableTeam,
        int shieldMask,
        HashSet<GameObject> alreadyHit = null
    )
    {
        List<GameObject> found = new();
        if (worldRadius <= 0f || string.IsNullOrEmpty(damageableTeam))
            return found;

        Physics.SyncTransforms();

        int targetMask = LayerMask.GetMask(damageableTeam);
        int blockingMask = LayerMask.GetMask("Walls", damageableTeam) | shieldMask;

        foreach (Collider target in Physics.OverlapSphere(origin, worldRadius, targetMask))
        {
            if (alreadyHit != null && !alreadyHit.Add(target.gameObject))
                continue;

            Vector3 toTarget = target.transform.position - origin;
            if (
                Physics.Raycast(
                    origin,
                    toTarget.normalized,
                    out RaycastHit hit,
                    toTarget.magnitude,
                    blockingMask
                )
                && hit.collider != target
            )
            {
                ShieldRush.ReportBlockedDamage(hit.collider, hit.point);
                continue;
            }

            found.Add(target.gameObject);
        }
        return found;
    }

    private static void DestroyWalls(Vector3 origin, float worldRadius, int shieldMask)
    {
        float cellSize = GameLoop.cellSize;
        if (GameLoop.Instance == null || cellSize <= 0.01f)
            return;

        foreach (
            Vector2Int cell in GetWallCellsWithinRadius(
                GridSystem.ConvertToGridCoords(origin),
                worldRadius / cellSize,
                GameLoop.wallLayout
            )
        )
        {
            if (!IsWallShelteredFrom(cell, origin, shieldMask))
                GameLoop.Instance.TryDestroyWallCell(cell);
        }
    }

    private static bool IsWallShelteredFrom(Vector2Int cell, Vector3 origin, int shieldMask)
    {
        if (
            shieldMask == 0
            || !GameLoop.Instance.TryGetWallInstance(cell, out GameObject wall)
        )
        {
            return false;
        }

        Collider face = wall.GetComponentInChildren<Collider>();
        if (face == null)
            return false;

        Vector3 fromWall = face.bounds.center;
        Vector3 towardBlast = origin - fromWall;
        float reach = towardBlast.magnitude - ShelterSurfaceMargin;
        return reach > 0f
            && Physics.Raycast(
                fromWall,
                towardBlast.normalized,
                reach,
                shieldMask,
                QueryTriggerInteraction.Ignore
            );
    }

    public static List<Vector2Int> GetWallCellsWithinRadius(
        Vector2Int center,
        float radiusCells,
        IEnumerable<Vector2Int> wallCells
    )
    {
        List<Vector2Int> hits = new();
        if (wallCells == null || radiusCells < 0f)
            return hits;

        float radiusSquared = radiusCells * radiusCells;
        int scanExtent = Mathf.CeilToInt(radiusCells);

        foreach (Vector2Int cell in wallCells)
        {
            int dx = cell.x - center.x;
            int dy = cell.y - center.y;
            if (Mathf.Abs(dx) > scanExtent || Mathf.Abs(dy) > scanExtent)
                continue;
            if (dx * dx + dy * dy <= radiusSquared)
                hits.Add(cell);
        }
        return hits;
    }
}
