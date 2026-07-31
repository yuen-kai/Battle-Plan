using UnityEngine;

/// <summary>
/// Bullet Echo-style weapon flashlight: a procedural fan mesh projected on the ground in front
/// of a unit and clipped by walls. Its full angle represents weapon spread and its length
/// represents attack sight range. Fog visibility remains a separate mechanic.
///
/// Mesh UVs match BattlePlan/VisionCone: uv.x = angular position (0..1), uv.y = distance (0..1).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VisionConeVisual : MonoBehaviour
{
    [Header("Cone shape")]
    [Tooltip("Full flashlight angle in degrees (twice UnitData.bulletSpread).")]
    public float viewAngle = 70f;

    [Tooltip("Attack sight range in world units (UnitData.targetRange times cell size).")]
    public float viewDistance = 12f;

    [Tooltip("Rays across the cone. More = smoother wall clipping.")]
    public int rayCount = 40;

    [Tooltip("Height above the floor to avoid z-fighting; keep above fog overlay quads.")]
    public float groundOffset = 0.06f;

    [Header("Look")]
    public Color coneColor = new(0.55f, 0.8f, 1f, 0.28f);

    [Tooltip("Optional material using BattlePlan/VisionCone; auto-created if empty.")]
    public Material coneMaterial;

    [Tooltip("Layers that block vision (default: Walls).")]
    public LayerMask blockingLayers;

    Mesh coneMesh;
    MeshRenderer meshRenderer;
    Vector3[] vertices;
    Vector2[] uvs;
    int[] triangles;

    // Pose and envelope the current mesh was cast for. Walls are the only thing that clips the fan
    // and they are spawned once during map setup, so a unit that has not moved or turned produces
    // an identical mesh every frame. Skipping those rebuilds keeps a still board free of raycasts,
    // which matters most on WebGL where rendering and the transport share one thread.
    Vector3 builtPosition;
    Quaternion builtRotation;
    float builtAngle;
    float builtDistance;
    bool hasBuilt;
    bool trianglesUploaded;

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (blockingLayers == 0)
            blockingLayers = LayerMask.GetMask("Walls");

        if (coneMaterial == null)
        {
            Shader coneShader = Shader.Find("BattlePlan/VisionCone");
            if (coneShader == null)
            {
                Debug.LogWarning("[VisionConeVisual] BattlePlan/VisionCone shader not found, disabling cone");
                enabled = false;
                return;
            }
            coneMaterial = new Material(coneShader);
        }
        meshRenderer.material = coneMaterial;
        meshRenderer.material.SetColor("_ConeColor", coneColor);
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        coneMesh = new Mesh { name = "VisionCone" };
        coneMesh.MarkDynamic();
        GetComponent<MeshFilter>().mesh = coneMesh;

        AllocateBuffers();
    }

    void AllocateBuffers()
    {
        int vertexCount = rayCount + 2; // apex + fan edge
        vertices = new Vector3[vertexCount];
        uvs = new Vector2[vertexCount];
        triangles = new int[rayCount * 3];
        for (int i = 0; i < rayCount; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
    }

    void LateUpdate()
    {
        if (
            hasBuilt
            && transform.position == builtPosition
            && transform.rotation == builtRotation
            && viewAngle == builtAngle
            && viewDistance == builtDistance
        )
        {
            return;
        }

        RebuildMesh();

        builtPosition = transform.position;
        builtRotation = transform.rotation;
        builtAngle = viewAngle;
        builtDistance = viewDistance;
        hasBuilt = true;
    }

    void RebuildMesh()
    {
        // Cast the fan in world space from the unit's position along its facing.
        Vector3 origin = transform.position;
        origin.y = groundOffset;

        vertices[0] = transform.InverseTransformPoint(origin);
        uvs[0] = new Vector2(0.5f, 0f);

        float halfAngle = viewAngle * 0.5f;
        for (int i = 0; i <= rayCount; i++)
        {
            float t = (float)i / rayCount;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * FlatForward();

            float distance = viewDistance;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, viewDistance, blockingLayers))
            {
                distance = hit.distance;
            }

            Vector3 worldPoint = origin + direction * distance;
            worldPoint.y = groundOffset;
            vertices[i + 1] = transform.InverseTransformPoint(worldPoint);
            uvs[i + 1] = new Vector2(t, distance / viewDistance);
        }

        coneMesh.vertices = vertices;
        coneMesh.uv = uvs;

        // Topology is fixed once AllocateBuffers has run, and the vertex count never changes with
        // it, so the index buffer needs a single upload rather than one per rebuild.
        if (!trianglesUploaded)
        {
            coneMesh.triangles = triangles;
            trianglesUploaded = true;
        }

        coneMesh.RecalculateBounds();
    }

    Vector3 FlatForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
    }

    public void SetColor(Color color)
    {
        coneColor = color;
        if (meshRenderer != null)
            meshRenderer.material.SetColor("_ConeColor", color);
    }

    /// <summary>
    /// Configures the visual weapon envelope. UnitData.bulletSpread is the maximum deviation on
    /// either side of center, so the rendered fan uses twice that value as its full angle.
    /// </summary>
    public void SetWeaponEnvelope(float halfSpreadDegrees, float sightRangeWorld)
    {
        viewAngle = Mathf.Clamp(halfSpreadDegrees * 2f, 0f, 360f);
        viewDistance = Mathf.Max(0.01f, sightRangeWorld);
    }
}
