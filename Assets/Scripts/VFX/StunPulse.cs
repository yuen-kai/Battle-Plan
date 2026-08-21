using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local-only stun presentation: the unit's whole body pulses for as long as ApplyStun holds it.
/// Mirrors DiveRecoveryPulse's body tint, but for the shorter, externally-applied interrupt a
/// knockback (or any future hard-CC) leaves behind rather than a unit picking itself back up.
/// Driven from the replicated StunState, so a peer that fog reveals mid-stun still joins the pulse
/// at the right point instead of a half-beat late.
///
/// Drawn on the character's own renderers through MaterialPropertyBlocks — the route
/// DiveRecoveryPulse and HitFlash already take — rather than on the floor, because the floor under
/// a unit is the one place it cannot be read: a unit stands on its own base plate, which covers it.
/// </summary>
public sealed class StunPulse : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Electric violet: distinct from dive recovery's amber, hit flash's white and Shield Rush's
    // teal, so a stunned unit never reads as any of the other three.
    private static readonly Color StunColor = new(0.62f, 0.22f, 1f, 1f);

    // A quick beat throughout, tightening slightly as the window closes — the same shape
    // DiveRecoveryPulse uses, scaled up because this whole effect is over inside a second.
    private const float StartPulsesPerSecond = 4f;
    private const float EndPulsesPerSecond = 9f;

    // How far toward the stun colour the body travels at the top of a pulse. Short of a full
    // replacement so the character stays recognisable underneath its own colour.
    private const float PeakTint = 0.72f;

    /// <summary>
    /// One material slot of the character. Renderers here are not one-material-each — the body
    /// mesh carries three — and a block set on the whole renderer would flatten every submesh to
    /// the first material's colour, so each slot is tinted from and restored to its own.
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
    private StunState stun;
    private float phase;

    public bool IsPulsing => stun.Active;

    /// <summary>Convenience for units that have never been stunned before, mirroring HitFlash.FlashTarget.</summary>
    public static StunPulse Attach(GameObject unit)
    {
        if (unit == null)
            return null;
        if (!unit.TryGetComponent(out StunPulse pulse))
            pulse = unit.AddComponent<StunPulse>();
        return pulse;
    }

    public void SetStun(StunState state)
    {
        bool wasPulsing = stun.Active;
        stun = state;

        if (!state.Active)
        {
            if (wasPulsing)
                Clear();
            return;
        }

        if (!wasPulsing)
        {
            // Open at the top of the pulse rather than the bottom: the unit has to light up on the
            // frame it is stunned, not a half-beat later.
            phase = 0.5f;
        }

        EnsureBodyRenderers();
        Draw();
    }

    private void Update()
    {
        if (!stun.Active)
            return;

        phase += Time.deltaTime * Mathf.Lerp(StartPulsesPerSecond, EndPulsesPerSecond, ResolveProgress());
        Draw();
    }

    private void OnDisable()
    {
        stun = default;
        Clear();
    }

    private float ResolveProgress()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? stun.ProgressAt(manager.ServerTime.Time) : 0f;
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
            propertyBlock.SetColor(BaseColorId, Color.Lerp(slot.BaseColor, StunColor, tint));
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
            if (!DiveRecoveryPulse.IsCharacterRenderer(candidate))
                continue;

            Material[] materials = candidate.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
                slots.Add(new BodySlot(candidate, index, materials[index]));
        }

        bodySlots = slots.ToArray();
        propertyBlock ??= new MaterialPropertyBlock();
    }
}
