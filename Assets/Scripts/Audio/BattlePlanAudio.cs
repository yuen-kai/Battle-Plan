using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Battle Plan's runtime audio system: mixer buses, a pooled set of AudioSources, one-shot and
/// looping playback, music with crossfade, and ducking of the bed under important gameplay cues.
///
/// <para><b>Presentation only.</b> Everything here is client-local. Nothing in this class reads or
/// writes gameplay state, sends an RPC, touches a NetworkVariable, or consumes bandwidth, and no
/// call into it can change a simulation outcome. It follows the same pattern as the existing local
/// VFX helpers (<see cref="ImpactShockwave"/>, <see cref="HitFlash"/>): a static entry point that
/// each peer calls for itself at the moment something happens.</para>
///
/// <para><b>No scene wiring.</b> The manager builds itself on first use through
/// <see cref="Bootstrap"/> and survives scene loads, so it works in every scene without a prefab
/// or a serialized reference — the same trick <see cref="Bullet"/> uses for its static registry.</para>
///
/// <para>Typical use:
/// <code>
/// BattlePlanAudio.Play(AudioCueId.LockIn);                              // flat
/// BattlePlanAudio.PlayAt(AudioCueId.WeaponRifle, muzzle, shooter);      // placed, fog-gated
/// AudioLoopHandle charge = BattlePlanAudio.PlayLoop(AudioCueId.SniperLockCharge, transform);
/// charge.Stop(0.15f);
/// </code>
/// </para>
/// </summary>
public class BattlePlanAudio : MonoBehaviour
{
    public const string MixerResourcePath = "BattlePlanMixer";
    public const string SfxResourceFolder = "BattlePlanSfx";
    public const string MusicResourceFolder = "BattlePlanMusic";

    private const int PooledSourceCount = 32;

    // Board audio is never fully spatialised. The match camera sits 24.4 units above a 40.5 x 27
    // board, so straight 3D would make distance-to-listener — which is mostly camera height —
    // swamp the part players actually read, which is left-to-right position. A 0.65 blend keeps
    // panning and a gentle near/far tilt without the far corner going thin and distant. The
    // distances are set around the camera height rather than the board's own dimensions for the
    // same reason: at min 4 / max 40 the far corner would attenuate to near silence.
    private const float PositionalSpatialBlend = 0.65f;
    private const float PositionalMinDistance = 20f;
    private const float PositionalMaxDistance = 56f;

    /// <summary>
    /// Your own crew reads louder than the opposition, which is a second, non-pitch channel for
    /// telling the two teams apart (colour and screen side are the others). Roughly the 3 dB split
    /// between own and enemy weapon fire in the audio direction brief.
    /// </summary>
    private const float OwnTeamGain = 1f;
    private const float EnemyTeamGain = 0.72f;

    public static BattlePlanAudio Instance { get; private set; }

    private readonly Dictionary<AudioBusId, AudioBusState> buses = new();
    private readonly Dictionary<string, AudioClip[]> clipsByKey = new();
    private readonly Dictionary<AudioCueId, float> lastPlayedUnscaled = new();
    private readonly Dictionary<AudioCueId, int> liveVoicesByCue = new();
    private readonly Dictionary<AudioCueId, int> lastVariantIndex = new();
    private readonly List<AudioVoice> pool = new();

    private AudioMixer mixer;
    private AudioSource musicA;
    private AudioSource musicB;
    private AudioSource activeMusic;
    private Coroutine musicFadeRoutine;
    private Coroutine duckRoutine;
    private AudioListener fallbackListener;
    private string currentMusicKey;
    private float musicTargetVolume = 1f;
    private bool warnedMissingClips;

    /// <summary>Name of the music track currently playing, or empty.</summary>
    public static string CurrentMusicKey => Instance != null ? Instance.currentMusicKey : string.Empty;

