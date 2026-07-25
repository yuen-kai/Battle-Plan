"""Builds Battle Plan's score into Assets/Audio/Resources/BattlePlanMusic.

Six seamless loops, all locked to 84 BPM in D major so the runtime can crossfade between any two of
them and so every interface sound in `make_sfx.py` sits in the same key.

The board is a bright, daylit tabletop of little figures, so the score is buoyant rather than
brooding: struck wooden bars instead of metallic FM bells, a plucked round bass that bounces instead
of a sub heartbeat that drones, warm major pads instead of detuned saws, and a light wooden kit
instead of contact-mic'd metal. The tempo grid stays at 84 BPM — the interface is quantised to its
1/8 note, and that contract is worth keeping — but the percussion drives in sixteenths, which is
what carries the lift without speeding the board up.

Loop lengths are exact bar multiples, and each track is rendered one crossfade longer than its final
length so the tail can be folded back over the head with no seam.

    python tools/audio/make_music.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bpsynth import (  # noqa: E402
    BAR, BEAT, SAMPLE_RATE, ar_env, at, bandpass, cents, exp_env, highpass, hz, lowpass,
    loop_seamless, marimba, mix, n_samples, noise, pad, pluck, resonator, rms_normalize, saw,
    seeded, silence, sine, soft_clip, sub_thump, woodblock, write_wav,
)

OUT = Path(__file__).resolve().parents[2] / "Assets" / "Audio" / "Resources" / "BattlePlanMusic"

# Exactly two sixteenths. An arbitrary crossfade length lands mid-subdivision, so the folded tail
# arrives against different percussion than the head and the join steps in level — which is what
# the Execute bed, the only one running a continuous sixteenth shaker, was doing at 0.35 s.
CROSSFADE = BEAT / 2.0

# I - IV - vi - V in D major. Unlike the old non-resolving minor loop this one lands, which is what
# makes it feel like a game you are enjoying rather than an operation you are enduring.
PROGRESSION = [
    ("D4", "F#4", "A4"),
    ("G3", "B3", "D4"),
    ("B3", "D4", "F#4"),
    ("A3", "C#4", "E4"),
]
ROOTS = ["D2", "G1", "B1", "A1"]

# D major pentatonic. No semitone clashes, so it stays sweet under any chord in the progression.
MOTIF = ["D5", "F#5", "A5", "B5"]
PENTATONIC = ["D", "E", "F#", "A", "B"]


def chord_for_bar(bar: int):
    return PROGRESSION[bar % len(PROGRESSION)]


def root_for_bar(bar: int) -> str:
    return ROOTS[bar % len(ROOTS)]


# --- layers ----------------------------------------------------------------


def bounce_bass(bars: int, seconds: float, gain: float = 1.0, busy: bool = False) -> np.ndarray:
    """
    The bounce. A short plucked round bass on 1 and 3 with a skip into the next bar — the tabletop
    equivalent of the old sub heartbeat, but it walks instead of throbbing.
    """
    out = silence(seconds)
    rng = seeded("BounceBass")
    for bar in range(bars + 1):
        root = hz(root_for_bar(bar))
        hits = [(0.0, 1.0), (2.0, 0.78)]
        if busy:
            hits += [(1.5, 0.4), (3.5, 0.5)]
        for beat, level in hits:
            when = bar * BAR + beat * BEAT
            if when >= seconds:
                break
            length = BEAT * (0.75 if busy else 1.05)
            note = pluck(root, length, rng, decay=0.16 if busy else 0.24, damping=0.35)
            # A little sine under the pluck so it still reads on a phone speaker.
            note = note + pad(sine(root, length) * exp_env(length, 0.09, attack=0.004), length) * 0.45
            out = at(out, note, when, gain * level)
    return lowpass(out, 900.0)


def warm_pad(bars: int, seconds: float, brightness: float, rng) -> np.ndarray:
    """
    Sunlight on the board. Soft major triads on a gently filtered saw with far fewer harmonics than
    the old detuned pad, so it reads as warm air rather than as analogue grain.
    """
    left = silence(seconds)
    right = silence(seconds)
    for bar in range(bars + 1):
        when = bar * BAR
        if when >= seconds:
            break
        length = min(BAR * 1.02, seconds - when)
        if length <= 0.05:
            break
        for index, note in enumerate(chord_for_bar(bar)):
            base = hz(note)
            voice_a = saw(cents(base, -4), length, harmonics=7)
            voice_b = saw(cents(base, +4), length, harmonics=7)
            env = ar_env(length, BEAT * 1.1, BEAT * 1.5, curve=1.4)
            spread = 0.5 + 0.25 * index
            left = at(left, voice_a * env, when, 0.3 * spread)
            right = at(right, voice_b * env, when, 0.3 * (1.5 - spread))

    lfo = 1.0 + 0.28 * np.sin(2 * np.pi * np.arange(n_samples(seconds)) / SAMPLE_RATE / 9.0)
    cutoff = np.clip(brightness * lfo, 400.0, 7000.0)
    left = bandpass(lowpass(left, cutoff), 240.0, 6000.0)
    right = bandpass(lowpass(right, cutoff), 240.0, 6000.0)
    return np.stack([left, right], axis=-1)


def marimba_motif(seconds: float, events, gain: float = 0.55, decay: float = 0.32) -> np.ndarray:
    """The melodic voice: struck wooden bars. `events` is a list of (time, note, level)."""
    out = silence(seconds)
    for when, note, level in events:
        if when >= seconds:
            continue
        length = min(decay * 4.0, seconds - when)
        if length <= 0.05:
            continue
        out = at(out, marimba(hz(note), length, decay=decay, brightness=0.9), when, gain * level)
    return out


def wood_kit(bars: int, seconds: float, rng, busy: bool = True) -> np.ndarray:
    """
    A tabletop kit: a soft round kick, a woodblock backbeat, a rim on the offbeats and a shaker in
    sixteenths. The sixteenths are what make an 84 BPM bed feel light on its feet.
    """
    out = silence(seconds)
    for bar in range(bars + 1):
        base = bar * BAR
        if base >= seconds:
            break
        out = at(out, _kick(rng), base, 0.5)
        out = at(out, _kick(rng), base + 2.5 * BEAT, 0.32)
        for beat in (1, 3):
            out = at(out, woodblock(hz("D5"), 0.09, rng, decay=0.03), base + beat * BEAT, 0.34)
        if busy:
            for step in range(16):
                when = base + step * BEAT / 4
                if step % 4 == 0:
                    continue
                level = 0.075 if step % 2 else 0.11
                out = at(out, _shaker(rng), when, level)
    return highpass(out, 55.0)


def _kick(rng) -> np.ndarray:
    """Round and soft — a padded mallet on a small drum, not a club kick."""
    body = sub_thump(hz("D2"), 0.16, drop=0.62, decay=0.055)
    tap = lowpass(noise(0.02, rng), 1800.0) * exp_env(0.02, 0.004) * 0.2
    return lowpass(mix(body, pad(tap, 0.16)), 700.0)


def _shaker(rng) -> np.ndarray:
    return bandpass(noise(0.035, rng), 5000.0, 11000.0) * exp_env(0.035, 0.006, attack=0.0008)


def pentatonic_figure(bars: int, seconds: float, rng) -> np.ndarray:
    """A bright repeating figure in D major pentatonic. Motion with a smile, not a cold ostinato."""
    pattern = ["D5", "A4", "F#5", "A4", "B5", "A5", "F#5", "E5"]
    out = silence(seconds)
    for bar in range(bars + 1):
        for step, note in enumerate(pattern):
            when = bar * BAR + step * BEAT / 2
            if when >= seconds:
                break
            tone = marimba(hz(note), 0.26, decay=0.09, brightness=0.7)
            out = at(out, tone, when, 0.15)
    return bandpass(out, 320.0, 6500.0)


def room_texture(seconds: float, rng, level: float = 0.05) -> np.ndarray:
    """Warm air over a lit table. Keeps the beds from sounding sterile without adding grit."""
    bed = lowpass(noise(seconds, rng), 520.0) * level
    shimmer = bandpass(noise(seconds, rng), 2600.0, 7000.0) * level * 0.22
    return bed + shimmer


def to_stereo(mono: np.ndarray, width: float = 0.0) -> np.ndarray:
    """
    Width by a short inter-channel delay. Capped at 8 ms and never used on the bass layers: a
    longer delay comb-filters badly the moment the mix is folded to mono, which is exactly what
    happens on a phone speaker.
    """
    if mono.ndim == 2:
        return mono
    width = min(width, 8.0)
    if width <= 0.0:
        return np.stack([mono, mono], axis=-1)
    delay = n_samples(width / 1000.0)
    right = np.concatenate([np.zeros(delay), mono[:-delay]]) if delay else mono
    return np.stack([mono, right], axis=-1)


def stack(*layers: np.ndarray) -> np.ndarray:
    length = max(len(layer) for layer in layers)
    out = np.zeros((length, 2))
    for layer in layers:
        stereo = to_stereo(layer) if layer.ndim == 1 else layer
        out[: len(stereo)] += stereo
    return out


# --- tracks ----------------------------------------------------------------


def track_planning():
    """Bed only. Bass bounce and pad, no kit at all — thinking music, but sunlit and unhurried."""
    bars, rng = 16, seeded("Planning")
    seconds = bars * BAR + CROSSFADE
    events = [(bar * BAR + BEAT * 2, MOTIF[i % 4], 0.55 - 0.1 * (i % 2))
              for i, bar in enumerate(range(3, bars + 1, 4))]
    body = stack(
        to_stereo(bounce_bass(bars, seconds, 0.9)),
        warm_pad(bars, seconds, brightness=1400.0, rng=rng) * 0.8,
        to_stereo(marimba_motif(seconds, events, gain=0.42, decay=0.4), width=7.0),
        to_stereo(room_texture(seconds, rng, 0.03)),
    )
    return _finish(body, -26.0)


def track_execute():
    """Same key and tempo, the wooden kit in and the figure running. Crossfades from Planning."""
    bars, rng = 16, seeded("Execute")
    seconds = bars * BAR + CROSSFADE
    body = stack(
        to_stereo(bounce_bass(bars, seconds, 1.0, busy=True)),
        warm_pad(bars, seconds, brightness=3000.0, rng=rng) * 0.6,
        to_stereo(wood_kit(bars, seconds, rng) * 0.85, width=6.0),
        to_stereo(pentatonic_figure(bars, seconds, rng) * 0.8, width=8.0),
        to_stereo(room_texture(seconds, rng, 0.025)),
    )
    return _finish(body, -22.0)


def track_title():
    """The motif stated plainly with room around it. Inviting, and it resolves."""
    bars, rng = 16, seeded("Title")
    seconds = bars * BAR + CROSSFADE
    events = []
    for phrase_start in (0, 8):
        for i, note in enumerate(MOTIF):
            events.append((phrase_start * BAR + i * BEAT * 1.5, note, 0.9 - 0.08 * i))
        events.append((phrase_start * BAR + 6 * BEAT, "D6", 0.5))
    body = stack(
        to_stereo(bounce_bass(bars, seconds, 0.85)),
        warm_pad(bars, seconds, brightness=2000.0, rng=rng) * 0.75,
        to_stereo(marimba_motif(seconds, events, gain=0.62, decay=0.5), width=8.0),
        to_stereo(room_texture(seconds, rng, 0.035)),
    )
    for bar in (4, 12):
        chime = marimba(hz("D4"), 0.9, decay=0.34, brightness=1.0)
        body[:, 0] = at(body[:, 0], chime, bar * BAR, 0.2)
        body[:, 1] = at(body[:, 1], chime, bar * BAR, 0.2)
    return _finish(body, -24.0)


def track_setup():
    """A lobby. Almost static: warm, waiting, and pleased to see you."""
    bars, rng = 8, seeded("Setup")
    seconds = bars * BAR + CROSSFADE
    body = stack(
        to_stereo(bounce_bass(bars, seconds, 0.55)),
        warm_pad(bars, seconds, brightness=1000.0, rng=rng) * 0.7,
        to_stereo(room_texture(seconds, rng, 0.04)),
    )
    for i in range(6):
        note = PENTATONIC[i % len(PENTATONIC)] + "5"
        tick = marimba(hz(note), 0.3, decay=0.1, brightness=0.6)
        when = rng.uniform(0.4, seconds - 1.0)
        body[:, 0] = at(body[:, 0], tick, when, 0.06)
        body[:, 1] = at(body[:, 1], tick, when + 0.004, 0.06)
    return _finish(body, -28.0)


def track_crew():
    """Readiness. A shade more forward than the lobby, and openly cheerful about it."""
    bars, rng = 16, seeded("Crew")
    seconds = bars * BAR + CROSSFADE
    events = []
    for bar in range(0, bars + 1, 2):
        events.append((bar * BAR + BEAT, "A5", 0.5))
        events.append((bar * BAR + BEAT * 2.5, "F#5", 0.38))
    pulse = silence(seconds)
    for bar in range(bars + 1):
        for beat in range(4):
            when = bar * BAR + beat * BEAT
            if when >= seconds:
                break
            pulse = at(pulse, _soft_tick(rng), when, 0.1 if beat % 2 == 0 else 0.06)
    body = stack(
        to_stereo(bounce_bass(bars, seconds, 0.8)),
        warm_pad(bars, seconds, brightness=1800.0, rng=rng) * 0.75,
        to_stereo(marimba_motif(seconds, events, gain=0.36, decay=0.3), width=7.0),
        to_stereo(pulse, width=5.0),
        to_stereo(room_texture(seconds, rng, 0.03)),
    )
    return _finish(body, -25.0)


def track_aftermath():
    """The pieces are back in the box. Softer and slower, but still in the light."""
    bars, rng = 8, seeded("Aftermath")
    seconds = bars * BAR + CROSSFADE
    events = []
    for bar in range(0, bars + 1, 4):
        for i, note in enumerate(["A5", "F#5", "D5"]):
            events.append((bar * BAR + i * BEAT * 1.25, note, 0.7 - 0.12 * i))
    drone = sine(hz("D2"), seconds) * 0.5 + sine(hz("D3"), seconds) * 0.34
    drone += sine(cents(hz("A3"), 4), seconds) * 0.18
    drone = lowpass(drone, 340.0) * ar_env(seconds, 1.2, 1.2)
    body = stack(
        to_stereo(drone),
        warm_pad(bars, seconds, brightness=900.0, rng=rng) * 0.55,
        to_stereo(marimba_motif(seconds, events, gain=0.38, decay=0.55), width=7.0),
        to_stereo(room_texture(seconds, rng, 0.028)),
    )
    return _finish(body, -26.0)


def _soft_tick(rng) -> np.ndarray:
    return resonator(noise(0.05, rng) * exp_env(0.05, 0.0015), hz("A5"), 16.0) * exp_env(0.05, 0.012)


def _finish(stereo: np.ndarray, rms_target_db: float) -> np.ndarray:
    """
    Fold the tail over the head for a seamless loop, then set the level by loudness.

    The beds are matched on RMS rather than on peak because the runtime crossfades freely between
    any two of them; peak-matching would let the sparse loops arrive noticeably quieter than the
    sustained ones even though the meters agreed.
    """
    left = loop_seamless(stereo[:, 0], CROSSFADE)
    right = loop_seamless(stereo[:, 1], CROSSFADE)
    joined = np.stack([left, right], axis=-1)
    joined = soft_clip(joined, 1.15)
    return rms_normalize(joined, rms_target_db, peak_ceiling_db=-3.0)


TRACKS = {
    "Planning": track_planning,
    "Execute": track_execute,
    "Title": track_title,
    "Setup": track_setup,
    "Crew": track_crew,
    "Aftermath": track_aftermath,
}


def main() -> None:
    only = set(sys.argv[1:])
    report = []
    for name, build in TRACKS.items():
        if only and name not in only:
            continue
        data = build()
        info = write_wav(OUT / f"{name}.wav", data, stereo=True)
        info["bars"] = round(info["seconds"] / BAR, 3)
        report.append(info)
        print(f"{name:<12} {info['seconds']:>7.3f}s  {info['bars']:>5.2f} bars  "
              f"peak {info['peak_db']:>6.2f}  mono {info['mono_peak_db']:>6.2f}  "
              f"rms {info['rms_db']:>7.2f} dB")

    (Path(__file__).parent / "music_report.json").write_text(json.dumps(report, indent=2))
    print(f"\n{len(report)} track(s) -> {OUT}")


if __name__ == "__main__":
    main()
