using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local-only dive-recovery presentation: the dodger's whole body pulses amber for as long as it is
/// on the floor, and the pulse quickens as it comes back up, so one effect says both "this unit
/// cannot shoot" and "it is nearly done". Driven from the replicated <see cref="DiveRecoveryState"/>,
/// so a peer that fog reveals midway through joins the pulse at the right rate.
///
/// Drawn on the character's own renderers through MaterialPropertyBlocks — the route
/// <see cref="HitFlash"/> already takes — rather than on the floor, because the floor under a unit
/// is the one place it cannot be read: a unit stands on its own base plate, which covers it.
///
/// Each slot is tinted from its own base colour rather than having one assigned, so a pulsing unit
/// still reads as its team and its character instead of turning into an amber silhouette. Only
/// <c>_BaseColor</c> is driven: it round-trips exactly, where writing <c>_EmissionColor</c> does not
/// and left the emissive base plate visibly wrong at the bottom of every beat.
/// </summary>
public sealed class DiveRecoveryPulse : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Amber, matching the language the rest of the dodge already uses, and far enough from the
    // white of a hit flash and the teal of the speed boost to never be mistaken for either.
    private static readonly Color RecoveryColor = new(1f, 0.52f, 0.12f, 1f);

    // A slow first beat that tightens as the unit gets its feet back under it. Ending well short
    // of a strobe keeps this readable next to four other units doing their own thing.
    private const float DownPulsesPerSecond = 1.3f;
    private const float RecoveredPulsesPerSecond = 3.6f;

    // How far toward the recovery colour the body travels at the top of a pulse. Short of a full
    // replacement so the character stays recognisable underneath its own colour.
    private const float PeakTint = 0.72f;

    /// <summary>
    /// One material slot of the character. Renderers here are not one-material-each — the body mesh
    /// carries three — and a block set on the whole renderer would flatten every submesh to the
    /// first material's colour, so each slot is tinted from and restored to its own.
    /// </summary>
    private readonly struct BodySlot
    {
        public readonly Renderer Renderer;
        public readonly int MaterialIndex;
        public readonly Color BaseColor;

        public BodySlot(Renderer renderer, int materialIndex, Material material)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            BaseColor =
                material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;
        }
    }

    private BodySlot[] bodySlots;
    private MaterialPropertyBlock propertyBlock;
    private DiveRecoveryState recovery;
    private float phase;

    public bool IsPulsing => recovery.Active;

    /// <summary>Convenience for units that have never dived before, mirroring HitFlash.FlashTarget.</summary>
    public static DiveRecoveryPulse Attach(GameObject unit)
    {
        if (unit == null)
            return null;
        if (!unit.TryGetComponent(out DiveRecoveryPulse pulse))
            pulse = unit.AddComponent<DiveRecoveryPulse>();
        return pulse;
    }

    public void SetRecovery(DiveRecoveryState state)
    {
        bool wasPulsing = recovery.Active;
        recovery = state;

        if (!state.Active)
        {
            if (wasPulsing)
                Clear();
            return;
        }

        if (!wasPulsing)
        {
            // Open at the top of the pulse rather than the bottom: the unit has to light up on the
            // frame it hits the floor, not a half-beat later.
            phase = 0.5f;
        }

        EnsureBodyRenderers();
        Draw();
    }

    private void Update()
    {
        if (!recovery.Active)
            return;

        phase += Time.deltaTime * Mathf.Lerp(
            DownPulsesPerSecond,
            RecoveredPulsesPerSecond,
            ResolveProgress()
        );
        Draw();
    }

    private void OnDisable()
    {
        recovery = default;
        Clear();
    }

    private float ResolveProgress()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? recovery.ProgressAt(manager.ServerTime.Time) : 0f;
    }

    private void Draw()
    {
        if (bodySlots == null)
            return;

        float tint = PeakTint * (0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI));

        foreach (BodySlot slot in bodySlots)
        {
            if (slot.Renderer == null)
                continue;

            slot.Renderer.GetPropertyBlock(propertyBlock, slot.MaterialIndex);
            propertyBlock.SetColor(BaseColorId, Color.Lerp(slot.BaseColor, RecoveryColor, tint));
            slot.Renderer.SetPropertyBlock(propertyBlock, slot.MaterialIndex);
        }
    }

    private void Clear()
    {
        if (bodySlots == null)
            return;

        foreach (BodySlot slot in bodySlots)
        {
            if (slot.Renderer != null)
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
        }
    }

    private void EnsureBodyRenderers()
    {
        if (bodySlots != null)
            return;

        List<BodySlot> slots = new();
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (!IsCharacterRenderer(candidate))
                continue;

            Material[] materials = candidate.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                slots.Add(new BodySlot(candidate, index, materials[index]));
        }

        bodySlots = slots.ToArray();
        propertyBlock ??= new MaterialPropertyBlock();
    }

    /// <summary>
    /// The character itself, not the effects parked on it. The runtime visuals a unit carries — the
    /// ground rings, the vision cone, the target laser — drive their own property blocks on
    /// BattlePlan/* shaders, and clearing the tint would take their state with it.
    /// </summary>
    public static bool IsCharacterRenderer(Renderer candidate)
    {
        if (candidate == null || candidate is LineRenderer)
            return false;

        Material material = candidate.sharedMaterial;
        if (material == null || material.shader == null)
            return false;

        return !material.shader.name.StartsWith(
            "BattlePlan/",
            System.StringComparison.Ordinal
        );
    }
}
