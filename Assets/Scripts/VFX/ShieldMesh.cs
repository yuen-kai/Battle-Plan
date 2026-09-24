using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rounded slab a raised shield is made of, replacing the stock cube the prefabs carry.
///
/// A shield is the one piece of geometry on the board that a player looks straight at for four and
/// a half seconds at a time, and a sharp-cornered box at that size reads as a placeholder however
/// it is shaded. What this builds instead is the box swept by an ellipsoid: the four corners of the
/// face turn over on a circular arc and the thin axis rounds off completely, so the slab has a
/// silhouette that catches the rim light all the way round rather than four hard points.
///
/// Corner radii are chosen in WORLD metres and converted back through the shield's own scale,
/// because that scale is wildly non-uniform — two-and-a-half cells wide, a body tall, a hand thick,
/// and wider again once <see cref="ShieldRush.TryExpandShieldFootprint"/> has run. A single radius in
/// local units would come out as three different roundovers in frame.
///
/// The collider is left alone. A rounded mesh inside the same box is a picture change and nothing
/// else: line of fire, blocked damage and the dodge windows all keep answering exactly as before.
/// </summary>
public static class ShieldMesh
{
    public const string GeneratedMeshName = "ShieldSlab (Rounded)";

    private const string StockMeshName = "Cube";

    // Of the slab's shorter face axis, which is its height. Enough that the turn is unmistakable at
    // the distance the camera holds, short of the point where the face starts reading as an oval.
    private const float CornerShare = 0.16f;

    private const int RoundSegments = 4;

    private static readonly int[] FaceAxis = { 0, 0, 1, 1, 2, 2 };
    private static readonly float[] FaceSign = { 1f, -1f, 1f, -1f, 1f, -1f };

    // Tangent pairs chosen so cross(u, v) points out of the face, which is the winding Unity reads
    // as front-facing.
    private static readonly int[] UAxis = { 1, 2, 2, 0, 0, 1 };
    private static readonly int[] VAxis = { 2, 1, 0, 2, 1, 0 };

    private static readonly Dictionary<Vector3Int, Mesh> Cache = new();

    /// <summary>
    /// Swaps <paramref name="shield"/>'s stock cube for a slab rounded to its current world scale.
    /// Idempotent and cheap to call again: meshes are cached per scale and shared between every
    /// shield of the same size. Call it after any change to the shield's scale, never before —
    /// the roundover is sized from that scale.
    /// </summary>
    public static bool Apply(Transform shield)
    {
        if (shield == null || !shield.TryGetComponent(out MeshFilter filter))
            return false;

        Mesh current = filter.sharedMesh;
        if (current != null && current.name != StockMeshName && current.name != GeneratedMeshName)
            return false;

        Vector3 scale = shield.lossyScale;
        if (scale.x <= 0.0001f || scale.y <= 0.0001f || scale.z <= 0.0001f)
            return false;

        Vector3Int key = new(
            Mathf.RoundToInt(scale.x * 200f),
            Mathf.RoundToInt(scale.y * 200f),
            Mathf.RoundToInt(scale.z * 200f)
        );
        if (!Cache.TryGetValue(key, out Mesh rounded) || rounded == null)
        {
            rounded = Build(scale);
            Cache[key] = rounded;
        }

        if (filter.sharedMesh == rounded)
            return false;

        filter.sharedMesh = rounded;
        return true;
    }