    // === Bootstrap ==========================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static BattlePlanAudio EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        GameObject host = new("BattlePlanAudio");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<BattlePlanAudio>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadMixer();
        BuildBuses();
        BuildPool();
        BuildMusicSources();
        LoadSfxClips();
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureListener();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureListener();
        LegacyAudioSuppressor.SuppressLegacySceneMusic();
        BattlePlanUIAudio.BindLoadedPanels();
        MusicDirector.OnSceneLoaded(scene.name);
        StartCoroutine(BindPanelsNextFrame());
    }

    /// <summary>
    /// A UIDocument that builds its tree after scene activation would miss the pass above; binding
    /// is idempotent, so a second look one frame later costs nothing and closes the gap.
    /// </summary>
    private IEnumerator BindPanelsNextFrame()
    {
        yield return null;
        BattlePlanUIAudio.BindLoadedPanels();
    }

    /// <summary>
    /// Guarantees something is listening. Every shipped scene already carries a listener on its
    /// camera; this only covers a scene that does not, and never adds a second one.
    /// </summary>
    private void EnsureListener()
    {
        if (fallbackListener != null)
        {
            bool sceneListenerExists = false;
            foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener != fallbackListener && listener.isActiveAndEnabled)
                {
                    sceneListenerExists = true;
                    break;
                }
            }

            fallbackListener.enabled = !sceneListenerExists;
            return;
        }

        if (FindFirstObjectByType<AudioListener>() != null)
            return;

        fallbackListener = gameObject.AddComponent<AudioListener>();
    }

    // === Mixer and buses ====================================================

    private void LoadMixer()
    {
        mixer = Resources.Load<AudioMixer>(MixerResourcePath);
        if (mixer == null)
        {
            Debug.LogWarning(
                $"[Audio] No AudioMixer at Resources/{MixerResourcePath}. Bus volumes will be "
                    + "applied per source instead; routing and exposed parameters are unavailable."
            );
        }
    }

    private void BuildBuses()
    {
        AddBus(AudioBusId.Master, "MasterVolume", "Master", 1f);
        AddBus(AudioBusId.Music, "MusicVolume", "Music", 0.62f);
        AddBus(AudioBusId.Sfx, "SFXVolume", "SFX", 1f);
        AddBus(AudioBusId.Ui, "UIVolume", "UI", 0.9f);
        AddBus(AudioBusId.Ambience, "AmbienceVolume", "Ambience", 0.75f);
    }

    private void AddBus(AudioBusId id, string exposedParameter, string groupName, float defaultVolume)
    {
        AudioMixerGroup group = null;
        if (mixer != null)
        {
            AudioMixerGroup[] matches = mixer.FindMatchingGroups(groupName);
            foreach (AudioMixerGroup candidate in matches)
            {
                if (candidate != null && candidate.name == groupName)
                {
                    group = candidate;
                    break;
                }
            }

            if (group == null)
            {
                Debug.LogWarning(
                    $"[Audio] Mixer group '{groupName}' not found; {id} will not be routed."
                );
            }
        }

        AudioBusState bus = new()
        {
            id = id,
            exposedParameter = exposedParameter,
            group = group,
            volume = defaultVolume,
        };
        buses[id] = bus;
        ApplyBus(bus);
    }

    private void ApplyBus(AudioBusState bus)
    {
        if (mixer == null || string.IsNullOrEmpty(bus.exposedParameter))
            return;

        mixer.SetFloat(bus.exposedParameter, LinearToDecibels(bus.EffectiveVolume));
    }

    /// <summary>
    /// Sets a bus fader, 0..1, perceptual. Persisting and exposing this is all an audio options
    /// panel would need — see the handoff note about the Settings dialog.
    /// </summary>
    public static void SetBusVolume(AudioBusId bus, float volume01)
    {
        BattlePlanAudio audio = EnsureInstance();
        if (!audio.buses.TryGetValue(bus, out AudioBusState state))
            return;

        state.volume = Mathf.Clamp01(volume01);
        audio.ApplyBus(state);
    }

    public static float GetBusVolume(AudioBusId bus)
    {
        BattlePlanAudio audio = EnsureInstance();
        return audio.buses.TryGetValue(bus, out AudioBusState state) ? state.volume : 1f;
    }

    /// <summary>
    /// The mixer group a bus routes to, or null when no mixer asset is present. Lets a
    /// hand-authored AudioSource in a scene or prefab join the same bus structure as the pool.
    /// </summary>
    public static AudioMixerGroup GetBusGroup(AudioBusId bus)
    {
        BattlePlanAudio audio = EnsureInstance();
        return audio.buses.TryGetValue(bus, out AudioBusState state) ? state.group : null;
    }

    /// <summary>Decibels for the mixer, with a true silence floor rather than -80 dB of hiss.</summary>
    public static float LinearToDecibels(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
    }

    private float GetBusGain(AudioBusId bus)
    {
        // With a mixer, the bus fader lives on the mixer and must not be applied twice.
        if (mixer != null)
            return 1f;

        float gain = buses.TryGetValue(bus, out AudioBusState state) ? state.EffectiveVolume : 1f;
        if (bus != AudioBusId.Master && buses.TryGetValue(AudioBusId.Master, out AudioBusState master))
            gain *= master.EffectiveVolume;
        return gain;
    }

    // === Clip binding =======================================================

    /// <summary>
    /// Clips are bound by filename rather than by serialized reference so that new variations can
    /// be dropped into the Resources folder without touching an asset or a line of code. A cue
    /// named <c>WeaponRifle</c> picks up <c>WeaponRifle.wav</c> and every <c>WeaponRifle_*.wav</c>.
    /// </summary>
    private void LoadSfxClips()
    {
        clipsByKey.Clear();
        AudioClip[] loaded = Resources.LoadAll<AudioClip>(SfxResourceFolder);
        Dictionary<string, List<AudioClip>> grouped = new();

        foreach (AudioClip clip in loaded)
        {
            if (clip == null)
                continue;

            string key = clip.name;
            int separator = key.LastIndexOf('_');
            if (separator > 0 && IsVariantSuffix(key, separator))
                key = key.Substring(0, separator);

            if (!grouped.TryGetValue(key, out List<AudioClip> list))
            {
                list = new List<AudioClip>();
                grouped[key] = list;
            }
            list.Add(clip);
        }

        foreach (var entry in grouped)
        {
            entry.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            clipsByKey[entry.Key] = entry.Value.ToArray();
        }
    }

    private static bool IsVariantSuffix(string name, int separatorIndex)
    {
        for (int i = separatorIndex + 1; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return false;
        }
        return separatorIndex + 1 < name.Length;
    }

    /// <summary>Rebuilds the clip table. Only needed after clips are added at edit time.</summary>
    public static void ReloadClips()
    {
        EnsureInstance().LoadSfxClips();
    }

    public static bool HasClipsFor(AudioCueId cue)
    {
        AudioCueSettings settings = AudioCueTable.Get(cue);
        return settings != null
            && EnsureInstance().clipsByKey.TryGetValue(settings.ClipKey, out AudioClip[] clips)
            && clips.Length > 0;
    }

    private AudioClip PickClip(AudioCueSettings settings)
    {
        if (!clipsByKey.TryGetValue(settings.ClipKey, out AudioClip[] clips) || clips.Length == 0)
            return null;
        if (clips.Length == 1)
            return clips[0];

        // Never the same variation twice in a row: two rifle shots that share a sample read as a
        // glitch, not as a second shot.
        lastVariantIndex.TryGetValue(settings.cue, out int previous);
        int index = Random.Range(0, clips.Length - 1);
        if (index >= previous)
            index++;
        lastVariantIndex[settings.cue] = index;
        return clips[index];
    }

    // === Voice pool =========================================================

    private void BuildPool()
    {
        for (int i = 0; i < PooledSourceCount; i++)
        {
            GameObject voiceObject = new($"Voice_{i:00}");
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = PositionalMinDistance;
            source.maxDistance = PositionalMaxDistance;
            pool.Add(new AudioVoice(source));
        }
    }

    private AudioVoice AcquireVoice(int priority)
    {
        AudioVoice weakest = null;
        foreach (AudioVoice voice in pool)
        {
            if (!voice.IsBusy)
                return voice;

            // Steal from the least important thing sounding, and only if we outrank it.
            if (weakest == null || voice.priority > weakest.priority)
                weakest = voice;
        }

        if (weakest != null && weakest.priority > priority)
        {
            ReleaseVoice(weakest);
            return weakest;
        }
        return null;
    }

    private void ReleaseVoice(AudioVoice voice)
    {
        if (voice.cue != AudioCueId.None && liveVoicesByCue.TryGetValue(voice.cue, out int count))
            liveVoicesByCue[voice.cue] = Mathf.Max(0, count - 1);

        voice.Reset();
    }

    private void Update()
    {
        float unscaledDelta = Time.unscaledDeltaTime;
        foreach (AudioVoice voice in pool)
        {
            if (!voice.IsBusy)
                continue;

            voice.Tick(unscaledDelta);
            if (voice.IsFinished)
                ReleaseVoice(voice);
        }

        CombatAudio.Tick();
    }

    // === Playback ===========================================================

    /// <summary>Plays a flat, non-positional cue. Interface and whole-match moments.</summary>
    public static void Play(AudioCueId cue, float volumeScale = 1f, float pitchScale = 1f)
    {
        EnsureInstance().PlayInternal(cue, null, null, volumeScale, pitchScale, null);
    }

    /// <summary>
    /// Plays a cue on the board. Fog-gated according to the cue's
    /// <see cref="AudioVisibilityRule"/>; pass <paramref name="sourceUnit"/> when the sound belongs
    /// to a specific unit so "always hear your own crew" can apply and the own/enemy level split
    /// can be worked out.
    /// </summary>
    public static void PlayAt(
        AudioCueId cue,
        Vector3 worldPosition,
        GameObject sourceUnit = null,
        float volumeScale = 1f,
        float pitchScale = 1f
    )
    {
        EnsureInstance().PlayInternal(cue, worldPosition, null, volumeScale, pitchScale, sourceUnit);
    }

    /// <summary>
    /// Semitone offset helper for cues that walk in pitch — the planning path tick rises as a route
    /// gets longer, which is the closest thing the game has to a signature sound.
    /// </summary>
    public static float SemitonesToPitch(float semitones)
    {
        return Mathf.Pow(2f, semitones / 12f);
    }

    /// <summary>
    /// Starts a looping cue that follows <paramref name="follow"/>. The returned handle is the only
    /// way to stop it; dropping the handle leaves the loop running until the object is destroyed.
    /// </summary>
    public static AudioLoopHandle PlayLoop(
        AudioCueId cue,
        Transform follow,
        float volumeScale = 1f
    )
    {
        BattlePlanAudio audio = EnsureInstance();
        Vector3? position = follow != null ? follow.position : (Vector3?)null;
        AudioVoice voice = audio.PlayInternal(
            cue,
            position,
            follow,
            volumeScale,
            1f,
            follow != null ? follow.gameObject : null
        );
        return new AudioLoopHandle(voice);
    }

    /// <summary>Starts a looping cue with no world position — ambience beds and interface drones.</summary>
    public static AudioLoopHandle PlayLoopFlat(AudioCueId cue, float volumeScale = 1f)
    {
        return new AudioLoopHandle(
            EnsureInstance().PlayInternal(cue, null, null, volumeScale, 1f, null)
        );
    }

    private AudioVoice PlayInternal(
        AudioCueId cue,
        Vector3? worldPosition,
        Transform follow,
        float volumeScale,
        float pitchScale,
        GameObject sourceUnit
    )
    {
        AudioCueSettings settings = AudioCueTable.Get(cue);
        if (settings == null)
        {
            Debug.LogWarning($"[Audio] Cue {cue} has no entry in AudioCueTable.");
            return null;
        }

        bool positional = settings.spatial == AudioSpatialMode.Positional && worldPosition.HasValue;
        if (
            positional
            && !AudioVisibilityGate.IsAudible(worldPosition.Value, settings.visibility, sourceUnit)
        )
        {
            return null;
        }

        float now = Time.unscaledTime;
        if (
            settings.minInterval > 0f
            && lastPlayedUnscaled.TryGetValue(cue, out float last)
            && now - last < settings.minInterval
        )
        {
            return null;
        }

        liveVoicesByCue.TryGetValue(cue, out int live);
        if (live >= Mathf.Max(1, settings.maxVoices))
            return null;

        AudioClip clip = PickClip(settings);
        if (clip == null)
        {
            ReportMissingClip(settings);
            return null;
        }

        AudioVoice voice = AcquireVoice(settings.priority);
        if (voice == null)
            return null;

        float volume =
            settings.volume
            * volumeScale
            * (1f + Random.Range(-settings.volumeJitter, settings.volumeJitter))
            * TeamPerspectiveGain(sourceUnit)
            * GetBusGain(settings.bus);
        float pitch =
            (settings.pitchCenter + Random.Range(-settings.pitchJitter, settings.pitchJitter))
            * Mathf.Max(0.05f, pitchScale);

        voice.Configure(
            settings,
            clip,
            Mathf.Clamp(volume, 0f, 1.5f),
            Mathf.Max(0.05f, pitch),
            positional,
            worldPosition ?? Vector3.zero,
            follow,
            buses.TryGetValue(settings.bus, out AudioBusState bus) ? bus.group : null,
            positional ? PositionalSpatialBlend : 0f
        );

        lastPlayedUnscaled[cue] = now;
        // Re-read: acquiring the voice may have retired an older instance of this same cue.
        liveVoicesByCue.TryGetValue(cue, out int liveAfterAcquire);
        liveVoicesByCue[cue] = liveAfterAcquire + 1;

        if (settings.musicDuck > 0f || settings.bedDuck > 0f)
            Duck(settings.musicDuck, settings.musicDuckHold, bedDepth: settings.bedDuck);

        return voice;
    }

    /// <summary>
    /// Own crew slightly forward, opposition slightly back. Unit-less cues are unaffected.
    /// </summary>
    private static float TeamPerspectiveGain(GameObject sourceUnit)
    {
        if (sourceUnit == null)
            return 1f;

        Unit identity = sourceUnit.GetComponentInParent<Unit>();
        if (identity == null || identity.TeamIndex < 0)
            return 1f;

        return GameLoop.IsTeamFriendlyToLocalPlayer(identity.TeamIndex)
            ? OwnTeamGain
            : EnemyTeamGain;
    }

    private void ReportMissingClip(AudioCueSettings settings)
    {
        if (warnedMissingClips)
            return;

        warnedMissingClips = true;
        Debug.LogWarning(
            $"[Audio] No clip found for '{settings.ClipKey}' under Resources/{SfxResourceFolder}. "
                + "Further missing-clip warnings this session are suppressed."
        );
    }

    internal void StopVoice(AudioVoice voice, float fadeSeconds)
    {
        if (voice == null || !voice.IsBusy)
            return;

        if (fadeSeconds <= 0f)
        {
            ReleaseVoice(voice);
            return;
        }
        voice.BeginFadeOut(fadeSeconds);
    }

    // === Music ==============================================================

    private void BuildMusicSources()
    {
        musicA = CreateMusicSource("MusicA");
        musicB = CreateMusicSource("MusicB");
        activeMusic = musicA;
    }

    private AudioSource CreateMusicSource(string sourceName)
    {
        GameObject host = new(sourceName);
        host.transform.SetParent(transform, false);
        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.priority = 0;
        if (buses.TryGetValue(AudioBusId.Music, out AudioBusState bus))
            source.outputAudioMixerGroup = bus.group;
        return source;
    }

    /// <summary>
    /// Crossfades to a music track loaded from <c>Resources/BattlePlanMusic</c>. Re-requesting the
    /// track already playing is a no-op, so scene reloads and phase changes never restart the bed.
    /// </summary>
    public static void PlayMusic(string trackKey, float fadeSeconds = 1.5f, float volume = 1f)
    {
        BattlePlanAudio audio = EnsureInstance();
        if (audio.currentMusicKey == trackKey && audio.activeMusic != null && audio.activeMusic.isPlaying)
        {
            audio.musicTargetVolume = volume;
            return;
        }

        audio.currentMusicKey = trackKey;
        audio.musicTargetVolume = volume;

        if (audio.musicFadeRoutine != null)
            audio.StopCoroutine(audio.musicFadeRoutine);
        audio.musicFadeRoutine = audio.StartCoroutine(
            audio.SwapMusic(trackKey, Mathf.Max(0.01f, fadeSeconds), volume)
        );
    }

    public static void StopMusic(float fadeSeconds = 1.5f)
    {
        BattlePlanAudio audio = EnsureInstance();
        audio.currentMusicKey = string.Empty;
        if (audio.musicFadeRoutine != null)
            audio.StopCoroutine(audio.musicFadeRoutine);
        audio.musicFadeRoutine = audio.StartCoroutine(
            audio.SwapMusic(null, Mathf.Max(0.01f, fadeSeconds), 0f)
        );
    }

    private IEnumerator SwapMusic(string trackKey, float fadeSeconds, float volume)
    {
        AudioClip clip = null;
        if (!string.IsNullOrEmpty(trackKey))
        {
            ResourceRequest request = Resources.LoadAsync<AudioClip>(
                $"{MusicResourceFolder}/{trackKey}"
            );
            yield return request;
            clip = request.asset as AudioClip;
            if (clip == null)
            {
                Debug.LogWarning(
                    $"[Audio] Music track '{trackKey}' not found under Resources/{MusicResourceFolder}."
                );
            }
        }

        AudioSource outgoing = activeMusic;
        AudioSource incoming = activeMusic == musicA ? musicB : musicA;

        if (clip != null)
        {
            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();
            activeMusic = incoming;
        }

        float startOut = outgoing != null ? outgoing.volume : 0f;
        float elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeSeconds);
            if (outgoing != null && outgoing != incoming)
                outgoing.volume = Mathf.Lerp(startOut, 0f, t);
            if (clip != null)
                incoming.volume = MusicSourceVolume() * t;
            yield return null;
        }

        if (outgoing != null && outgoing != incoming)
        {
            outgoing.Stop();
            outgoing.clip = null;
        }
        if (clip != null)
            incoming.volume = MusicSourceVolume();

        musicFadeRoutine = null;
    }

    /// <summary>
    /// Level for the music AudioSources. With a mixer the bus fader and the duck both live on the
    /// mixer, so the source itself stays at the requested track volume and nothing is applied twice.
    /// </summary>
    private float MusicSourceVolume()
    {
        if (mixer != null)
            return musicTargetVolume;

        float gain = musicTargetVolume;
        if (buses.TryGetValue(AudioBusId.Music, out AudioBusState music))
            gain *= music.EffectiveVolume;
        if (buses.TryGetValue(AudioBusId.Master, out AudioBusState master))
            gain *= master.EffectiveVolume;
        return gain;
    }

    // === Ducking ============================================================

    /// <summary>
    /// Pulls the beds down so a gameplay cue can land, then lets them back up. Depths and hold come
    /// from the cue table; a deeper duck already in flight is never cut short by a shallower one.
    ///
    /// <para>The SFX bus is never ducked. Ducking it would attenuate the very impact that asked for
    /// the duck, so the hole a kill leaves behind is made by dropping music and ambience instead.</para>
    /// </summary>
    public static void Duck(
        float depth01,
        float holdSeconds,
        float releaseSeconds = 0.7f,
        float bedDepth = 0f
    )
    {
        BattlePlanAudio audio = EnsureInstance();
        if (!audio.buses.TryGetValue(AudioBusId.Music, out AudioBusState music))
            return;

        depth01 = Mathf.Clamp01(depth01);
        bedDepth = Mathf.Clamp01(bedDepth);
        if (audio.duckRoutine != null && depth01 <= music.duck)
            return;

        if (audio.duckRoutine != null)
            audio.StopCoroutine(audio.duckRoutine);
        audio.duckRoutine = audio.StartCoroutine(
            audio.DuckRoutine(depth01, bedDepth, holdSeconds, releaseSeconds)
        );
    }

    private IEnumerator DuckRoutine(
        float musicDepth,
        float bedDepth,
        float holdSeconds,
        float releaseSeconds
    )
    {
        // 40 ms attack: fast enough to be out of the way before the transient, slow enough not to
        // click the bed.
        const float attackSeconds = 0.04f;

        buses.TryGetValue(AudioBusId.Music, out AudioBusState music);
        buses.TryGetValue(AudioBusId.Ambience, out AudioBusState ambience);
        buses.TryGetValue(AudioBusId.Ui, out AudioBusState ui);

        float musicStart = music?.duck ?? 0f;
        float bedStart = ambience?.duck ?? 0f;

        float elapsed = 0f;
        while (elapsed < attackSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / attackSeconds);
            SetDuckLevels(
                music,
                ambience,
                ui,
                Mathf.Lerp(musicStart, musicDepth, t),
                Mathf.Lerp(bedStart, bedDepth, t)
            );
            yield return null;
        }

        SetDuckLevels(music, ambience, ui, musicDepth, bedDepth);

        elapsed = 0f;
        while (elapsed < holdSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < releaseSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / releaseSeconds);
            SetDuckLevels(
                music,
                ambience,
                ui,
                Mathf.Lerp(musicDepth, 0f, t),
                Mathf.Lerp(bedDepth, 0f, t)
            );
            yield return null;
        }

        SetDuckLevels(music, ambience, ui, 0f, 0f);
        duckRoutine = null;
    }

    private void SetDuckLevels(
        AudioBusState music,
        AudioBusState ambience,
        AudioBusState ui,
        float musicDuck,
        float bedDuck
    )
    {
        if (music != null)
        {
            music.duck = musicDuck;
            ApplyMusicDuck(music);
        }
        if (ambience != null)
        {
            ambience.duck = bedDuck;
            ApplyBus(ambience);
        }
        if (ui != null)
        {
            // The interface only steps aside for the deepest ducks; a click swallowed by an
            // explosion reads as an unresponsive button.
            ui.duck = bedDuck * 0.6f;
            ApplyBus(ui);
        }
    }

    private void ApplyMusicDuck(AudioBusState music)
    {
        if (mixer != null)
        {
            ApplyBus(music);
            return;
        }

        // Without a mixer the duck rides the music sources directly. A crossfade in flight owns
        // those volumes and picks the duck up on its own.
        if (musicFadeRoutine != null)
            return;

        float target = MusicSourceVolume();
        if (musicA != null && musicA.isPlaying)
            musicA.volume = target;
        if (musicB != null && musicB.isPlaying)
            musicB.volume = target;
    }

    // === Internals ==========================================================

    private class AudioBusState
    {
        public AudioBusId id;
        public string exposedParameter;
        public AudioMixerGroup group;
        public float volume = 1f;
        public float duck;

        public float EffectiveVolume => Mathf.Clamp01(volume * (1f - Mathf.Clamp01(duck)));
    }
}

