using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds every source the game plays at the levels the player chose. Sources authored into a scene
/// are found when that scene's manager wakes; anything appearing later has to come through
/// PlayEffect, PlayEffectAt, or RegisterEffectsSource, because a source nobody hands over cannot be
/// scaled until the level next changes.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public AudioSource musicSource;
    public AudioSource SFXSource;

    public AudioClip buttonClick;

    private static AudioManager instance;

    /// <summary>
    /// Kept per process rather than per manager so a source stays at its authored level across the
    /// scene loads that rebuild the manager, and so registering one twice cannot mistake the level
    /// already applied to it for the level it was authored with.
    /// </summary>
    private static readonly List<MixedSource> mixedSources = new();

    /// <summary>
    /// Every scene carries its own AudioManager prefab instance, so this resolves on demand instead
    /// of holding one across loads.
    /// </summary>
    public static AudioManager Instance
    {
        get
        {
            if (instance == null)
                instance = FindAnyObjectByType<AudioManager>();
            return instance;
        }
    }

    private void Awake()
    {
        instance = this;
        GameSettings.EnsureLoaded();
        EnsureSubscribed();
        CollectSources();
        ApplyToTrackedSources(GameSettings.Current);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>Drops state a Play session with no domain reload would inherit.</summary>
    public static void ClearRuntimeState()
    {
        GameSettings.Changed -= ApplyToTrackedSources;
        mixedSources.Clear();
        instance = null;
    }

    public void PlayButtonClick()
    {
        PlaySfx(buttonClick);
    }

    /// <summary>
    /// The effects source already carries the effects level, so <paramref name="volumeScale"/> is
    /// this shot's own weight against its siblings and never the level itself.
    /// </summary>
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null)
            return;

        if (SFXSource == null)
        {
            PlayDetached(clip, transform.position, 0f, volumeScale);
            return;
        }

        SFXSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    /// <summary>
    /// Fires a flat one-shot at the effects level from anywhere, with or without a manager in the
    /// scene. This is what gameplay should call instead of reaching for an AudioSource of its own.
    /// </summary>
    public static void PlayEffect(AudioClip clip, float volumeScale = 1f)
    {
        AudioManager manager = Instance;
        if (manager != null)
            manager.PlaySfx(clip, volumeScale);
        else
            PlayDetached(clip, Vector3.zero, 0f, volumeScale);
    }

    /// <summary>
    /// A positioned one-shot for combat audio, standing in for AudioSource.PlayClipAtPoint. That
    /// call leaves behind a source nothing can reach, so its only chance to respect the effects
    /// level is the volume argument at the call site; this keeps the source and scales it here.
    /// </summary>
    public static void PlayEffectAt(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        PlayDetached(clip, position, 1f, volumeScale);
    }

    /// <summary>
    /// Brings a source the manager never saw under the effects level and keeps it there: sources on
    /// prefabs spawned mid-match, pooled sources, and sources switched on long after Awake. Call it
    /// before anything scales the source, since its current volume is taken as the authored level.
    /// Calling it again for the same source only re-applies, so a pool can register on every reuse.
    /// </summary>
    public static void RegisterEffectsSource(AudioSource source)
    {
        if (source != null)
            Track(source, false, source.volume);
    }

    /// <summary>
    /// Registers a source whose authored level the caller already knows, for pools that reset
    /// volume themselves or prefabs that set it in code.
    /// </summary>
    public static void RegisterEffectsSource(AudioSource source, float authoredVolume)
    {
        if (source != null)
            Track(source, false, authoredVolume);
    }

    /// <summary>
    /// Opts a source built at runtime into the music level. Without this, anything arriving after
    /// Awake takes the effects level, since that is what runtime audio nearly always is.
    /// </summary>
    public static void RegisterMusicSource(AudioSource source)
    {
        if (source != null)
            Track(source, true, source.volume);
    }

    /// <summary>
    /// A backing track either loops or starts itself on a clip it already owns; an effects source
    /// is handed its clip at the moment it fires. Only authored scene audio is read this way: a
    /// projectile that plays its own clip on awake would look like a track, so runtime sources take
    /// the effects level unless the caller asks for music.
    /// </summary>
    public static bool IsBackingTrack(AudioSource source)
    {
        return source.loop || (source.playOnAwake && source.clip != null);
    }

    /// <summary>
    /// The scene's audio is authored as loose AudioSources that nothing holds a reference to, and
    /// none of them carry a mixer group to route by, so the levels are applied per source.
    /// </summary>
    private void CollectSources()
    {
        AudioSource authoredTrack = musicSource;
        AudioSource authoredEffects = SFXSource;

        foreach (AudioSource source in FindSources())
        {
            bool isMusic =
                ReferenceEquals(source, authoredTrack)
                || (!ReferenceEquals(source, authoredEffects) && IsBackingTrack(source));
            Track(source, isMusic, source.volume);

            if (isMusic && musicSource == null)
                musicSource = source;
            else if (!isMusic && SFXSource == null)
                SFXSource = source;
        }

        if (SFXSource == null)
            SFXSource = CreateEffectsSource();
    }

    /// <summary>Keeps click feedback working in a scene that authored no effects source.</summary>
    private AudioSource CreateEffectsSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        Track(source, false, source.volume);
        return source;
    }

    /// <summary>
    /// One subscription for the process, re-made rather than counted, so neither a scene load nor
    /// the reset that clears the event can leave the tracked sources following nothing.
    /// </summary>
    private static void EnsureSubscribed()
    {
        GameSettings.Changed -= ApplyToTrackedSources;
        GameSettings.Changed += ApplyToTrackedSources;
    }

    private static void Track(AudioSource source, bool isMusic, float authoredVolume)
    {
        EnsureSubscribed();

        MixedSource mixed = Tracked(source);
        if (mixed == null)
        {
            mixed = new MixedSource(source, isMusic, authoredVolume);
            mixedSources.Add(mixed);
        }

        Apply(mixed, GameSettings.Current);
    }

    /// <summary>
    /// Drops the sources destroyed since the last look on the way past, so the one-shots a match
    /// spawns cannot pile up in here.
    /// </summary>
    private static MixedSource Tracked(AudioSource source)
    {
        MixedSource found = null;
        for (int i = mixedSources.Count - 1; i >= 0; i--)
        {
            MixedSource mixed = mixedSources[i];
            if (mixed.source == null)
            {
                mixedSources.RemoveAt(i);
                continue;
            }

            if (ReferenceEquals(mixed.source, source))
                found = mixed;
        }

        return found;
    }

    private static void ApplyToTrackedSources(GameSettings settings)
    {
        if (instance != null)
            AdoptStrangers();

        for (int i = mixedSources.Count - 1; i >= 0; i--)
        {
            MixedSource mixed = mixedSources[i];
            if (mixed.source == null)
            {
                mixedSources.RemoveAt(i);
                continue;
            }

            Apply(mixed, settings);
        }
    }

    /// <summary>
    /// Catches sources that reached the scene without announcing themselves, so a level the player
    /// picks mid-match still lands on audio that is already playing. It cannot help a one-shot that
    /// starts and finishes between two changes, which is what the play and register calls are for.
    /// Guarded by a manager that has actually woken, so nothing sweeps an editor scene.
    /// </summary>
    private static void AdoptStrangers()
    {
        foreach (AudioSource source in FindSources())
        {
            if (Tracked(source) == null)
                mixedSources.Add(new MixedSource(source, false, source.volume));
        }
    }

    private static AudioSource[] FindSources()
    {
        return FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
    }

    private static void Apply(MixedSource mixed, GameSettings settings)
    {
        mixed.source.volume =
            mixed.authoredVolume * (mixed.isMusic ? settings.musicVolume : settings.sfxVolume);
    }

    /// <summary>
    /// Gives a one-shot a source of its own so the level can be applied to it, then clears up once
    /// the clip has run.
    /// </summary>
    private static void PlayDetached(
        AudioClip clip,
        Vector3 position,
        float spatialBlend,
        float volumeScale
    )
    {
        if (clip == null)
            return;

        GameObject host = new($"Effect {clip.name}");
        host.transform.position = position;

        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.clip = clip;
        source.spatialBlend = spatialBlend;
        Track(source, false, Mathf.Clamp01(volumeScale));
        source.Play();
        Destroy(host, clip.length + 0.1f);
    }

    /// <summary>
    /// Remembers the level a source was authored with so repeated applies scale that level instead
    /// of compounding on whatever the last apply left behind.
    /// </summary>
    private sealed class MixedSource
    {
        public readonly AudioSource source;
        public readonly float authoredVolume;
        public readonly bool isMusic;

        public MixedSource(AudioSource source, bool isMusic, float authoredVolume)
        {
            this.source = source;
            this.isMusic = isMusic;
            this.authoredVolume = authoredVolume;
        }
    }
}
