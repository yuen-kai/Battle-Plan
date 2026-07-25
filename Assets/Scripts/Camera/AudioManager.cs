using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Prefab-facing façade over <see cref="BattlePlanAudio"/>.
///
/// This component used to be an empty stub with two null sources and an unused click clip. It is
/// kept — rather than deleted — for two reasons: <c>Assets/Prefabs/AudioManager.prefab</c> still
/// serialises against it, and the audio direction brief (§10.2) asks for <c>uiSource</c> and
/// <c>ambienceSource</c> alongside the original two. It is deliberately *not* a second audio
/// manager. All playback, pooling, ducking and routing belong to <see cref="BattlePlanAudio"/>,
/// which bootstraps itself before the first scene loads and does not need this prefab to exist.
///
/// What this class adds is a home for hand-placed sources: drop an AudioSource on the prefab,
/// assign it here, and it gets routed to the right mixer bus instead of playing straight out to
/// the master. Everything here is client-local presentation and never touches simulation state.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Hand-placed sources (optional)")]
    [Tooltip("Routed to the Music bus. Leave empty unless a scene needs its own bespoke bed.")]
    public AudioSource musicSource;

    [Tooltip("Routed to the SFX bus.")]
    public AudioSource SFXSource;

    [Tooltip("Routed to the UI bus.")]
    public AudioSource uiSource;

    [Tooltip("Routed to the Ambience bus.")]
    public AudioSource ambienceSource;

    [Header("Legacy")]
    [Tooltip("Retained so the existing prefab reference stays valid. Prefer AudioCueId.UiPress.")]
    public AudioClip buttonClick;

    private void Awake()
    {
        BattlePlanAudio.EnsureInstance();

        Route(musicSource, AudioBusId.Music);
        Route(SFXSource, AudioBusId.Sfx);
        Route(uiSource, AudioBusId.Ui);
        Route(ambienceSource, AudioBusId.Ambience);
    }

    private static void Route(AudioSource source, AudioBusId bus)
    {
        if (source == null)
            return;

        AudioMixerGroup group = BattlePlanAudio.GetBusGroup(bus);
        if (group != null)
            source.outputAudioMixerGroup = group;
    }

    /// <summary>
    /// Kept so the old call shape still works if anything reaches for it. Routes through the cue
    /// table so the click obeys the UI bus, its priority, and its repeat limiting.
    /// </summary>
    public void PlayButtonClick()
    {
        BattlePlanAudio.Play(AudioCueId.UiPress);
    }
}