/// <summary>One pooled AudioSource plus the bookkeeping needed to reuse it safely.</summary>
internal class AudioVoice
{
    public readonly AudioSource source;
    public AudioCueId cue;
    public int priority = int.MaxValue;

    private Transform follow;
    private bool followsATarget;
    private float fadeRemaining;
    private float fadeDuration;
    private float fadeStartVolume;
    private bool looping;

    public AudioVoice(AudioSource source)
    {
        this.source = source;
    }

    public bool IsBusy => cue != AudioCueId.None;

    public bool IsFinished =>
        IsBusy && ((!looping && !source.isPlaying) || (fadeDuration > 0f && fadeRemaining <= 0f));

    public void Configure(
        AudioCueSettings settings,
        AudioClip clip,
        float volume,
        float pitch,
        bool positional,
        Vector3 position,
        Transform followTarget,
        AudioMixerGroup group,
        float spatialBlend
    )
    {
        cue = settings.cue;
        priority = settings.priority;
        looping = settings.loop;
        follow = followTarget;
        followsATarget = followTarget != null;
        fadeDuration = 0f;
        fadeRemaining = 0f;

        source.transform.position = position;
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.loop = settings.loop;
        source.priority = Mathf.Clamp(settings.priority, 0, 255);
        source.spatialBlend = spatialBlend;
        source.outputAudioMixerGroup = group;
        source.Play();
    }

