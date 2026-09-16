using UnityEngine;

/// <summary>
/// What a raised shield slab takes away from an explosion's picture of itself.
///
/// A blast's flat layers — the dust front, the burning rim, the flash, the burn left in the deck —
/// are quads centred on the blast, and its thrown material is aimed outward from the same point.
/// None of them know that a slab is standing in the way, so a grenade landing beside a Sentinel
/// used to paint its whole ring straight through the shield. What a slab actually removes is the
/// ground past its own plane inside the angle its width subtends from the blast, plus every piece
/// of matter whose flight would have to cross it.
///
/// Resolved locally on every peer from the shield colliders themselves rather than replicated: the
/// slabs are active on all peers (see <see cref="ShieldStance"/>), so each screen can answer this
/// for itself and no explosion has to carry occlusion over the wire.
///
/// Slabs occlude whoever they belong to. A shield is a physical object in the middle of a blast,
/// and the team that raised it is a damage question, not a line-of-sight one.
/// </summary>
public readonly struct BlastShadow
{
    private const int MaxSlabs = 2;
    private const float FeatherCells = 0.12f;

    private static readonly int BlastShadowId = Shader.PropertyToID("_BlastShadow");
    private static readonly int[] SlabIds =
    {
        Shader.PropertyToID("_BlastSlab0"),
        Shader.PropertyToID("_BlastSlab1"),
    };
    private static readonly int[] SlabEdgeIds =
    {
        Shader.PropertyToID("_BlastSlabEdge0"),
        Shader.PropertyToID("_BlastSlabEdge1"),
    };

    private static readonly Collider[] Overlaps = new Collider[16];
    private static int shieldMask = -1;

    private readonly struct Slab
    {
        public readonly Vector2 Centre;
        public readonly Vector2 Normal;
        public readonly Vector2 Right;
        public readonly float HalfWidth;
        public readonly float Top;
        public readonly float Bottom;
        public readonly Collider Face;

        public Slab(
            Vector2 centre,
            Vector2 normal,
            Vector2 right,
            float halfWidth,
            float top,
            float bottom,
            Collider face
        )
        {
            Centre = centre;
            Normal = normal;
            Right = right;
            HalfWidth = halfWidth;
            Top = top;
            Bottom = bottom;
            Face = face;
        }
    }

    private readonly Vector2 origin;
    private readonly Slab first;
    private readonly Slab second;
    private readonly int count;
    private readonly float feather;

    private BlastShadow(Vector2 origin, Slab first, Slab second, int count, float feather)
    {
        this.origin = origin;
        this.first = first;
        this.second = second;
        this.count = count;
        this.feather = feather;
    }

    public bool Any => count > 0;

    /// <summary>Collision mask of both teams' slabs, for anything that wants to bounce off one.</summary>
    public static int ShieldMask
    {
        get
        {
            if (shieldMask < 0)
            {
                shieldMask =
                    LayerMask.GetMask(ShieldRush.BlueShieldLayerName, ShieldRush.RedShieldLayerName);
            }
            return shieldMask;
        }
    }

    /// <summary>
    /// The slabs standing within <paramref name="radius"/> of a blast at <paramref name="position"/>,
    /// nearest first. Two is the cap: a third slab close enough to matter has never been reachable
    /// on this board, and every layer of a burst pays for the ones it carries on every pixel.
    /// </summary>
    public static BlastShadow Collect(Vector3 position, float radius)
    {
        int mask = ShieldMask;
        Vector2 blastOrigin = new(position.x, position.z);
        float cell = GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
        float pad = FeatherCells * cell;
        if (mask == 0 || radius <= 0f)
            return new BlastShadow(blastOrigin, default, default, 0, pad);

        // Transforms drive shields and units during execution, so a slab raised or moved this frame
        // is only where it looks if the physics scene has been told about it first.
        Physics.SyncTransforms();
        int found = Physics.OverlapSphereNonAlloc(
            position,
            radius,
            Overlaps,
            mask,
            QueryTriggerInteraction.Ignore
        );

        Slab nearest = default;
        Slab runnerUp = default;
        float nearestDistance = float.MaxValue;
        float runnerUpDistance = float.MaxValue;
        int kept = 0;

        for (int index = 0; index < found; index++)
        {
            if (!TryBuildSlab(Overlaps[index], out Slab slab))
                continue;

            float distance = (slab.Centre - blastOrigin).sqrMagnitude;
            if (distance < nearestDistance)
            {
                runnerUp = nearest;
                runnerUpDistance = nearestDistance;
                nearest = slab;
                nearestDistance = distance;
            }
            else if (distance < runnerUpDistance)
            {
                runnerUp = slab;
                runnerUpDistance = distance;
            }
            kept = Mathf.Min(kept + 1, MaxSlabs);
        }

        for (int index = 0; index < found; index++)
            Overlaps[index] = null;

        return new BlastShadow(blastOrigin, nearest, runnerUp, kept, pad);
    }

    private static bool TryBuildSlab(Collider collider, out Slab slab)
    {
        slab = default;
        if (collider is not BoxCollider box || !collider.gameObject.activeInHierarchy)
            return false;

        Transform body = box.transform;
        Vector3 scale = body.lossyScale;
        float width = Mathf.Abs(box.size.x * scale.x);
        if (width <= 0.01f)
            return false;

        Vector2 normal = new(body.forward.x, body.forward.z);
        Vector2 right = new(body.right.x, body.right.z);
        if (normal.sqrMagnitude < 1e-4f || right.sqrMagnitude < 1e-4f)
            return false;

        Vector3 centre = body.TransformPoint(box.center);
        float halfHeight = Mathf.Abs(box.size.y * scale.y) * 0.5f;
        slab = new Slab(
            new Vector2(centre.x, centre.z),
            normal.normalized,
            right.normalized,
            width * 0.5f,
            centre.y + halfHeight,
            centre.y - halfHeight,
            collider
        );
        return true;
    }

    /// <summary>Hands the wedge to a flat blast layer. See <c>BP_BlastShadow.hlsl</c>.</summary>
    public void Apply(Material material)
    {
        if (material == null)
            return;

        material.SetVector(BlastShadowId, new Vector4(origin.x, origin.y, count, feather));
        WriteSlab(material, 0, first);
        WriteSlab(material, 1, second);
    }

    private static void WriteSlab(Material material, int index, Slab slab)
    {
        material.SetVector(
            SlabIds[index],
            new Vector4(slab.Centre.x, slab.Centre.y, slab.Normal.x, slab.Normal.y)
        );
        // The top edge goes over with the width: the shader needs it to tell ground the slab hides
        // from the camera apart from ground far enough back to be seen over the top of it.
        material.SetVector(
            SlabEdgeIds[index],
            new Vector4(slab.Right.x, slab.Right.y, slab.HalfWidth, slab.Top)
        );
    }

    /// <summary>
    /// Sets every slab standing in this blast reacting, at the point on its face the blast actually
    /// reached. A barrier that silently subtracts half an explosion does not read as a barrier; one
    /// that lights up where the blast washed over it does.
    ///
    /// Local and unreplicated, because the burst that calls it is already running on every peer.
    /// <see cref="ShieldBlockPulse"/> folds a wave into one already running at the same spot, so a
    /// blast that also blocked damage — which reports over the wire — still reads as a single hit.
    /// </summary>
    public void Strike()
    {
        if (count == 0)
            return;

        Strike(first);
        if (count > 1)
            Strike(second);
    }

    private void Strike(Slab slab)
    {
        if (slab.Face == null || !ShieldRush.TryGetShieldOwner(slab.Face, out GameObject owner))
            return;

        // Nearest point on the slab's own face to the blast, clamped to its width, and low on it:
        // the charge comes off the deck, so it arrives near the foot rather than at chest height.
        float lateral = Mathf.Clamp(
            Vector2.Dot(origin - slab.Centre, slab.Right),
            -slab.HalfWidth,
            slab.HalfWidth
        );
        Vector2 contact = slab.Centre + slab.Right * lateral;
        ShieldBlockPulse.Play(
            owner,
            new Vector3(contact.x, Mathf.Lerp(slab.Bottom, slab.Top, 0.3f), contact.y)
        );
    }

    /// <summary>
    /// Whether a slab stands between the blast and <paramref name="point"/>. For the burst's
    /// discrete matter — thrown chunks, dust lobes, burning ground — which is cheaper and reads
    /// better to place out of the shadow than to clip inside it.
    /// </summary>
    public bool IsOccluded(Vector3 point)
    {
        if (count == 0)
            return false;

        Vector2 ground = new(point.x, point.z);
        if (Crosses(first, ground, out _))
            return true;
        return count > 1 && Crosses(second, ground, out _);
    }

    /// <summary>
    /// How far out from the blast a piece thrown along <paramref name="direction"/> can get before
    /// a slab stops it, capped at <paramref name="distance"/>. Pieces bank up against the near face
    /// rather than vanishing, which is what says the shield took the hit.
    /// <paramref name="apexHeight"/> is the highest the piece gets on the way: anything that clears
    /// a slab's top edge is thrown over it and is not stopped by it, which is the difference between
    /// ground dust banking against the shield and hero chunks arcing across it.
    /// </summary>
    public float Reach(Vector3 direction, float distance, float apexHeight = 0f)
    {
        if (count == 0 || distance <= 0f)
            return distance;

        Vector2 target = origin + new Vector2(direction.x, direction.z).normalized * distance;
        float reach = distance;
        if (apexHeight <= first.Top && Crosses(first, target, out float firstTravel))
            reach = Mathf.Min(reach, firstTravel * distance);
        if (count > 1 && apexHeight <= second.Top && Crosses(second, target, out float secondTravel))
            reach = Mathf.Min(reach, secondTravel * distance);
        return reach;
    }

    /// <summary>
    /// Fraction of the way from the blast to <paramref name="ground"/> at which the ray meets
    /// <paramref name="slab"/> inside its width, or false when it never does.
    /// </summary>
    private bool Crosses(Slab slab, Vector2 ground, out float travel)
    {
        travel = 1f;
        Vector2 ray = ground - origin;
        float denominator = Vector2.Dot(slab.Normal, ray);
        if (Mathf.Abs(denominator) < 1e-5f)
            return false;

        travel = Vector2.Dot(slab.Normal, slab.Centre - origin) / denominator;
        if (travel <= 0f || travel >= 1f)
            return false;

        Vector2 crossing = origin + ray * travel;
        return Mathf.Abs(Vector2.Dot(crossing - slab.Centre, slab.Right)) <= slab.HalfWidth;
    }
}
