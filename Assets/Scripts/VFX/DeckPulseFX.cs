using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The deck pulse at the start of Execution (ArtDirection §9.4): the grid separator's emission
/// lifts from black to FX_RAIL × 0.5 and back over 0.45 s, so the board itself acknowledges that
/// the plan is now running. One coroutine, one property, no per-cell allocation.
///
/// The emission is pushed through MaterialPropertyBlocks, never the shared material asset, so
/// nothing here can dirty a .mat while the editor is in play mode.
///
/// This depends on whoever owns Assets/Materials/Map naming the grid separator material
/// Map_GridLine and leaving the _EMISSION keyword enabled on it. Until then it falls back to the
/// separator material that ships today, and if neither exists it is a silent no-op.
/// </summary>
public sealed class DeckPulseFX : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>Material names accepted as the grid separator, most preferred first.</summary>
    private static readonly string[] SeparatorMaterialNames = { "Map_GridLine", "GridCellOutline" };

    public const float PulseSeconds = 0.45f;
    public const float PulseStrength = 0.5f;

    private static DeckPulseFX instance;
    private static readonly List<Renderer> separators = new();
    private static bool searched;

    private MaterialPropertyBlock properties;
    private Coroutine pulseRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        separators.Clear();
        searched = false;
    }

    /// <summary>Runs one pulse. Safe to call on every peer; safe to call when nothing matches.</summary>
    public static void Pulse()
    {
        EnsureSeparators();
        if (separators.Count == 0)
            return;

        if (instance == null)
        {
            GameObject host = new("DeckPulseFX");
            instance = host.AddComponent<DeckPulseFX>();
            instance.properties = new MaterialPropertyBlock();
        }

        if (instance.pulseRoutine != null)
            instance.StopCoroutine(instance.pulseRoutine);
        instance.pulseRoutine = instance.StartCoroutine(instance.Run());
    }

    /// <summary>Drops the cached renderer list. Call when the board is rebuilt.</summary>
    public static void Invalidate()
    {
        separators.Clear();
        searched = false;
    }

    private static void EnsureSeparators()
    {
        if (searched && separators.Count > 0 && separators[0] != null)
            return;

        separators.Clear();
        searched = true;

        // One scan for the whole match. The board is scene-authored and does not change shape.
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (string wanted in SeparatorMaterialNames)
        {
            foreach (Renderer candidate in all)
            {
                Material shared = candidate.sharedMaterial;
                if (shared != null && shared.name == wanted)
                    separators.Add(candidate);
            }
            if (separators.Count > 0)
                return;
        }
    }

    private IEnumerator Run()
    {
        // Toward ink, not away from it. §9.5 makes the phase transition darken the grid rather
        // than light it: on a bright board a brightening pulse is nearly invisible, and the deck
        // going momentarily heavy is the readable version of the same beat.
        Color peak = Color.Lerp(Color.white, FXPalette.Ink, PulseStrength);
        float elapsed = 0f;
        while (elapsed < PulseSeconds)
        {
            // Up fast, down slow: a struck surface, not a breathing one.
            float progress = elapsed / PulseSeconds;
            float strength = progress < 0.25f
                ? progress / 0.25f
                : 1f - (progress - 0.25f) / 0.75f;
            Push(peak * strength);
            elapsed += Time.deltaTime;
            yield return null;
        }
        Push(Color.black);
        pulseRoutine = null;
    }

    private void Push(Color emission)
    {
        for (int index = separators.Count - 1; index >= 0; index--)
        {
            Renderer separator = separators[index];
            if (separator == null)
            {
                separators.RemoveAt(index);
                continue;
            }
            separator.GetPropertyBlock(properties);
            properties.SetColor(EmissionColorId, emission);
            separator.SetPropertyBlock(properties);
        }
    }
}