    public void BeginFadeOut(float seconds)
    {
        fadeDuration = Mathf.Max(0.01f, seconds);
        fadeRemaining = fadeDuration;
        fadeStartVolume = source.volume;
    }

    public void Tick(float unscaledDelta)
    {
        if (follow != null)
            source.transform.position = follow.position;
        else if (followsATarget && fadeDuration <= 0f)
        {
            // A followed object that was destroyed takes its loop with it.
            BeginFadeOut(0.12f);
        }

        if (fadeDuration <= 0f)
            return;

        fadeRemaining -= unscaledDelta;
        source.volume = fadeStartVolume * Mathf.Clamp01(fadeRemaining / fadeDuration);
    }

    public void Reset()
    {
        source.Stop();
        source.clip = null;
        source.loop = false;
        source.outputAudioMixerGroup = null;
        cue = AudioCueId.None;
        priority = int.MaxValue;
        follow = null;
        followsATarget = false;
        looping = false;
        fadeDuration = 0f;
        fadeRemaining = 0f;
    }
}

/// <summary>Stop token for a looping cue. Safe to hold past the loop's own lifetime.</summary>
public struct AudioLoopHandle
{
    private readonly AudioVoice voice;
    private readonly AudioCueId cue;

    internal AudioLoopHandle(AudioVoice voice)
    {
        this.voice = voice;
        cue = voice != null ? voice.cue : AudioCueId.None;
    }

    public bool IsPlaying => voice != null && voice.cue == cue && cue != AudioCueId.None;

    public void Stop(float fadeSeconds = 0.12f)
    {
        if (!IsPlaying || BattlePlanAudio.Instance == null)
            return;

        BattlePlanAudio.Instance.StopVoice(voice, fadeSeconds);
    }
}
