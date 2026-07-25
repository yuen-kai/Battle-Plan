using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-cue mix and behaviour settings. One instance per <see cref="AudioCueId"/>, defined once in
/// <see cref="AudioCueTable"/>.
/// </summary>
public class AudioCueSettings
{
    public AudioCueId cue;

    /// <summary>Resource filename stem. Several cues may share one clip family.</summary>
    public string clipKey;

    public AudioBusId bus = AudioBusId.Sfx;
    public AudioSpatialMode spatial = AudioSpatialMode.Flat;
    public AudioVisibilityRule visibility = AudioVisibilityRule.RequireVisibleCell;

    /// <summary>Linear gain before the bus fader.</summary>
    public float volume = 0.8f;

    public float pitchCenter = 1f;

    /// <summary>Half-width of the uniform pitch jitter. Keeps repeats from machine-gunning.</summary>
    public float pitchJitter = 0.04f;

    /// <summary>Half-width of the uniform gain jitter, as a fraction of <see cref="volume"/>.</summary>
    public float volumeJitter = 0.06f;

    /// <summary>Minimum seconds between two starts of this cue. Later requests inside it are dropped.</summary>
    public float minInterval;

    /// <summary>Hard cap on simultaneously sounding instances of this cue.</summary>
    public int maxVoices = 4;

    /// <summary>Unity voice priority: 0 never stolen, 255 stolen first.</summary>
    public int priority = 128;

    /// <summary>How far the music bed drops while this cue lands, 0..1.</summary>
    public float musicDuck;

    /// <summary>Seconds the duck holds at full depth before recovering.</summary>
    public float musicDuckHold = 0.2f;

    /// <summary>
    /// How far the ambience and interface beds drop alongside the music, 0..1. Reserved for the
    /// handful of cues that are supposed to leave a hole behind them — a kill, a big ability
    /// impact, the dodge alarm. The SFX bus is deliberately excluded: ducking it would duck the
    /// very impact that asked for the duck.
    /// </summary>
    public float bedDuck;

    public bool loop;

    public string ClipKey => string.IsNullOrEmpty(clipKey) ? cue.ToString() : clipKey;
}

/// <summary>
/// The single source of truth for how every cue sits in the mix.
///
/// <para><b>Hierarchy.</b> Lower <c>priority</c> numbers survive voice stealing. The order the mix
/// protects, loudest and least stealable first, is: round-phase changes and the planning clock →
/// damage to your own crew and eliminations → ability telegraphs and dodge alerts → your own
/// committed actions → weapon fire and impacts → interface → traversal → ambience → music.</para>
///
/// <para><b>Fog.</b> Every positional cue declares an <see cref="AudioVisibilityRule"/>. Only cues
/// whose visual counterpart already pierces fog by design (telegraphs, dodge alerts, grenades,
/// forced reveals) are marked <see cref="AudioVisibilityRule.AlwaysAudible"/>.</para>
/// </summary>
public static class AudioCueTable
{
    private static readonly Dictionary<AudioCueId, AudioCueSettings> settingsByCue = Build();

    public static AudioCueSettings Get(AudioCueId cue)
    {
        return settingsByCue.TryGetValue(cue, out AudioCueSettings settings) ? settings : null;
    }

    public static IEnumerable<AudioCueSettings> All => settingsByCue.Values;

