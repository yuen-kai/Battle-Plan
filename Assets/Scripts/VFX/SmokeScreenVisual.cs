using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The visible body of a deployed smoke screen: a bank of drifting, camera-facing puffs standing
/// over the cells it covers, on a ground stain that keeps the exact footprint readable from
/// straight above. Purely local and non-interactive — the server owns which cells are smoked and
/// what they block, and this only has to make them impossible to miss. A screen drawn as a faint
/// tint on the floor gets walked into, because nothing about it says the sightline is gone.
/// </summary>
public sealed class SmokeScreenVisual : MonoBehaviour
{
    // Three layers per cell. Inside a 3x3 screen the puffs of neighbouring cells pile up into
    // something close to solid, while the outermost ring keeps a soft edge that reads as smoke
    // rather than as a box.
    private const int PuffsPerCell = 3;
    private const float LowestPuffHeight = 0.45f;
    private const float PuffHeightStep = 0.55f;
    private const float PuffJitterCells = 0.22f;
    private const float MinPuffCells = 1.05f;
    private const float MaxPuffCells = 1.4f;

    // Clear of the move-range overlay's outline quad at 0.227 and below the ability target markers
    // at 0.26, so the stain neither z-fights the board nor buries a marker.
    private const float StainHeight = 0.24f;
    private const float StainCells = 0.98f;

    private static readonly Color StainColor = new(0.62f, 0.68f, 0.72f, 0.55f);
    private static readonly Color PuffColor = new(0.80f, 0.84f, 0.87f, 0.45f);

    // A bare quad is a hard-edged pane, and a bank of them reads as stacked glass rather than as
    // smoke. The puffs are drawn through a radial falloff so each one dissolves at its rim and only
    // the overlap between them builds up into something solid.
    private const int PuffTextureSize = 64;

    // Enough motion to read as a cloud holding its ground, not as drifting away from the cells it
    // is denying — the footprint is exact and the visual may not suggest otherwise.
    private const float RollDegreesPerSecond = 9f;
    private const float BobHeight = 0.12f;
    private const float BobSpeed = 0.55f;

    private readonly List<Puff> puffs = new();
    private Material stainMaterial;
    private Material puffMaterial;
    private Texture2D puffTexture;

    private struct Puff
    {
        public Transform Transform;
        public Vector3 Anchor;
        public float Phase;
        public float Roll;
    }

    public static SmokeScreenVisual Create(
        Transform parent,
        IReadOnlyList<Vector3> cellWorldPositions,
        float cellSize
    )
    {
        GameObject root = new("SmokeScreenVisuals");
        root.transform.SetParent(parent, true);
        SmokeScreenVisual visual = root.AddComponent<SmokeScreenVisual>();
        visual.Build(cellWorldPositions, cellSize);
        return visual;
    }

    private void Build(IReadOnlyList<Vector3> cellWorldPositions, float cellSize)
    {
        Shader shader = Shader.Find("Sprites/Default");
        stainMaterial = new Material(shader) { name = "Smoke Stain (Runtime)", color = StainColor };
        puffTexture = CreateSoftPuffTexture();
        puffMaterial = new Material(shader)
        {
            name = "Smoke Puff (Runtime)",
            color = PuffColor,
            mainTexture = puffTexture,
        };
        if (cellWorldPositions == null)
            return;

        foreach (Vector3 cellWorldPosition in cellWorldPositions)
        {
            CreateQuad(
                "SmokeStain",
                cellWorldPosition + Vector3.up * StainHeight,
                Quaternion.Euler(90f, 0f, 0f),
                cellSize * StainCells,
                stainMaterial
            );

            // A second canister landing elsewhere rebuilds the whole screen, so the jitter is
            // seeded from the cell: a screen already standing redraws identically instead of
            // reshuffling itself the moment its neighbour deploys.
            Vector2Int cell = GridSystem.ConvertToGridCoords(cellWorldPosition);
            Random.State callerState = Random.state;
            Random.InitState(cell.x * 73856093 ^ cell.y * 19349663);
            for (int layer = 0; layer < PuffsPerCell; layer++)
                CreatePuff(cellWorldPosition, cellSize, layer);
            Random.state = callerState;
        }
    }

    private static Texture2D CreateSoftPuffTexture()
    {
        Texture2D texture = new(PuffTextureSize, PuffTextureSize, TextureFormat.RGBA32, false)
        {
            name = "Smoke Puff Falloff (Runtime)",
            wrapMode = TextureWrapMode.Clamp,
        };

        Color[] pixels = new Color[PuffTextureSize * PuffTextureSize];
        for (int y = 0; y < PuffTextureSize; y++)
        {
            for (int x = 0; x < PuffTextureSize; x++)
            {
                Vector2 fromCentre = new(
                    (x + 0.5f) / PuffTextureSize - 0.5f,
                    (y + 0.5f) / PuffTextureSize - 0.5f
                );
                // Solid core out to nearly half the quad, then a dissolving rim. A plain radial
                // gradient loses so much of its area to the falloff that the bank thins back into
                // the haze this replaced; the screen has to look like it stops a shot.
                float alpha =
                    1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.24f, 0.5f, fromCentre.magnitude));
                pixels[y * PuffTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private void CreatePuff(Vector3 cellWorldPosition, float cellSize, int layer)
    {
        Vector3 anchor =
            cellWorldPosition
            + new Vector3(
                Random.Range(-PuffJitterCells, PuffJitterCells) * cellSize,
                LowestPuffHeight + layer * PuffHeightStep,
                Random.Range(-PuffJitterCells, PuffJitterCells) * cellSize
            );

        Transform puff = CreateQuad(
            "SmokePuff",
            anchor,
            Quaternion.identity,
            cellSize * Random.Range(MinPuffCells, MaxPuffCells),
            puffMaterial
        );

        puffs.Add(
            new Puff
            {
                Transform = puff,
                Anchor = anchor,
                Phase = Random.value * Mathf.PI * 2f,
                Roll = Random.value * 360f,
            }
        );
    }

    private Transform CreateQuad(
        string name,
        Vector3 position,
        Quaternion rotation,
        float size,
        Material material
    )
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.layer = 0;
        quad.transform.SetParent(transform, true);
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = Vector3.one * size;

        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
        {
            // Board picks raycast the Grid layer, but a live collider here would still swallow
            // clicks meant for the cells underneath.
            quadCollider.enabled = false;
            Destroy(quadCollider);
        }

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        return quad.transform;
    }

    /// <summary>
    /// Turns every puff to face the camera and drifts it. Billboarding is what gives the bank its
    /// mass from the tactical angle: flat-lying quads would collapse into the floor stain and leave
    /// the screen looking like the tint it used to be.
    /// </summary>
    private void LateUpdate()
    {
        Camera viewCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (viewCamera == null)
            viewCamera = Camera.main;
        if (viewCamera == null)
            return;

        Quaternion facing = viewCamera.transform.rotation;
        float time = Time.time;
        foreach (Puff puff in puffs)
        {
            if (puff.Transform == null)
                continue;

            puff.Transform.SetPositionAndRotation(
                puff.Anchor + Vector3.up * (Mathf.Sin(time * BobSpeed + puff.Phase) * BobHeight),
                facing * Quaternion.Euler(0f, 0f, puff.Roll + time * RollDegreesPerSecond)
            );
        }
    }

    private void OnDestroy()
    {
        if (stainMaterial != null)
            Destroy(stainMaterial);
        if (puffMaterial != null)
            Destroy(puffMaterial);
        if (puffTexture != null)
            Destroy(puffTexture);
    }
}
