/// <summary>
/// Every sound the game can ask for, by meaning rather than by filename. Call sites reference
/// these instead of clips so the sonic vocabulary can be re-cut without touching gameplay code.
/// Tuning for each entry (bus, gain, pitch spread, voice limit, fog rule) lives in
/// <see cref="AudioCueTable"/>; the clips themselves are bound by name in <see cref="BattlePlanAudio"/>.
/// </summary>
public enum AudioCueId
{
    None = 0,

    // === Interface ===
    UiHover,
    UiPress,
    UiPressPrimary,
    UiPressDisabled,
    UiToggleOn,
    UiToggleOff,
    UiTabChange,
    UiNavigate,
    UiDialogOpen,
    UiDialogClose,
    UiError,
    UiConfirm,

    // === Match setup / crew selection ===
    RosterPick,
    RosterRemove,
    RosterConfirm,
    RelayCodeReady,
    MatchDeploy,

    // === Round flow ===
    PhasePlanning,
    PhaseDodge,
    PhaseDodgeResolve,
    PhaseExecute,
    PhaseRoundEnd,
    TimerTick,
    TimerFinal,
    MatchWin,
    MatchLose,
    MatchDraw,

    // === Planning interaction (local player's own orders) ===
    UnitSelect,
    UnitDeselect,
    AbilityModeEnter,
    AbilityModeExit,
    PathNodeAdd,
    PathNodeRemove,
    PathInvalid,
    TargetConfirm,
    LockIn,
    LockInWaiting,
    Unlock,
    AbilityReady,

    // === Combat ===
    WeaponPistol,
    WeaponRifle,
    WeaponShotgun,
    WeaponSniper,
    WeaponSmg,
    SniperLockCharge,
    WeaponReloadShort,
    WeaponReloadLong,
    BulletWhizz,
    ImpactBody,
    ImpactArmor,
    ImpactSurface,
    ShieldBlock,
    DamageTaken,
    UnitEliminated,
    UnitSpawn,
    UnitStep,
    UnitDive,

    // === Abilities ===
    AbilityTelegraph,
    DodgeAlert,
    SmokeThrow,
    SmokeBloom,
    PogoLaunch,
    PogoLand,
    ShieldRaise,
    ShieldDrop,
    AreaLockCharge,
    AreaLockFire,
    AreaLockImpact,
    AreaLockExpire,
    GrenadeThrow,
    GrenadeFuse,
    GrenadeExplode,

    // === King of the Hill ===
    HillCaptured,
    HillLost,
    HillContested,

    // === Bed ===
    AmbienceBoard,
}

/// <summary>Mixer destination. Maps to an exposed volume parameter on the Battle Plan mixer.</summary>
public enum AudioBusId
{
    Master = 0,
    Music,
    Sfx,
    Ui,
    Ambience,
}

/// <summary>Whether a cue is heard flat in the mix or placed on the board.</summary>
public enum AudioSpatialMode
{
    /// <summary>Interface and whole-match cues. No attenuation, no panning by world position.</summary>
    Flat = 0,

    /// <summary>Placed at a world position and attenuated by distance from the team camera.</summary>
    Positional,
}

/// <summary>
/// Fog-of-war contract for a positional cue. Battle Plan hides enemy units from a client entirely,
/// so a sound played at a hidden position would hand the listener free intel. Every positional cue
/// must state which rule it plays by, and any cue that pierces fog has to be justified against the
/// fog-piercing set already agreed in <c>docs/GAME_DESIGN.md</c> §6.
/// </summary>
public enum AudioVisibilityRule
{
    /// <summary>Only audible when the local team can currently see the cell it happens in.</summary>
    RequireVisibleCell = 0,

    /// <summary>
    /// Audible when the cell is visible, and always audible when it happens to a unit on the
    /// local team. You always hear your own crew, wherever they are.
    /// </summary>
    VisibleCellOrOwnUnit,

    /// <summary>
    /// Deliberately pierces fog. Only for events the opponent is already shown by design
    /// (ability telegraphs, dodge alerts, grenades, tracers, forced reveals).
    /// </summary>
    AlwaysAudible,
}
