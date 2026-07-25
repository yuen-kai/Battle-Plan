"""Builds Battle Plan's sound-effect set into Assets/Audio/Resources/BattlePlanSfx.

Each function is one cue from `AudioCueId`, built to the character and duration in the audio
direction brief (`docs/design/ArtDirection.md` §10.4). Filenames must match the cue's `ClipKey`
exactly; `_01`, `_02`… suffixes are round-robin variations that `BattlePlanAudio` picks between.

The board is a bright, daylit tabletop of little figures, so the whole set is struck rather than
blasted: wood, plastic and tuned bars, with pitched pops where a war game would put gunpowder.
Everything is still short and dry — the board is a table, not a cathedral — and every pitch lands
on the D major grid the score uses, so the interface belongs to the music.

    python tools/audio/make_sfx.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bpsynth import (  # noqa: E402
    _unit, ar_env, at, bandpass, click, declick, exp_env, highpass, hz, lowpass, marimba, mix,
    noise, normalize, pad, plastic_knock, pluck, pop, resonator, saw, seeded, semitones, silence,
    rms_normalize, sine, soft_clip, sub_thump, servo, swell_env, sweep, woodblock, wood_tick,
    write_wav,
)

OUT = Path(__file__).resolve().parents[2] / "Assets" / "Audio" / "Resources" / "BattlePlanSfx"


# === Interface =============================================================
# Dry, tuned, tiny. No reverb at all. Every pitch lands on the D major grid so the interface
# belongs to the score.


def ui_hover():
    # Soft and filtered with no transient: this fires on every pointer move and must never bite.
    rng = seeded("UiHover")
    body = wood_tick(0.045, hz("D6"), rng, decay=0.008, bright=0.15)
    return normalize(lowpass(body, 4200.0), -19.0)


def ui_press():
    rng = seeded("UiPress")
    tick = woodblock(hz("A5"), 0.065, rng, decay=0.014, bright=0.6)
    knock = sub_thump(hz("D3"), 0.04, drop=0.7, decay=0.011) * 0.16
    return normalize(mix(tick, pad(knock, 0.065)), -8.0)


def ui_press_primary():
    rng = seeded("UiPressPrimary")
    out = silence(0.12)
    out = at(out, woodblock(hz("A5"), 0.05, rng, decay=0.014), 0.0, 1.0)
    out = at(out, sub_thump(hz("D2"), 0.09, drop=0.55, decay=0.03), 0.004, 0.7)
    out = at(out, marimba(hz("D6"), 0.09, decay=0.035, brightness=0.8), 0.012, 0.35)
    return normalize(out, -6.0)


def ui_press_disabled():
    # A soft wooden "won't budge". Damped, low, and completely unaggressive.
    rng = seeded("UiPressDisabled")
    thud = lowpass(noise(0.09, rng), 420.0) * exp_env(0.09, 0.026, attack=0.002)
    body = _unit(resonator(noise(0.09, rng) * exp_env(0.09, 0.0015), hz("D3"), 7.0))
    body = body * exp_env(0.09, 0.02) * 0.5
    return normalize(lowpass(mix(thud, body), 620.0), -12.0)


def ui_toggle(on: bool):
    name = "UiToggleOn" if on else "UiToggleOff"
    rng = seeded(name)
    first, second = (hz("D4"), hz("A4")) if on else (hz("A4"), hz("D4"))
    out = silence(0.085)
    out = at(out, wood_tick(0.03, first, rng, decay=0.008, bright=0.35), 0.0, 0.7)
    out = at(out, wood_tick(0.055, second, rng, decay=0.014, bright=0.5), 0.019, 1.0)
    out = at(out, sub_thump(hz("D3"), 0.05, drop=0.7, decay=0.014), 0.019, 0.28)
    return normalize(out, -8.5 if on else -10.0)


def ui_tab_change():
    rng = seeded("UiTabChange")
    slide = bandpass(noise(0.055, rng), 700.0, 2000.0) * ar_env(0.055, 0.006, 0.04)
    slide = slide * np.linspace(0.5, 1.0, len(slide))
    out = silence(0.075)
    out = at(out, slide, 0.0, 0.9)
    out = at(out, woodblock(hz("A4"), 0.032, rng, decay=0.008), 0.05, 0.85)
    return normalize(lowpass(out, 6000.0), -9.0)


def ui_dialog_open():
    rng = seeded("UiDialogOpen")
    out = silence(0.19)
    move = bandpass(noise(0.14, rng), 400.0, 3000.0) * (np.linspace(0.0, 1.0, 6720) ** 2.0)
    out = at(out, move, 0.0, 0.45)
    out = at(out, woodblock(hz("A5"), 0.055, rng, decay=0.017), 0.138, 1.0)
    out = at(out, sub_thump(hz("D2"), 0.06, drop=0.7, decay=0.02), 0.138, 0.4)
    return normalize(out, -7.5)


def ui_dialog_close():
    rng = seeded("UiDialogClose")
    out = silence(0.15)
    out = at(out, woodblock(hz("D5"), 0.05, rng, decay=0.014), 0.0, 0.9)
    fall = bandpass(noise(0.12, rng), 300.0, 2200.0) * (np.linspace(1.0, 0.0, 5760) ** 1.6)
    out = at(out, fall, 0.026, 0.4)
    return normalize(out, -9.0)


def ui_error():
    """
    Heard constantly, so it must read as "no" without ever being harsh. A gentle descending
    major-second on wooden bars — musical rather than a buzzer, which is the difference between a
    game correcting you and a machine scolding you.
    """
    out = silence(0.22)
    out = at(out, marimba(hz("A4"), 0.18, decay=0.07, brightness=0.5), 0.0, 0.9)
    out = at(out, marimba(hz("G4"), 0.2, decay=0.08, brightness=0.45), 0.075, 0.85)
    return normalize(lowpass(out, 3400.0), -8.0)


def ui_confirm():
    out = silence(0.15)
    out = at(out, marimba(hz("A4"), 0.1, decay=0.05), 0.0, 0.8)
    out = at(out, marimba(hz("D5"), 0.12, decay=0.06), 0.045, 1.0)
    return normalize(out, -7.0)


# === Match setup and crew selection ========================================


def roster_pick():
    rng = seeded("RosterPick")
    out = silence(0.12)
    out = at(out, woodblock(hz("A5"), 0.04, rng, decay=0.011), 0.0, 0.7)
    out = at(out, marimba(hz("A4"), 0.11, decay=0.05), 0.008, 0.8)
    return normalize(out, -7.0)


def roster_remove():
    rng = seeded("RosterRemove")
    out = silence(0.12)
    out = at(out, woodblock(hz("D5"), 0.035, rng, decay=0.009), 0.0, 0.55)
    out = at(out, marimba(hz("A4"), 0.09, decay=0.04), 0.006, 0.6)
    out = at(out, marimba(hz("D4"), 0.09, decay=0.04), 0.04, 0.5)
    return normalize(lowpass(out, 5200.0), -9.0)


def roster_confirm():
    # A rising fragment of the motif plus a soft latch: the crew is committed.
    rng = seeded("RosterConfirm")
    out = silence(0.42)
    for i, note in enumerate(["D4", "F#4", "A4"]):
        out = at(out, marimba(hz(note), 0.3, decay=0.13), i * 0.062, 0.8)
    out = at(out, sub_thump(hz("D2"), 0.2, drop=0.5, decay=0.06), 0.124, 0.65)
    out = at(out, woodblock(hz("D5"), 0.18, rng, decay=0.045), 0.124, 0.45)
    return normalize(out, -5.5)


def relay_code_ready():
    out = silence(0.24)
    out = at(out, marimba(hz("A4"), 0.22, decay=0.1), 0.0, 1.0)
    out = at(out, marimba(hz("D6"), 0.18, decay=0.07, brightness=0.8), 0.006, 0.34)
    return normalize(out, -7.0)


def match_deploy():
    # The rising swell that opens a match. Two crews arriving on a sunlit board.
    rng = seeded("MatchDeploy")
    seconds = 2.4
    pad_layer = saw(hz("D3"), seconds, harmonics=9) * 0.5 + saw(hz("A3") * 1.003, seconds, harmonics=8) * 0.35
    pad_layer = lowpass(pad_layer, sweep(240.0, 2600.0, seconds, curve=1.6)) * swell_env(seconds, 0.86)
    rise = bandpass(noise(seconds, rng), 400.0, 5000.0) * (np.linspace(0.0, 1.0, len(pad_layer)) ** 2.6)
    sub = sine(sweep(hz("D2") * 0.75, hz("D2"), seconds, curve=1.4), seconds) * swell_env(seconds, 0.9)
    out = mix(pad_layer * 0.7, rise * 0.25, sub * 0.85)
    # Lands on the tonic: the pieces are on the board.
    for i, note in enumerate(["D5", "A5"]):
        out = at(out, marimba(hz(note), 0.6, decay=0.2), 2.05 + i * 0.05, 0.85 - i * 0.2)
    out = at(out, sub_thump(hz("D2"), 0.45, drop=0.7, decay=0.14), 2.05, 0.85)
    return normalize(out, -4.0)


# === Round flow ============================================================


def phase_planning():
    # A soft up-swell into a bright bar. Time to think, and the board is inviting about it.
    rng = seeded("PhasePlanning")
    seconds = 0.9
    swell = saw(hz("D3"), seconds, harmonics=8) * 0.5 + saw(hz("A3"), seconds, harmonics=7) * 0.35
    swell = lowpass(swell, sweep(340.0, 2100.0, seconds, curve=1.5)) * swell_env(seconds, 0.7)
    whoosh = bandpass(noise(seconds, rng), 500.0, 4000.0) * swell_env(seconds, 0.65)
    out = mix(swell * 0.65, whoosh * 0.24)
    out = at(out, marimba(hz("A4"), 0.4, decay=0.16), 0.62, 0.55)
    return normalize(out, -6.0)


def phase_execute():
    # A bright downbeat and the kit arriving. The pieces start moving.
    rng = seeded("PhaseExecute")
    seconds = 1.2
    out = silence(seconds)
    out = at(out, sub_thump(hz("D2"), 0.5, drop=0.55, decay=0.16), 0.0, 0.95)
    out = at(out, woodblock(hz("D4"), 0.3, rng, decay=0.05), 0.0, 0.8)
    out = at(out, marimba(hz("D5"), 0.7, decay=0.22), 0.0, 0.7)
    open_up = bandpass(noise(0.9, rng), 250.0, 6000.0) * ar_env(0.9, 0.01, 0.7)
    open_up = lowpass(open_up, sweep(600.0, 7000.0, 0.9, curve=0.7))
    out = at(out, open_up, 0.0, 0.22)
    out = at(out, woodblock(hz("A4"), 0.2, rng, decay=0.035), 0.357, 0.45)
    out = at(out, woodblock(hz("D5"), 0.18, rng, decay=0.03), 0.714, 0.35)
    return normalize(out, -4.5)


def phase_dodge():
    # A 2 Hz alarm pulse in the 900-2400 Hz shout zone. Urgent, never painful, and tuned to the
    # score so it reads as the game talking rather than a smoke detector.
    seconds = 1.4285  # two full pulses at 84 BPM, so it loops on the beat
    t = np.arange(int(seconds * 48000)) / 48000.0
    pulse = (np.sin(2 * np.pi * 2.0 * t - np.pi / 2) * 0.5 + 0.5) ** 2.2
    tone = np.sin(2 * np.pi * hz("A5") * t) * 0.6 + np.sin(2 * np.pi * hz("D5") * t) * 0.4
    body = bandpass(tone * pulse, 900.0, 2400.0)
    sub = np.sin(2 * np.pi * hz("D3") * t) * pulse * 0.22
    return normalize(lowpass(mix(body, sub), 5000.0), -8.0)


def phase_dodge_resolve():
    rng = seeded("PhaseDodgeResolve")
    seconds = 0.3
    release = bandpass(noise(seconds, rng), 400.0, 2600.0) * (np.linspace(1.0, 0.0, int(seconds * 48000)) ** 1.8)
    out = mix(release * 0.45, pad(marimba(hz("D5"), 0.24, decay=0.09) * 0.55, seconds))
    return normalize(out, -10.0)


def phase_round_end():
    # A descending three-note resolve on wooden bars. The round is filed away, cheerfully.
    out = silence(0.72)
    for i, note in enumerate(["A5", "F#5", "D5"]):
        out = at(out, marimba(hz(note), 0.5, decay=0.17), i * 0.13, 0.9 - i * 0.08)
    out = at(out, sub_thump(hz("D3"), 0.3, drop=0.6, decay=0.1), 0.26, 0.35)
    return normalize(out, -7.5)


def timer_tick(urgent: bool):
    name = "TimerFinal" if urgent else "TimerTick"
    rng = seeded(name)
    freq = semitones(hz("A4"), 2.0) if urgent else hz("A4")
    tick = wood_tick(0.06, freq, rng, decay=0.011, bright=0.55 if urgent else 0.3)
    return normalize(lowpass(tick, 6500.0), -10.0 if urgent else -14.0)


def match_win():
    # The motif up a fourth, full band. Warm and pleased, never a military fanfare.
    rng = seeded("MatchWin")
    seconds = 3.5
    out = silence(seconds)
    for i, note in enumerate(["G4", "B4", "D5", "G5"]):
        out = at(out, marimba(hz(note), 2.2, decay=0.6, brightness=1.0), i * 0.238, 0.9)
    pad_layer = saw(hz("G2"), seconds, harmonics=10) * 0.4 + saw(hz("D3") * 1.003, seconds, harmonics=9) * 0.3
    pad_layer = lowpass(pad_layer, sweep(500.0, 3000.0, seconds, curve=0.8)) * ar_env(seconds, 0.1, 1.6)
    out = mix(out, pad_layer * 0.45)
    out = at(out, sub_thump(hz("G2"), 1.2, drop=0.7, decay=0.4), 0.0, 0.7)
    out = at(out, woodblock(hz("G5"), 0.4, rng, decay=0.08), 0.0, 0.4)
    # A little upward flourish on the tail so it ends smiling.
    for i, note in enumerate(["D5", "G5", "B5"]):
        out = at(out, marimba(hz(note), 0.8, decay=0.28, brightness=0.9), 1.9 + i * 0.11, 0.45)
    return normalize(lowpass(out, 9000.0), -4.0)


def match_lose():
    # The motif falling to the relative minor: softer, slower, a shade wistful. Not grim, and
    # never detuned — a toy board losing a round should still sound like a toy board.
    seconds = 3.0
    out = silence(seconds)
    for i, note in enumerate(["D5", "B4", "A4", "F#4"]):
        out = at(out, marimba(hz(note), 2.0, decay=0.55, brightness=0.6), i * 0.26, 0.75 - i * 0.06)
    pad_layer = saw(hz("B2"), seconds, harmonics=7) * 0.3
    pad_layer = lowpass(pad_layer, 900.0) * ar_env(seconds, 0.3, 1.8)
    drone = sine(hz("D2"), seconds) * ar_env(seconds, 0.2, 1.8) * 0.4
    return normalize(mix(out, pad_layer * 0.5, drone), -6.5)


# === Planning interaction ==================================================


def unit_select():
    rng = seeded("UnitSelect")
    out = silence(0.1)
    out = at(out, woodblock(hz("D6"), 0.04, rng, decay=0.009, bright=0.6), 0.0, 0.65)
    out = at(out, marimba(hz("A4"), 0.1, decay=0.04), 0.005, 0.85)
    return normalize(out, -8.0)


def unit_deselect():
    rng = seeded("UnitDeselect")
    out = silence(0.09)
    out = at(out, woodblock(hz("A5"), 0.03, rng, decay=0.008, bright=0.3), 0.0, 0.55)
    # A 5 kHz ceiling over a 30 ms decay left nothing above the fundamental, so the release read as
    # a bare tone. Letting the bar's second and third modes through costs no extra brightness at
    # this level and is the difference between a beep and a piece being set down.
    out = at(out, marimba(hz("D4"), 0.08, decay=0.045, brightness=0.55, rng=rng), 0.004, 0.5)
    return normalize(lowpass(out, 8500.0), -12.0)


def path_node(remove: bool):
    """
    The signature sound of the game: a tiny wooden tick, one per waypoint. The runtime walks its
    pitch up 40 cents per cell and resets when the route does, so drawing a five-cell path feels
    like winding something up. Small and warm so a long route is pleasant rather than shrill.
    """
    name = "PathNodeRemove" if remove else "PathNodeAdd"
    rng = seeded(name)
    freq = hz("D4") if remove else hz("A4")
    out = wood_tick(0.05, freq, rng, decay=0.012, bright=0.2 if remove else 0.45)
    return normalize(lowpass(out, 2800.0 if remove else 5200.0), -14.0 if remove else -12.0)


def target_confirm():
    out = silence(0.15)
    out = at(out, marimba(hz("A4"), 0.1, decay=0.05), 0.0, 0.8)
    out = at(out, marimba(hz("E5"), 0.12, decay=0.06), 0.05, 1.0)
    return normalize(out, -7.0)


def ability_mode(enter: bool):
    name = "AbilityModeEnter" if enter else "AbilityModeExit"
    rng = seeded(name)
    seconds = 0.27 if enter else 0.17
    start, end = (hz("D3"), hz("D4")) if enter else (hz("D4"), hz("D3"))
    charge = servo(seconds * 0.75, start, end, rng, brightness=0.55) * 0.55
    out = silence(seconds)
    out = at(out, charge, 0.0, 1.0)
    if enter:
        out = at(out, marimba(hz("A4"), 0.14, decay=0.05), seconds * 0.72, 0.95)
    return normalize(out, -8.0 if enter else -11.0)


def lock_in():
    # A decisive two-stage wooden latch plus a round thump. Orders are committed.
    rng = seeded("LockIn")
    seconds = 0.34
    out = silence(seconds)
    out = at(out, woodblock(hz("A4"), 0.1, rng, decay=0.026), 0.0, 0.6)
    out = at(out, woodblock(hz("D4"), 0.24, rng, decay=0.06), 0.055, 1.0)
    out = at(out, sub_thump(hz("D2"), 0.28, drop=0.6, decay=0.1), 0.055, 0.5)
    out = at(out, marimba(hz("D6"), 0.1, decay=0.03, brightness=0.7), 0.055, 0.3)
    return normalize(out, -4.5)


def unlock():
    rng = seeded("Unlock")
    seconds = 0.25
    out = silence(seconds)
    out = at(out, woodblock(hz("D4"), 0.16, rng, decay=0.045), 0.0, 0.7)
    out = at(out, woodblock(hz("A4"), 0.1, rng, decay=0.028), 0.07, 0.5)
    out = at(out, sub_thump(hz("D3"), 0.16, drop=0.75, decay=0.05), 0.0, 0.35)
    return normalize(lowpass(out, 6000.0), -9.0)


def lock_in_waiting():
    out = silence(0.22)
    out = at(out, marimba(hz("D4"), 0.18, decay=0.08, brightness=0.5), 0.0, 0.7)
    out = at(out, marimba(hz("A4"), 0.16, decay=0.07, brightness=0.5), 0.085, 0.55)
    return normalize(out, -12.0)


def ability_ready():
    out = silence(0.2)
    out = at(out, marimba(hz("A5"), 0.14, decay=0.06), 0.0, 0.8)
    out = at(out, marimba(hz("D6"), 0.16, decay=0.07), 0.055, 0.9)
    return normalize(out, -10.0)


# === Weapons ===============================================================
# Punchy and readable, never brutal. These are pieces on a board firing at each other, so the
# transient is a pitched pop with a wooden body rather than a gunpowder crack over a sub drop.
# Each archetype gets its own pitch so you can tell who is shooting without looking.


def _gun(name, seconds, note, decay, body_note, air_low, air_high, level=1.0):
    """
    A toy-gun report: a fast pitched pop, a small struck body underneath, and a short puff of air.
    The mid band carries the shot — a crack up in the top octave reads as hiss on laptop speakers
    and as fatigue over a whole execution phase.
    """
    rng = seeded(name)
    snap = pop(hz(note), seconds, rng, decay=decay)
    body = plastic_knock(hz(body_note), seconds, rng, decay=decay * 1.6) * 0.55
    puff = bandpass(noise(seconds, rng), air_low, air_high) * exp_env(seconds, decay * 0.7, attack=0.0003) * 0.3
    thump = pad(sub_thump(hz(body_note) * 0.5, min(seconds, decay * 5), drop=0.6, decay=decay * 1.4), seconds)
    out = mix(snap * level, body, puff, thump * 0.4)
    return soft_clip(out, 1.25)


def weapon_pistol():
    return normalize(_gun("WeaponPistol", 0.11, "A5", 0.018, "D4", 900.0, 4200.0), -6.0)


def weapon_rifle():
    return normalize(_gun("WeaponRifle", 0.13, "F#5", 0.022, "D4", 700.0, 3800.0), -5.5)


def weapon_smg():
    return normalize(_gun("WeaponSmg", 0.09, "D6", 0.013, "A4", 1100.0, 5200.0), -7.0)


def weapon_sniper():
    # The big one: a deep pitched pop with a long, dry wooden tail. Still no gunpowder.
    rng = seeded("WeaponSniper")
    seconds = 0.52
    snap = pop(hz("D5"), 0.18, rng, decay=0.03)
    body = plastic_knock(hz("D3"), seconds, rng, decay=0.11) * 0.8
    ring = marimba(hz("D4"), seconds, decay=0.16, brightness=0.7) * 0.4
    tail = lowpass(noise(seconds, rng), 1600.0) * exp_env(seconds, 0.14, attack=0.004) * 0.28
    thump = pad(sub_thump(hz("D2"), 0.4, drop=0.5, decay=0.11), seconds)
    return normalize(soft_clip(mix(pad(snap, seconds), body, ring, tail, thump * 0.8), 1.5), -4.0)


def weapon_shotgun():
    # One dense wooden clatter for the whole ten-pellet burst, never ten overlapping clips.
    rng = seeded("WeaponShotgun")
    seconds = 0.42
    grains = silence(seconds)
    for i in range(11):
        offset = i * 0.011 + rng.uniform(0.0, 0.004)
        grain = plastic_knock(hz("A4") * rng.uniform(0.85, 1.5), 0.07, rng, decay=0.014)
        grains = at(grains, grain, offset, 0.5 * (1.0 - i / 16.0))
    burst = pop(hz("D5"), 0.12, rng, decay=0.026)
    roar = lowpass(noise(seconds, rng), 2200.0) * exp_env(seconds, 0.07, attack=0.002) * 0.45
    thump = pad(sub_thump(hz("D2"), 0.3, drop=0.55, decay=0.09), seconds)
    return normalize(soft_clip(mix(grains, pad(burst, seconds), roar, thump * 0.75), 1.45), -4.5)


def sniper_lock_charge():
    # Exactly 2000 ms to match UnitData.targetLockDuration. A bright winding-up, not a weapon arming.
    rng = seeded("SniperLockCharge")
    seconds = 2.0
    f = sweep(hz("D3"), hz("D4"), seconds, curve=1.5)
    tone = sine(f, seconds) * 0.5 + sine(f * 2.0, seconds) * 0.22 + sine(f * 3.0, seconds) * 0.08
    shimmer = bandpass(noise(seconds, rng), 2000.0, 6000.0) * 0.1
    ramp = np.linspace(0.15, 1.0, int(seconds * 48000)) ** 1.6
    wobble = 1.0 + 0.06 * np.sin(2 * np.pi * 6.0 * np.arange(len(ramp)) / 48000.0)
    return normalize(declick((tone + shimmer) * ramp * wobble, 6.0), -11.0)


def weapon_reload(long: bool):
    name = "WeaponReloadLong" if long else "WeaponReloadShort"
    rng = seeded(name)
    seconds = 2.5 if long else 1.0
    scale = seconds / 1.0
    out = silence(seconds)
    # Magazine out, magazine in, charging handle — as moulded plastic parts, not machined steel.
    out = at(out, plastic_knock(hz("A4"), 0.14, rng, decay=0.03), 0.06 * scale, 0.55)
    out = at(out, plastic_knock(hz("D4"), 0.2, rng, decay=0.045), 0.42 * scale, 0.7)
    out = at(out, sub_thump(hz("D3"), 0.12, drop=0.7, decay=0.035), 0.42 * scale, 0.3)
    out = at(out, servo(0.16 * scale, hz("A4"), hz("D4"), rng, 0.4), 0.6 * scale, 0.25)
    out = at(out, woodblock(hz("D5"), 0.12, rng, decay=0.025), 0.86 * scale, 0.6)
    return normalize(lowpass(out, 8000.0), -12.0)


def bullet_whizz():
    # A doppler snap as a round passes one of your own units. Fog-piercing on purpose.
    rng = seeded("BulletWhizz")
    seconds = 0.14
    f = sweep(2600.0, 900.0, seconds, curve=0.6)
    body = sine(f, seconds) * 0.4
    hiss = bandpass(noise(seconds, rng), 1200.0, 5200.0) * 0.42
    env = ar_env(seconds, 0.03, 0.09, curve=2.4)
    return normalize(declick((body + hiss) * env, 3.0), -11.0)


# === Impacts ===============================================================
# Wood and plastic landing on wood and plastic. Body without brutality, and no gore anywhere.


def impact_body():
    # A soft knock into a moulded figure. Deliberately no gore: these characters are goofy.
    rng = seeded("ImpactBody")
    seconds = 0.09
    knock = plastic_knock(hz("D4"), seconds, rng, decay=0.022)
    padded = lowpass(noise(seconds, rng), 900.0) * exp_env(seconds, 0.016, attack=0.0006) * 0.55
    thump = sub_thump(hz("D3"), seconds, drop=0.6, decay=0.024)
    return normalize(mix(knock, padded, thump * 0.4), -6.5)


def impact_armor():
    # Harder and higher than a body hit: a bright plastic plate taking it.
    rng = seeded("ImpactArmor")
    seconds = 0.1
    plate = plastic_knock(hz("A5"), seconds, rng, decay=0.018)
    ring = marimba(hz("A5"), seconds, decay=0.03, brightness=0.8, rng=rng) * 0.35
    # Without this the two tuned layers landed on an exact octave and the hit measured as a pair of
    # pure partials. A plate struck hard also rattles, and that scatter is what sells it as armour
    # rather than a note.
    rattle = bandpass(noise(seconds, rng), 3200.0, 11000.0) * exp_env(seconds, 0.012, attack=0.0006) * 0.2
    thump = sub_thump(hz("D3"), seconds, drop=0.65, decay=0.02)
    return normalize(mix(plate, ring, rattle, thump * 0.5), -6.5)


def impact_surface():
    # A round burying itself in the scenery. Wooden, with a little dust.
    rng = seeded("ImpactSurface")
    seconds = 0.1
    tick = woodblock(hz("D4"), seconds, rng, decay=0.011, bright=0.8)
    debris = bandpass(noise(seconds, rng), 2500.0, 8000.0) * exp_env(seconds, 0.010, attack=0.001) * 0.12
    dust = lowpass(noise(seconds, rng), 700.0) * exp_env(seconds, 0.022) * 0.45
    return normalize(mix(tick, debris, dust), -10.0)


def shield_block():
    # A bright ping: the round simply did not get through.
    rng = seeded("ShieldBlock")
    seconds = 0.12
    ping = marimba(hz("D6"), seconds, decay=0.045, brightness=1.0)
    plate = plastic_knock(hz("A5"), seconds, rng, decay=0.016) * 0.45
    return normalize(mix(ping, plate), -6.0)


def damage_taken():
    # Your crew, taking fire. A knock plus a short tuned partial so it reads as information
    # rather than just an impact — this is the cue that makes you look.
    rng = seeded("DamageTaken")
    seconds = 0.16
    out = silence(seconds)
    out = at(out, plastic_knock(hz("D4"), 0.1, rng, decay=0.024), 0.0, 0.85)
    out = at(out, lowpass(noise(0.1, rng), 800.0) * exp_env(0.1, 0.02, attack=0.0006), 0.0, 0.5)
    out = at(out, sub_thump(hz("D3"), 0.13, drop=0.55, decay=0.035), 0.0, 0.42)
    out = at(out, marimba(hz("C5"), 0.12, decay=0.04, brightness=0.6), 0.008, 0.4)
    return normalize(out, -5.0)


def unit_eliminated():
    """
    A piece leaving the board: a little wooden tumble and a soft settle. No scream and no gore —
    these characters are goofy, and a realistic death would be genuinely unpleasant. The falling
    three-note figure is the same shape as the round-end resolve, one octave down.
    """
    rng = seeded("UnitEliminated")
    seconds = 0.7
    out = silence(seconds)
    for i, note in enumerate(["A4", "F#4", "D4"]):
        out = at(out, marimba(hz(note), 0.42, decay=0.13, brightness=0.55), i * 0.085, 0.7 - i * 0.08)
    # The figure toppling over, then coming to rest.
    for i in range(3):
        out = at(out, plastic_knock(hz("D5") * (0.9 - 0.12 * i), 0.12, rng, decay=0.02),
                 0.3 + i * 0.055, 0.34 - i * 0.08)
    out = at(out, sub_thump(hz("D2"), 0.3, drop=0.6, decay=0.1), 0.3, 0.6)
    out = at(out, lowpass(noise(0.2, rng), 600.0) * exp_env(0.2, 0.045, attack=0.004), 0.32, 0.35)
    return normalize(out, -5.0)


def unit_spawn():
    # A piece being set down on the board.
    rng = seeded("UnitSpawn")
    seconds = 0.3
    out = silence(seconds)
    materialise = bandpass(noise(0.18, rng), 600.0, 4200.0) * (np.linspace(0.0, 1.0, 8640) ** 2.2)
    out = at(out, materialise, 0.0, 0.28)
    out = at(out, woodblock(hz("D4"), 0.22, rng, decay=0.05), 0.17, 0.85)
    out = at(out, sub_thump(hz("D3"), 0.2, drop=0.6, decay=0.06), 0.17, 0.6)
    out = at(out, marimba(hz("D5"), 0.2, decay=0.06, brightness=0.7), 0.17, 0.3)
    return normalize(out, -8.0)


def unit_step(variant: int):
    """
    Light and toy-like: a small wooden foot on a board. Four round-robin variants.

    Matched on RMS rather than on peak. Variants differ in pitch and therefore in crest factor, so
    peak-normalising them leaves a spread of several dB — and because these play back-to-back as a
    unit walks, that spread reads as a limp rather than as variation.
    """
    rng = seeded(f"UnitStep_{variant:02d}")
    seconds = 0.075
    body = woodblock(hz("D4") * (1.0 + 0.055 * variant), seconds, rng, decay=0.012, bright=0.45)
    tap = lowpass(noise(seconds, rng), 1400.0 + variant * 220.0) * exp_env(seconds, 0.009, attack=0.0004)
    return rms_normalize(mix(body * 0.9, tap * 0.45), -34.0, peak_ceiling_db=-9.0)


def unit_dive():
    rng = seeded("UnitDive")
    seconds = 0.24
    scramble = silence(seconds)
    for i in range(4):
        scramble = at(scramble, unit_step(i) * 0.9, 0.012 + i * 0.035, 1.0)
    whoosh = bandpass(noise(seconds, rng), 500.0, 3600.0) * ar_env(seconds, 0.03, 0.16) * 0.35
    return normalize(mix(scramble, whoosh), -8.0)


# === Abilities =============================================================


def smoke_throw():
    rng = seeded("SmokeThrow")
    seconds = 0.19
    toss = bandpass(noise(seconds, rng), 700.0, 4000.0) * ar_env(seconds, 0.015, 0.13) * 0.5
    clack = plastic_knock(hz("A4"), 0.1, rng, decay=0.02)
    out = silence(seconds)
    out = at(out, clack, 0.0, 0.7)
    out = at(out, toss, 0.01, 1.0)
    return normalize(out, -11.0)


def smoke_bloom():
    # A pressurised hiss blooming outward. Public information: both players get it.
    rng = seeded("SmokeBloom")
    seconds = 0.95
    hiss = bandpass(noise(seconds, rng), 900.0, 7000.0)
    env = np.concatenate([
        np.linspace(0.0, 1.0, 2400) ** 0.4,
        np.linspace(1.0, 0.12, int(seconds * 48000) - 2400) ** 1.3,
    ])
    body = lowpass(noise(seconds, rng), sweep(2600.0, 500.0, seconds, curve=0.7)) * env * 0.5
    burst = plastic_knock(hz("D5"), 0.14, rng, decay=0.028)
    out = mix(hiss * env * 0.6, body)
    out = at(out, burst, 0.0, 0.6)
    return normalize(out, -8.0)


def pogo_launch():
    # Spring compression and release. Unashamedly springy — it is a pogo stick.
    rng = seeded("PogoLaunch")
    seconds = 0.27
    spring = sine(sweep(hz("D3"), hz("D5"), seconds, curve=1.8), seconds) * ar_env(seconds, 0.02, 0.18)
    boing = pluck(hz("D4"), 0.2, rng, decay=0.07, damping=0.3) * 0.5
    compress = lowpass(noise(seconds, rng), sweep(600.0, 3000.0, seconds)) * ar_env(seconds, 0.03, 0.2) * 0.3
    out = mix(spring * 0.5, pad(boing, seconds), compress)
    return normalize(out, -8.0)


def pogo_land():
    rng = seeded("PogoLand")
    seconds = 0.35
    out = silence(seconds)
    out = at(out, sub_thump(hz("D2"), 0.28, drop=0.5, decay=0.09), 0.0, 0.62)
    out = at(out, woodblock(hz("D4"), 0.3, rng, decay=0.06), 0.0, 0.8)
    for i in range(3):
        out = at(out, plastic_knock(hz("D5"), 0.1, rng, decay=0.018), 0.1 + i * 0.045, 0.22)
    return normalize(soft_clip(out, 1.3), -4.5)


def shield_raise():
    rng = seeded("ShieldRaise")
    seconds = 0.43
    snap = bandpass(noise(0.12, rng), 1200.0, 7000.0) * exp_env(0.12, 0.022, attack=0.0005)
    energy = sine(sweep(hz("D3"), hz("D4"), 0.3, curve=1.3), 0.3) * ar_env(0.3, 0.02, 0.2) * 0.45
    out = silence(seconds)
    out = at(out, energy, 0.0, 1.0)
    out = at(out, snap, 0.0, 0.5)
    out = at(out, woodblock(hz("D4"), 0.22, rng, decay=0.05), 0.27, 0.8)
    out = at(out, marimba(hz("A5"), 0.2, decay=0.06, brightness=0.9), 0.27, 0.45)
    out = at(out, sub_thump(hz("D3"), 0.18, drop=0.7, decay=0.05), 0.27, 0.45)
    return normalize(out, -7.0)


def shield_drop():
    rng = seeded("ShieldDrop")
    seconds = 0.25
    fall = sine(sweep(hz("D4"), hz("D3"), seconds, curve=0.7), seconds) * ar_env(seconds, 0.01, 0.2) * 0.5
    air_out = bandpass(noise(seconds, rng), 400.0, 2600.0) * ar_env(seconds, 0.01, 0.2) * 0.28
    return normalize(mix(fall, air_out), -12.0)


def area_lock_charge():
    # A rising resonant hum with a 1.1 Hz pulse, matched to the beam.
    rng = seeded("AreaLockCharge")
    seconds = 3.0
    t = np.arange(int(seconds * 48000)) / 48000.0
    f = sweep(hz("D3"), hz("D4"), seconds, curve=1.4)
    tone = sine(f, seconds) * 0.45 + sine(f * 1.5, seconds) * 0.22 + sine(f * 2.5, seconds) * 0.1
    pulse = 0.72 + 0.28 * (np.sin(2 * np.pi * 1.1 * t - np.pi / 2) * 0.5 + 0.5)
    shimmer = bandpass(noise(seconds, rng), 2600.0, 6500.0) * 0.03
    ramp = np.linspace(0.25, 1.0, len(t)) ** 1.3
    return normalize(declick((tone + shimmer) * pulse * ramp, 8.0), -10.0)


def area_lock_fire():
    # The loudest sound in the game: a bright discharge with real punch. Big and exciting rather
    # than searing — this is the board's showpiece, not an artillery strike.
    rng = seeded("AreaLockFire")
    seconds = 0.7
    zap = sine(sweep(hz("D6"), hz("D4"), 0.3, curve=0.45), 0.3) * exp_env(0.3, 0.09) * 0.6
    sear = bandpass(noise(seconds, rng), 900.0, 8000.0) * exp_env(seconds, 0.07, attack=0.0004) * 0.55
    ring = marimba(hz("D5"), seconds, decay=0.2, brightness=1.0) * 0.5
    drop = sub_thump(hz("D3"), 0.55, drop=0.32, decay=0.16)
    return normalize(soft_clip(mix(pad(zap, seconds), sear, ring, pad(drop, seconds) * 0.9), 1.7), -3.0)


def area_lock_impact():
    rng = seeded("AreaLockImpact")
    seconds = 0.5
    out = silence(seconds)
    out = at(out, sub_thump(hz("D2"), 0.4, drop=0.45, decay=0.13), 0.0, 0.6)
    out = at(out, bandpass(noise(0.2, rng), 700.0, 7000.0) * exp_env(0.2, 0.032, attack=0.0003), 0.0, 0.6)
    out = at(out, woodblock(hz("D4"), 0.4, rng, decay=0.08), 0.0, 0.7)
    out = at(out, marimba(hz("D5"), 0.4, decay=0.13, brightness=0.9), 0.0, 0.45)
    return normalize(soft_clip(out, 1.5), -3.5)


def area_lock_expire():
    rng = seeded("AreaLockExpire")
    seconds = 0.3
    decharge = sine(sweep(hz("D4"), hz("D3"), seconds, curve=0.6), seconds) * ar_env(seconds, 0.01, 0.24) * 0.55
    fizz = bandpass(noise(seconds, rng), 1200.0, 4500.0) * ar_env(seconds, 0.01, 0.26) * 0.2
    return normalize(mix(decharge, fizz), -12.0)


def grenade_throw():
    rng = seeded("GrenadeThrow")
    seconds = 0.17
    toss = bandpass(noise(seconds, rng), 500.0, 3200.0) * ar_env(seconds, 0.02, 0.13) * 0.45
    pin = plastic_knock(hz("D5"), 0.08, rng, decay=0.016)
    out = silence(seconds)
    out = at(out, pin, 0.0, 0.6)
    out = at(out, toss, 0.008, 1.0)
    return normalize(out, -12.0)


def grenade_fuse():
    # An accelerating tick across one second, synchronised to the 2->9 Hz visual blink.
    rng = seeded("GrenadeFuse")
    seconds = 1.0
    out = silence(seconds)
    position, rate = 0.0, 2.0
    while position < seconds - 0.03:
        out = at(out, wood_tick(0.04, hz("D5"), rng, decay=0.007, bright=0.7), position, 0.8)
        position += 1.0 / rate
        rate = min(rate * 1.35, 9.0)
    return normalize(declick(lowpass(out, 7000.0), 2.0), -10.0)


def grenade_explode():
    # A big round pop with a wooden clatter tail. Fog-piercing by existing design.
    rng = seeded("GrenadeExplode")
    seconds = 1.1
    blast = lowpass(noise(seconds, rng), sweep(3500.0, 300.0, seconds, curve=0.5))
    blast = blast * exp_env(seconds, 0.11, attack=0.0006)
    burst = pop(hz("D4"), 0.2, rng, decay=0.05)
    boom = sub_thump(hz("D2") * 0.9, 0.8, drop=0.42, decay=0.22)
    debris = silence(seconds)
    for i in range(9):
        debris = at(
            debris,
            plastic_knock(hz("D5") * rng.uniform(0.7, 1.8), 0.2, rng, decay=0.03),
            0.04 + rng.uniform(0.0, 0.42),
            rng.uniform(0.1, 0.3),
        )
    out = mix(blast * 0.8, pad(burst, seconds) * 0.9, pad(boom, seconds) * 0.6, debris * 0.6)
    return normalize(soft_clip(out, 1.6), -2.5)


# === Alerts and objective ==================================================


def ability_telegraph():
    # Something is coming, and where. Deliberately pierces fog: the visual telegraph already does.
    rng = seeded("AbilityTelegraph")
    seconds = 0.42
    out = silence(seconds)
    out = at(out, marimba(hz("C5"), 0.3, decay=0.11, brightness=1.0), 0.0, 0.9)
    out = at(out, marimba(hz("F#4"), 0.32, decay=0.12, brightness=0.8), 0.09, 0.7)
    out = at(out, bandpass(noise(0.2, rng), 1400.0, 4800.0) * exp_env(0.2, 0.05, attack=0.004), 0.0, 0.2)
    return normalize(out, -6.5)


def dodge_alert():
    # Two urgent pulses. Paired with the on-screen alert icon, never the only channel.
    seconds = 0.9
    t = np.arange(int(seconds * 48000)) / 48000.0
    pulse = (np.sin(2 * np.pi * 2.2 * t - np.pi / 2) * 0.5 + 0.5) ** 2.4
    tone = np.sin(2 * np.pi * hz("B5") * t) * 0.55 + np.sin(2 * np.pi * hz("F#5") * t) * 0.45
    body = bandpass(tone * pulse, 900.0, 2600.0)
    sub = np.sin(2 * np.pi * hz("D3") * t) * pulse * 0.26
    return normalize(declick(mix(body, sub) * ar_env(seconds, 0.005, 0.16), 3.0), -4.0)


def hill_captured():
    out = silence(0.5)
    for i, note in enumerate(["D4", "F#4", "A4", "D5"]):
        out = at(out, marimba(hz(note), 0.34, decay=0.13), i * 0.075, 0.8)
    out = at(out, sub_thump(hz("D3"), 0.24, drop=0.6, decay=0.08), 0.225, 0.45)
    return normalize(out, -6.0)


def hill_lost():
    out = silence(0.5)
    for i, note in enumerate(["D5", "A4", "F#4", "D4"]):
        out = at(out, marimba(hz(note), 0.34, decay=0.13, brightness=0.6), i * 0.075, 0.7)
    return normalize(lowpass(out, 6000.0), -8.0)


def hill_contested():
    # An unsettled two-tone: a major second that refuses to resolve either way.
    out = silence(0.36)
    out = at(out, marimba(hz("A4"), 0.28, decay=0.1, brightness=0.8), 0.0, 0.8)
    out = at(out, marimba(hz("B4"), 0.28, decay=0.1, brightness=0.8), 0.055, 0.75)
    return normalize(out, -8.5)


def ambience_board():
    """
    Almost nothing: the room tone of a bright room with a game laid out in it, looping at 12 s.
    Stereo with decorrelated noise beds — a mono room tone collapses to a point source and stops
    reading as a room. The low warmth stays centred so it survives a mono fold-down.
    """
    from bpsynth import loop_seamless

    rng = seeded("AmbienceBoard")
    render = 13.0
    warmth = sine(hz("D2"), render) * 0.22 + sine(hz("D3"), render) * 0.1
    warmth = warmth * (1.0 + 0.08 * sine(0.07, render)) * 0.5

    channels = []
    for side in range(2):
        side_rng = seeded(f"AmbienceBoard_{side}")
        room = lowpass(noise(render, side_rng), 420.0) * 0.45
        airy = bandpass(noise(render, side_rng), 1800.0, 5200.0) * 0.015
        channel = mix(warmth, room, airy)
        # The occasional tiny wooden settle, as if a piece shifted on the board.
        for _ in range(4):
            settle = woodblock(hz("D5") * side_rng.uniform(0.6, 1.6), 0.3, side_rng, decay=0.05)
            channel = at(channel, settle, side_rng.uniform(0.5, render - 1.5), side_rng.uniform(0.02, 0.045))
        channels.append(loop_seamless(channel, 1.0))

    length = min(len(channels[0]), len(channels[1]))
    return normalize(np.stack([channels[0][:length], channels[1][:length]], axis=-1), -22.0)


def cents_of(note: str, amount: float) -> float:
    from bpsynth import cents

    return cents(hz(note), amount)


# === Manifest ==============================================================

CLIPS = {
    "UiHover": ui_hover,
    "UiPress": ui_press,
    "UiPressPrimary": ui_press_primary,
    "UiPressDisabled": ui_press_disabled,
    "UiToggleOn": lambda: ui_toggle(True),
    "UiToggleOff": lambda: ui_toggle(False),
    "UiTabChange": ui_tab_change,
    "UiDialogOpen": ui_dialog_open,
    "UiDialogClose": ui_dialog_close,
    "UiError": ui_error,
    "UiConfirm": ui_confirm,
    "RosterPick": roster_pick,
    "RosterRemove": roster_remove,
    "RosterConfirm": roster_confirm,
    "RelayCodeReady": relay_code_ready,
    "MatchDeploy": match_deploy,
    "PhasePlanning": phase_planning,
    "PhaseDodge": phase_dodge,
    "PhaseDodgeResolve": phase_dodge_resolve,
    "PhaseExecute": phase_execute,
    "PhaseRoundEnd": phase_round_end,
    "TimerTick": lambda: timer_tick(False),
    "TimerFinal": lambda: timer_tick(True),
    "MatchWin": match_win,
    "MatchLose": match_lose,
    "UnitSelect": unit_select,
    "UnitDeselect": unit_deselect,
    "AbilityModeEnter": lambda: ability_mode(True),
    "AbilityModeExit": lambda: ability_mode(False),
    "PathNodeAdd": lambda: path_node(False),
    "PathNodeRemove": lambda: path_node(True),
    "TargetConfirm": target_confirm,
    "LockIn": lock_in,
    "LockInWaiting": lock_in_waiting,
    "Unlock": unlock,
    "AbilityReady": ability_ready,
    "WeaponPistol": weapon_pistol,
    "WeaponRifle": weapon_rifle,
    "WeaponShotgun": weapon_shotgun,
    "WeaponSniper": weapon_sniper,
    "WeaponSmg": weapon_smg,
    "SniperLockCharge": sniper_lock_charge,
    "WeaponReloadShort": lambda: weapon_reload(False),
    "WeaponReloadLong": lambda: weapon_reload(True),
    "BulletWhizz": bullet_whizz,
    "ImpactBody": impact_body,
    "ImpactArmor": impact_armor,
    "ImpactSurface": impact_surface,
    "ShieldBlock": shield_block,
    "DamageTaken": damage_taken,
    "UnitEliminated": unit_eliminated,
    "UnitSpawn": unit_spawn,
    "UnitStep_01": lambda: unit_step(0),
    "UnitStep_02": lambda: unit_step(1),
    "UnitStep_03": lambda: unit_step(2),
    "UnitStep_04": lambda: unit_step(3),
    "UnitDive": unit_dive,
    "AbilityTelegraph": ability_telegraph,
    "DodgeAlert": dodge_alert,
    "SmokeThrow": smoke_throw,
    "SmokeBloom": smoke_bloom,
    "PogoLaunch": pogo_launch,
    "PogoLand": pogo_land,
    "ShieldRaise": shield_raise,
    "ShieldDrop": shield_drop,
    "AreaLockCharge": area_lock_charge,
    "AreaLockFire": area_lock_fire,
    "AreaLockImpact": area_lock_impact,
    "AreaLockExpire": area_lock_expire,
    "GrenadeThrow": grenade_throw,
    "GrenadeFuse": grenade_fuse,
    "GrenadeExplode": grenade_explode,
    "HillCaptured": hill_captured,
    "HillLost": hill_lost,
    "HillContested": hill_contested,
    "AmbienceBoard": ambience_board,
}


def main() -> None:
    only = set(sys.argv[1:])
    report = []
    for name, build in CLIPS.items():
        if only and name not in only:
            continue
        data = build()
        report.append(write_wav(OUT / f"{name}.wav", np.asarray(data, dtype=np.float64)))
        print(f"{report[-1]['name']:<20} {report[-1]['seconds']:>6.3f}s  "
              f"peak {report[-1]['peak_db']:>6.2f} dB  rms {report[-1]['rms_db']:>7.2f} dB")

    (Path(__file__).parent / "sfx_report.json").write_text(json.dumps(report, indent=2))
    print(f"\n{len(report)} clip(s) -> {OUT}")


if __name__ == "__main__":
    main()
