using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds every source the game plays at the levels the player chose. Sources authored into a scene
/// are found when that scene's manager wakes; anything appearing later has to come through
/// PlayEffect, PlayEffectAt, or RegisterEffectsSource, because a source nobody hands over cannot be
/// scaled until the level next changes. The one backing track a scene authors is the exception: it
/// is lifted off the scene onto a single process-wide source, so the loads that ask for the clip
/// already playing never restart or re-decode it.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public AudioSource musicSource;
    public AudioSource SFXSource;

    public AudioClip buttonClick;

    /// <summary>
    /// Long enough that a clip change reads as a fade rather than a cut, short enough that the
    /// outgoing track is gone before the new screen has settled.
    /// </summary>
    private const float CrossfadeSeconds = 0.3f;

    private static AudioManager instance;

    /// <summary>
    /// Kept per process rather than per manager so a source stays at its authored level across the
    /// scene loads that rebuild the manager, and so registering one twice cannot mistake the level
    /// already applied to it for the level it was authored with.
    /// </summary>
    private static readonly List<MixedSource> mixedSources = new();

    /// <summary>
    /// The one source that survives a scene load, so the menu screens share a single continuous
    /// play head instead of each starting the track again. It is tracked as music like any other
    /// source, so the player's level still reaches it.
    /// </summary>
    private static GameObject backingTrackHost;
    private static AudioSource backingTrack;
    private static MixedSource backingMix;

    /// <summary>
    /// The clip waiting for the outgoing one to fade away, held here rather than in a coroutine
    /// because the manager that started the change is destroyed by the load that caused it.
    /// </summary>
    private static AudioClip queuedClip;
    private static bool queuedLoop;
    private static float queuedVolume;
    private static bool fadingOut;

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

    private void Update()
    {
        AdvanceCrossfade(Time.unscaledDeltaTime);
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

        GameObject host = backingTrackHost;
        backingTrackHost = null;
        backingTrack = null;
        backingMix = null;
        queuedClip = null;
        queuedLoop = false;
        queuedVolume = 1f;
        fadingOut = false;

        if (host != null)
            Discard(host);
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
    /// none of them carry a mixer group to route by, so the levels are applied per source. The
    /// scene's own backing track is taken over rather than tracked: its clip moves to the source
    /// that outlives the load and the component is destroyed, so nothing plays it twice and no
    /// later sweep can mistake it for an effect.
    /// </summary>
    private void CollectSources()
    {
        AudioSource[] sources = FindSources();
        AudioSource authoredEffects = SFXSource;
        AudioSource authoredTrack = PickBackingTrack(sources, authoredEffects);

        if (authoredTrack != null)
            AdoptBackingTrack(authoredTrack);

        foreach (AudioSource source in sources)
        {
            if (
                source == null
                || ReferenceEquals(source, authoredTrack)
                || ReferenceEquals(source, backingTrack)
            )
                continue;

            bool isMusic = !ReferenceEquals(source, authoredEffects) && IsBackingTrack(source);
            Track(source, isMusic, source.volume);

            if (!isMusic && SFXSource == null)
                SFXSource = source;
        }

        if (backingTrack != null)
            musicSource = backingTrack;

        if (SFXSource == null)
            SFXSource = CreateEffectsSource();
    }

    /// <summary>
    /// At most one track per scene is lifted out, and the inspector reference wins over the search,
    /// because a match spawns any amount of looping combat audio and none of it is music.
    /// </summary>
    private AudioSource PickBackingTrack(AudioSource[] sources, AudioSource authoredEffects)
    {
        if (musicSource != null && !ReferenceEquals(musicSource, backingTrack))
            return musicSource.clip != null ? musicSource : null;

        foreach (AudioSource source in sources)
        {
            if (
                source == null
                || ReferenceEquals(source, authoredEffects)
                || ReferenceEquals(source, backingTrack)
            )
                continue;

            if (source.clip != null && IsBackingTrack(source))
                return source;
        }

        return null;
    }

    /// <summary>
    /// Takes the clip off the scene's track and hands it to the source that outlives the load. A
    /// load asking for the clip already playing leaves the play head alone, which is what keeps the
    /// menu screens seamless; anything else fades across.
    /// </summary>
    private static void AdoptBackingTrack(AudioSource authored)
    {
        AudioClip clip = authored.clip;
        bool loop = authored.loop;
        float authoredVolume = authored.volume;

        authored.Stop();
        authored.clip = null;
        Discard(authored);

        EnsureBackingTrack();

        if (ReferenceEquals(backingTrack.clip, clip))
        {
            backingTrack.loop = loop;
            queuedClip = null;
            fadingOut = false;

            if (!backingTrack.isPlaying)
                backingTrack.Play();

            return;
        }

        queuedClip = clip;
        queuedLoop = loop;
        queuedVolume = authoredVolume;

        if (backingTrack.clip == null)
            StartQueuedClip();
        else
            fadingOut = true;
    }

    private static void EnsureBackingTrack()
    {
        if (backingTrack != null)
            return;

        backingTrackHost = new GameObject("Persistent Music");
        if (Application.isPlaying)
            DontDestroyOnLoad(backingTrackHost);

        backingTrack = backingTrackHost.AddComponent<AudioSource>();
        backingTrack.playOnAwake = false;
        backingTrack.spatialBlend = 0f;

        backingMix = new MixedSource(backingTrack, true, 1f) { gain = 0f };
        mixedSources.Add(backingMix);
        Apply(backingMix, GameSettings.Current);
    }

    /// <summary>Silent at the swap so the fade in is driven by the same gain the fade out used.</summary>
    private static void StartQueuedClip()
    {
        backingTrack.Stop();
        backingTrack.clip = queuedClip;
        backingTrack.loop = queuedLoop;
        backingMix.authoredVolume = queuedVolume;
        backingMix.gain = 0f;
        Apply(backingMix, GameSettings.Current);
        backingTrack.Play();
        queuedClip = null;
    }

    /// <summary>
    /// Driven from the per-scene manager rather than a component on the persistent object, so the
    /// object stays a bare source and the fade cannot be cut short by the load that started it.
    /// Unscaled, because the menus and the pauses a match takes both stop time, and stepped in
    /// slices so the long frame a scene load costs cannot swallow the whole fade at once.
    /// </summary>
    private static void AdvanceCrossfade(float deltaTime)
    {
        if (backingMix == null || backingTrack == null)
            return;

        float step = Mathf.Min(deltaTime, 0.05f) / CrossfadeSeconds;

        if (fadingOut)
        {
            backingMix.gain = Mathf.MoveTowards(backingMix.gain, 0f, step);
            Apply(backingMix, GameSettings.Current);

            if (backingMix.gain > 0f)
                return;

            fadingOut = false;
            if (queuedClip != null)
                StartQueuedClip();

            return;
        }

        if (backingMix.gain < 1f)
        {
            backingMix.gain = Mathf.MoveTowards(backingMix.gain, 1f, step);
            Apply(backingMix, GameSettings.Current);
        }
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
            mixed.authoredVolume
            * mixed.gain
            * (mixed.isMusic ? settings.musicVolume : settings.sfxVolume);
    }

    /// <summary>
    /// The audio state has to be gone before the next look at it, and the resets run outside play,
    /// so neither path can wait for the end of the frame.
    /// </summary>
    private static void Discard(Object doomed)
    {
        if (Application.isPlaying)
            Destroy(doomed);
        else
            DestroyImmediate(doomed);
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
    /// of compounding on whatever the last apply left behind. The gain is the fade's own weight,
    /// kept apart from the authored level so a crossfade and the player's slider compose rather
    /// than overwrite one another.
    /// </summary>
    private sealed class MixedSource
    {
        public readonly AudioSource source;
        public readonly bool isMusic;
        public float authoredVolume;
        public float gain = 1f;

        public MixedSource(AudioSource source, bool isMusic, float authoredVolume)
        {
            this.source = source;
            this.isMusic = isMusic;
            this.authoredVolume = authoredVolume;
        }
    }
}