    private static Mesh Build(Vector3 scale)
    {
        Vector3 half = scale * 0.5f;

        // One radius for both face axes so the corners are circular arcs rather than ellipses, and
        // the whole half-thickness on the thin axis so the edge comes off as a full roundover.
        float corner = Mathf.Min(
            CornerShare * Mathf.Min(scale.x, scale.y),
            Mathf.Min(half.x, half.y)
        );
        Vector3 radius = new(corner, corner, half.z);

        Vector3 inner = new(
            Mathf.Max(half.x / radius.x - 1f, 0f),
            Mathf.Max(half.y / radius.y - 1f, 0f),
            Mathf.Max(half.z / radius.z - 1f, 0f)
        );

        float[][] samples =
        {
            AxisSamples(inner.x),
            AxisSamples(inner.y),
            AxisSamples(inner.z),
        };

        List<Vector3> vertices = new();
        List<Vector3> normals = new();
        List<Vector2> uvs = new();
        List<int> triangles = new();

        for (int face = 0; face < 6; face++)
        {
            int fixedAxis = FaceAxis[face];
            int acrossAxis = UAxis[face];
            int alongAxis = VAxis[face];
            float[] across = samples[acrossAxis];
            float[] along = samples[alongAxis];
            int start = vertices.Count;

            for (int j = 0; j < along.Length; j++)
            {
                for (int i = 0; i < across.Length; i++)
                {
                    Vector3 sample = Vector3.zero;
                    sample[fixedAxis] = FaceSign[face] * (inner[fixedAxis] + 1f);
                    sample[acrossAxis] = across[i];
                    sample[alongAxis] = along[j];

                    Vector3 core = new(
                        Mathf.Clamp(sample.x, -inner.x, inner.x),
                        Mathf.Clamp(sample.y, -inner.y, inner.y),
                        Mathf.Clamp(sample.z, -inner.z, inner.z)
                    );
                    Vector3 outward = (sample - core).normalized;

                    // Built in the space where the sweeping ellipsoid is a unit sphere, then
                    // carried back out through the radii and down through the shield's own scale.
                    // Normals take the inverse of each step, which is why they are multiplied by
                    // the scale the positions are divided by.
                    Vector3 point = Vector3.Scale(core + outward, radius);
                    vertices.Add(
                        new Vector3(point.x / scale.x, point.y / scale.y, point.z / scale.z)
                    );
                    normals.Add(
                        new Vector3(
                            outward.x * scale.x / radius.x,
                            outward.y * scale.y / radius.y,
                            outward.z * scale.z / radius.z
                        ).normalized
                    );
                    uvs.Add(new Vector2(point.x / scale.x + 0.5f, point.y / scale.y + 0.5f));
                }
            }

            for (int j = 0; j < along.Length - 1; j++)
            {
                for (int i = 0; i < across.Length - 1; i++)
                {
                    int corner00 = start + j * across.Length + i;
                    int corner10 = corner00 + 1;
                    int corner01 = corner00 + across.Length;
                    int corner11 = corner01 + 1;

                    triangles.Add(corner00);
                    triangles.Add(corner10);
                    triangles.Add(corner11);
                    triangles.Add(corner00);
                    triangles.Add(corner11);
                    triangles.Add(corner01);
                }
            }
        }

        Mesh mesh = new()
        {
            name = GeneratedMeshName,
            hideFlags = HideFlags.DontSave,
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Where to sample one axis of the unit-sphere space: the flat middle gets three rows, and the
    /// roundover at each end gets <see cref="RoundSegments"/> spaced by sine so the arc is even in
    /// angle rather than in extent. Axes with no flat part left collapse to a single shared centre
    /// row instead of two coincident ones.
    /// </summary>
    private static float[] AxisSamples(float flatHalf)
    {
        bool hasFlat = flatHalf > 0.0001f;
        float[] samples = new float[2 * RoundSegments + (hasFlat ? 3 : 1)];

        int index = 0;
        for (int k = RoundSegments; k >= (hasFlat ? 0 : 1); k--)
            samples[index++] = -(flatHalf + Mathf.Sin(k * Mathf.PI * 0.5f / RoundSegments));
        if (hasFlat)
            samples[index++] = 0f;
        for (int k = 0; k <= RoundSegments; k++)
            samples[index++] = flatHalf + Mathf.Sin(k * Mathf.PI * 0.5f / RoundSegments);

        return samples;
    }
}