    private static Dictionary<AudioCueId, AudioCueSettings> Build()
    {
        AudioCueSettings[] table =
        {
            // === Interface ============================================================
            // Hover is the most repeated sound in the game; it has to be tiny, droppable, and
            // never the reason a real cue gets stolen.
            new()
            {
                cue = AudioCueId.UiHover,
                bus = AudioBusId.Ui,
                volume = 0.24f,
                pitchJitter = 0.05f,
                minInterval = 0.045f,
                maxVoices = 2,
                priority = 200,
            },
            new()
            {
                cue = AudioCueId.UiPress,
                bus = AudioBusId.Ui,
                volume = 0.55f,
                pitchJitter = 0.035f,
                minInterval = 0.04f,
                maxVoices = 3,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.UiPressPrimary,
                bus = AudioBusId.Ui,
                volume = 0.72f,
                pitchJitter = 0.02f,
                minInterval = 0.08f,
                maxVoices = 2,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.UiPressDisabled,
                bus = AudioBusId.Ui,
                volume = 0.42f,
                pitchJitter = 0.03f,
                minInterval = 0.12f,
                maxVoices = 1,
                priority = 90,
            },
            new()
            {
                cue = AudioCueId.UiToggleOn,
                clipKey = nameof(AudioCueId.UiToggleOn),
                bus = AudioBusId.Ui,
                volume = 0.55f,
                minInterval = 0.05f,
                maxVoices = 2,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.UiToggleOff,
                bus = AudioBusId.Ui,
                volume = 0.5f,
                minInterval = 0.05f,
                maxVoices = 2,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.UiTabChange,
                bus = AudioBusId.Ui,
                volume = 0.6f,
                pitchJitter = 0.02f,
                minInterval = 0.06f,
                maxVoices = 2,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.UiNavigate,
                clipKey = nameof(AudioCueId.UiHover),
                bus = AudioBusId.Ui,
                volume = 0.3f,
                pitchCenter = 1.08f,
                pitchJitter = 0.03f,
                minInterval = 0.05f,
                maxVoices = 2,
                priority = 190,
            },
            new()
            {
                cue = AudioCueId.UiDialogOpen,
                bus = AudioBusId.Ui,
                volume = 0.68f,
                pitchJitter = 0.015f,
                minInterval = 0.15f,
                maxVoices = 1,
                priority = 60,
                musicDuck = 0.18f,
                musicDuckHold = 0.15f,
            },
            new()
            {
                cue = AudioCueId.UiDialogClose,
                bus = AudioBusId.Ui,
                volume = 0.6f,
                pitchJitter = 0.015f,
                minInterval = 0.15f,
                maxVoices = 1,
                priority = 65,
            },
            new()
            {
                cue = AudioCueId.UiError,
                bus = AudioBusId.Ui,
                volume = 0.7f,
                pitchJitter = 0.01f,
                minInterval = 0.25f,
                maxVoices = 1,
                priority = 45,
                musicDuck = 0.2f,
                musicDuckHold = 0.18f,
            },
            new()
            {
                cue = AudioCueId.UiConfirm,
                bus = AudioBusId.Ui,
                volume = 0.7f,
                pitchJitter = 0.015f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 55,
            },

            // === Match setup / crew selection =========================================
            new()
            {
                cue = AudioCueId.RosterPick,
                bus = AudioBusId.Ui,
                volume = 0.72f,
                pitchJitter = 0.05f,
                minInterval = 0.05f,
                maxVoices = 3,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.RosterRemove,
                bus = AudioBusId.Ui,
                volume = 0.6f,
                pitchJitter = 0.05f,
                minInterval = 0.05f,
                maxVoices = 3,
                priority = 65,
            },
            new()
            {
                cue = AudioCueId.RosterConfirm,
                bus = AudioBusId.Ui,
                volume = 0.85f,
                pitchJitter = 0.01f,
                minInterval = 0.3f,
                maxVoices = 1,
                priority = 40,
                musicDuck = 0.3f,
                musicDuckHold = 0.4f,
            },
            new()
            {
                cue = AudioCueId.RelayCodeReady,
                bus = AudioBusId.Ui,
                volume = 0.8f,
                pitchJitter = 0f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 45,
            },
            new()
            {
                cue = AudioCueId.MatchDeploy,
                volume = 0.9f,
                pitchJitter = 0.01f,
                minInterval = 1f,
                maxVoices = 1,
                priority = 25,
                musicDuck = 0.5f,
                musicDuckHold = 0.8f,
            },

            // === Round flow ===========================================================
            // The clock and the phase changes are the spine of the match. Nothing steals them.
            new()
            {
                cue = AudioCueId.PhasePlanning,
                volume = 0.9f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 20,
                musicDuck = 0.45f,
                musicDuckHold = 0.55f,
            },
            new()
            {
                cue = AudioCueId.PhaseDodge,
                volume = 0.95f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 15,
                musicDuck = 0.6f,
                musicDuckHold = 0.7f,
            },
            new()
            {
                cue = AudioCueId.PhaseDodgeResolve,
                volume = 0.7f,
                pitchJitter = 0.01f,
                minInterval = 0.3f,
                maxVoices = 1,
                priority = 30,
            },
            new()
            {
                cue = AudioCueId.PhaseExecute,
                volume = 0.92f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 20,
                musicDuck = 0.5f,
                musicDuckHold = 0.6f,
            },
            new()
            {
                cue = AudioCueId.PhaseRoundEnd,
                volume = 0.8f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 30,
                musicDuck = 0.3f,
                musicDuckHold = 0.35f,
            },
            new()
            {
                cue = AudioCueId.TimerTick,
                volume = 0.5f,
                pitchJitter = 0f,
                minInterval = 0.4f,
                maxVoices = 1,
                priority = 30,
            },
            new()
            {
                cue = AudioCueId.TimerFinal,
                volume = 0.8f,
                pitchJitter = 0f,
                minInterval = 0.4f,
                maxVoices = 1,
                priority = 25,
            },
            new()
            {
                cue = AudioCueId.MatchWin,
                volume = 1f,
                pitchJitter = 0f,
                minInterval = 2f,
                maxVoices = 1,
                priority = 10,
                musicDuck = 0.85f,
                musicDuckHold = 2.5f,
            },
            new()
            {
                cue = AudioCueId.MatchLose,
                volume = 1f,
                pitchJitter = 0f,
                minInterval = 2f,
                maxVoices = 1,
                priority = 10,
                musicDuck = 0.85f,
                musicDuckHold = 2.5f,
            },
            new()
            {
                cue = AudioCueId.MatchDraw,
                clipKey = nameof(AudioCueId.MatchLose),
                volume = 0.95f,
                pitchCenter = 0.96f,
                pitchJitter = 0f,
                minInterval = 2f,
                maxVoices = 1,
                priority = 10,
                musicDuck = 0.85f,
                musicDuckHold = 2.5f,
            },

            // === Planning interaction =================================================
            // All of these describe the local player's own orders, so none of them can leak.
            new()
            {
                cue = AudioCueId.UnitSelect,
                volume = 0.6f,
                pitchJitter = 0.05f,
                minInterval = 0.04f,
                maxVoices = 2,
                priority = 65,
            },
            new()
            {
                cue = AudioCueId.UnitDeselect,
                volume = 0.42f,
                pitchJitter = 0.05f,
                minInterval = 0.05f,
                maxVoices = 2,
                priority = 90,
            },
            new()
            {
                cue = AudioCueId.AbilityModeEnter,
                volume = 0.72f,
                pitchJitter = 0.02f,
                minInterval = 0.08f,
                maxVoices = 2,
                priority = 55,
            },
            new()
            {
                cue = AudioCueId.AbilityModeExit,
                volume = 0.55f,
                pitchJitter = 0.02f,
                minInterval = 0.08f,
                maxVoices = 2,
                priority = 80,
            },
            new()
            {
                cue = AudioCueId.PathNodeAdd,
                volume = 0.4f,
                pitchJitter = 0.07f,
                minInterval = 0.035f,
                maxVoices = 3,
                priority = 120,
            },
            new()
            {
                cue = AudioCueId.PathNodeRemove,
                volume = 0.34f,
                pitchCenter = 0.92f,
                pitchJitter = 0.06f,
                minInterval = 0.035f,
                maxVoices = 3,
                priority = 125,
            },
            new()
            {
                cue = AudioCueId.PathInvalid,
                clipKey = nameof(AudioCueId.UiError),
                volume = 0.5f,
                pitchCenter = 1.05f,
                pitchJitter = 0.02f,
                minInterval = 0.3f,
                maxVoices = 1,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.TargetConfirm,
                volume = 0.75f,
                pitchJitter = 0.015f,
                minInterval = 0.1f,
                maxVoices = 2,
                priority = 50,
            },
            new()
            {
                cue = AudioCueId.LockIn,
                volume = 0.9f,
                pitchJitter = 0.01f,
                minInterval = 0.25f,
                maxVoices = 1,
                priority = 35,
                musicDuck = 0.3f,
                musicDuckHold = 0.3f,
            },
            new()
            {
                cue = AudioCueId.LockInWaiting,
                volume = 0.55f,
                pitchJitter = 0.01f,
                minInterval = 0.4f,
                maxVoices = 1,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.Unlock,
                volume = 0.6f,
                pitchCenter = 0.94f,
                pitchJitter = 0.01f,
                minInterval = 0.25f,
                maxVoices = 1,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.AbilityReady,
                volume = 0.6f,
                pitchJitter = 0.01f,
                minInterval = 0.2f,
                maxVoices = 2,
                priority = 60,
            },

            // === Combat ===============================================================
            // Weapon fire is placed on the board and gated on fog: a muzzle report is a precise
            // position read, and the shooter may be somewhere the local team cannot see. The
            // tracer itself stays visible (an existing, deliberate fog-piercing decision), so a
            // player still gets "you are being shot from over there" from the projectile.
            new()
            {
                cue = AudioCueId.WeaponPistol,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.6f,
                pitchJitter = 0.06f,
                minInterval = 0.05f,
                maxVoices = 4,
                priority = 100,
            },
            new()
            {
                cue = AudioCueId.WeaponRifle,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.68f,
                pitchJitter = 0.05f,
                minInterval = 0.05f,
                maxVoices = 4,
                priority = 95,
            },
            // One burst, not ten pellets: the Shotgunner empties a 10-round magazine at 0.01s
            // between shots, so the cue is capped to a single voice and locked out for the burst.
            new()
            {
                cue = AudioCueId.WeaponShotgun,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.8f,
                pitchJitter = 0.05f,
                minInterval = 0.4f,
                maxVoices = 1,
                priority = 85,
                musicDuck = 0.15f,
                musicDuckHold = 0.12f,
            },
            new()
            {
                cue = AudioCueId.WeaponSniper,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.85f,
                pitchJitter = 0.02f,
                minInterval = 0.3f,
                maxVoices = 2,
                priority = 75,
                musicDuck = 0.22f,
                musicDuckHold = 0.18f,
            },
            new()
            {
                cue = AudioCueId.WeaponSmg,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.6f,
                pitchJitter = 0.07f,
                minInterval = 0.05f,
                maxVoices = 4,
                priority = 100,
            },
            // The lock laser force-reveals the sniper to the victim's team, so the charge is
            // audible exactly when the beam is drawable and never before.
            new()
            {
                cue = AudioCueId.SniperLockCharge,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.55f,
                pitchJitter = 0f,
                maxVoices = 2,
                priority = 70,
                loop = true,
            },
            // Reloads are one to two and a half seconds of every execution phase and are the
            // clearest tell that a unit is briefly defenceless. Worth hearing.
            new()
            {
                cue = AudioCueId.WeaponReloadShort,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.42f,
                pitchJitter = 0.05f,
                minInterval = 0.2f,
                maxVoices = 3,
                priority = 150,
            },
            new()
            {
                cue = AudioCueId.WeaponReloadLong,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.45f,
                pitchJitter = 0.04f,
                minInterval = 0.3f,
                maxVoices = 3,
                priority = 150,
            },
            // Deliberately fog-piercing, and the best cue in the game for it: a round passing your
            // own unit tells you that you are under fire from somewhere without naming the cell.
            // The tracer that produces it is already visible through fog by existing design.
            new()
            {
                cue = AudioCueId.BulletWhizz,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 0.5f,
                pitchJitter = 0.09f,
                minInterval = 0.07f,
                maxVoices = 3,
                priority = 85,
            },
            new()
            {
                cue = AudioCueId.ImpactBody,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.62f,
                pitchJitter = 0.08f,
                minInterval = 0.04f,
                maxVoices = 5,
                priority = 90,
            },
            new()
            {
                cue = AudioCueId.ImpactArmor,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.6f,
                pitchJitter = 0.08f,
                minInterval = 0.04f,
                maxVoices = 4,
                priority = 95,
            },
            new()
            {
                cue = AudioCueId.ImpactSurface,
                spatial = AudioSpatialMode.Positional,
                volume = 0.42f,
                pitchJitter = 0.1f,
                minInterval = 0.05f,
                maxVoices = 3,
                priority = 130,
            },
            new()
            {
                cue = AudioCueId.ShieldBlock,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.72f,
                pitchJitter = 0.05f,
                minInterval = 0.06f,
                maxVoices = 3,
                priority = 80,
            },
            // Getting hurt always reaches its owner. You must know your crew is taking fire even
            // when the shooter is dark, and it is your own unit's position, so nothing leaks.
            new()
            {
                cue = AudioCueId.DamageTaken,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.75f,
                pitchJitter = 0.06f,
                minInterval = 0.07f,
                maxVoices = 3,
                priority = 45,
                musicDuck = 0.16f,
                musicDuckHold = 0.12f,
            },
            new()
            {
                // The hole a kill leaves behind is the payoff. Bed out, impact, then quiet.
                cue = AudioCueId.UnitEliminated,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.95f,
                pitchJitter = 0.03f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 30,
                musicDuck = 0.5f,
                musicDuckHold = 0.5f,
                bedDuck = 0.45f,
            },
            new()
            {
                cue = AudioCueId.UnitSpawn,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.6f,
                pitchJitter = 0.05f,
                minInterval = 0.04f,
                maxVoices = 5,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.UnitStep,
                spatial = AudioSpatialMode.Positional,
                volume = 0.2f,
                pitchJitter = 0.11f,
                volumeJitter = 0.18f,
                minInterval = 0.055f,
                maxVoices = 5,
                priority = 200,
            },
            new()
            {
                cue = AudioCueId.UnitDive,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.7f,
                pitchJitter = 0.06f,
                minInterval = 0.08f,
                maxVoices = 3,
                priority = 60,
            },

            // === Abilities ============================================================
            // Telegraphs and dodge alerts are the counterplay window and are already shown to
            // both players through fog by design (GAME_DESIGN §6), so their audio matches.
            new()
            {
                cue = AudioCueId.AbilityTelegraph,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 0.8f,
                pitchJitter = 0.02f,
                minInterval = 0.1f,
                maxVoices = 3,
                priority = 40,
                musicDuck = 0.3f,
                musicDuckHold = 0.3f,
            },
            new()
            {
                cue = AudioCueId.DodgeAlert,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 0.95f,
                pitchJitter = 0f,
                minInterval = 0.4f,
                maxVoices = 1,
                priority = 15,
                musicDuck = 0.6f,
                musicDuckHold = 0.7f,
                bedDuck = 0.5f,
            },
            new()
            {
                cue = AudioCueId.SmokeThrow,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.6f,
                pitchJitter = 0.05f,
                minInterval = 0.1f,
                maxVoices = 2,
                priority = 85,
            },
            // The screen is public: both players are told where it lands, because it changes
            // sightlines for both of them.
            new()
            {
                cue = AudioCueId.SmokeBloom,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 0.78f,
                pitchJitter = 0.04f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 55,
                musicDuck = 0.25f,
                musicDuckHold = 0.35f,
            },
            new()
            {
                cue = AudioCueId.PogoLaunch,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.72f,
                pitchJitter = 0.04f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 70,
            },
            new()
            {
                cue = AudioCueId.PogoLand,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.88f,
                pitchJitter = 0.04f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 50,
                musicDuck = 0.28f,
                musicDuckHold = 0.25f,
            },
            new()
            {
                cue = AudioCueId.ShieldRaise,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.75f,
                pitchJitter = 0.03f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 60,
            },
            new()
            {
                cue = AudioCueId.ShieldDrop,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.55f,
                pitchJitter = 0.03f,
                minInterval = 0.15f,
                maxVoices = 2,
                priority = 100,
            },
            // Area Lock force-reveals the sniper to enemy clients for the ability window, so its
            // charge, fire and impact are audible to exactly the clients that can see the beam.
            new()
            {
                cue = AudioCueId.AreaLockCharge,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.62f,
                pitchJitter = 0f,
                maxVoices = 2,
                priority = 55,
                loop = true,
            },
            new()
            {
                cue = AudioCueId.AreaLockFire,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.9f,
                pitchJitter = 0.02f,
                minInterval = 0.2f,
                maxVoices = 2,
                priority = 45,
                musicDuck = 0.35f,
                musicDuckHold = 0.3f,
            },
            new()
            {
                cue = AudioCueId.AreaLockImpact,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.95f,
                pitchJitter = 0.02f,
                minInterval = 0.2f,
                maxVoices = 2,
                priority = 40,
                musicDuck = 0.45f,
                musicDuckHold = 0.35f,
                bedDuck = 0.4f,
            },
            // Area Lock can time out without ever firing. Silence there reads as a bug.
            new()
            {
                cue = AudioCueId.AreaLockExpire,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.55f,
                pitchJitter = 0.02f,
                minInterval = 0.2f,
                maxVoices = 2,
                priority = 90,
            },
            new()
            {
                cue = AudioCueId.GrenadeThrow,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.VisibleCellOrOwnUnit,
                volume = 0.55f,
                pitchJitter = 0.05f,
                minInterval = 0.1f,
                maxVoices = 2,
                priority = 90,
            },
            // Grenade flight and detonation already pierce fog by design, so the fuse — which is
            // the audible half of the telegraph — pierces it too.
            new()
            {
                cue = AudioCueId.GrenadeFuse,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 0.55f,
                pitchJitter = 0f,
                maxVoices = 2,
                priority = 60,
                loop = true,
            },
            new()
            {
                cue = AudioCueId.GrenadeExplode,
                spatial = AudioSpatialMode.Positional,
                visibility = AudioVisibilityRule.AlwaysAudible,
                volume = 1f,
                pitchJitter = 0.04f,
                minInterval = 0.15f,
                maxVoices = 3,
                priority = 35,
                musicDuck = 0.45f,
                musicDuckHold = 0.4f,
                bedDuck = 0.4f,
            },

            // === King of the Hill =====================================================
            new()
            {
                cue = AudioCueId.HillCaptured,
                volume = 0.85f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 35,
                musicDuck = 0.35f,
                musicDuckHold = 0.4f,
            },
            new()
            {
                cue = AudioCueId.HillLost,
                volume = 0.85f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 35,
                musicDuck = 0.35f,
                musicDuckHold = 0.4f,
            },
            new()
            {
                cue = AudioCueId.HillContested,
                volume = 0.7f,
                pitchJitter = 0.01f,
                minInterval = 0.5f,
                maxVoices = 1,
                priority = 45,
            },

            // === Bed ==================================================================
            new()
            {
                cue = AudioCueId.AmbienceBoard,
                bus = AudioBusId.Ambience,
                volume = 0.5f,
                pitchJitter = 0f,
                maxVoices = 1,
                priority = 250,
                loop = true,
            },
        };

        Dictionary<AudioCueId, AudioCueSettings> map = new(table.Length);
        foreach (AudioCueSettings settings in table)
        {
            if (map.ContainsKey(settings.cue))
            {
                Debug.LogError($"[Audio] Duplicate cue definition for {settings.cue}.");
                continue;
            }
            map[settings.cue] = settings;
        }
        return map;
    }
}
