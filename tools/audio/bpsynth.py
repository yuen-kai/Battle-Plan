"""Synthesis toolkit for Battle Plan's sound set.

The audio direction brief (`docs/design/ArtDirection.md` §10) asks for a palette that is cold,
dry and mechanical, tuned to an 84 BPM F minor score, with interface sounds quantised to an
eighth note. Sounds like that are built, not recorded, so this module is the instrument: a small
bank of oscillators, envelopes, filters and resonant bodies, plus loop-safe rendering and a
consistent loudness stage.

Everything is deterministic. Each clip seeds its own generator from its filename, so regenerating
the set produces byte-identical output and a review can be reproduced.

Run `make_sfx.py` and `make_music.py` to produce the assets.
"""

from __future__ import annotations

import math
import re
import struct
import wave
from pathlib import Path

import numpy as np
from scipy import signal

SAMPLE_RATE = 48_000
BPM = 84.0
BEAT = 60.0 / BPM              # 0.714 s
EIGHTH = BEAT / 2              # 0.357 s
BAR = BEAT * 4                 # 2.857 s

# D major / D major-pentatonic: the bright, daylit key the score and interface are tuned to.
# Computed rather than tabulated so any note in any octave resolves — a hand-written table only
# covers the notes the current arrangement happens to use and breaks the moment one changes.
_PITCH_CLASS = {
    "C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11,
}

_NOTE_PATTERN = re.compile(r"^([A-Ga-g])([#b]?)(-?\d+)$")


def hz(note: str) -> float:
    """Equal-tempered frequency for a scientific-pitch note name, e.g. 'D4', 'F#3', 'Bb2'."""
    match = _NOTE_PATTERN.match(note.strip())
    if match is None:
        raise ValueError(f"Unparseable note name: {note!r}")

    letter, accidental, octave = match.groups()
    semitone = _PITCH_CLASS[letter.upper()]
    if accidental == "#":
        semitone += 1
    elif accidental == "b":
        semitone -= 1

    # MIDI 69 is A4 = 440 Hz; C4 is MIDI 60.
    midi = (int(octave) + 1) * 12 + semitone
    return 440.0 * (2.0 ** ((midi - 69) / 12.0))


def semitones(freq: float, steps: float) -> float:
    return freq * (2.0 ** (steps / 12.0))


def cents(freq: float, amount: float) -> float:
    return freq * (2.0 ** (amount / 1200.0))


def n_samples(seconds: float) -> int:
    return max(1, int(round(seconds * SAMPLE_RATE)))


def t_axis(seconds: float) -> np.ndarray:
    return np.arange(n_samples(seconds), dtype=np.float64) / SAMPLE_RATE


def silence(seconds: float) -> np.ndarray:
    return np.zeros(n_samples(seconds), dtype=np.float64)


# --- oscillators -----------------------------------------------------------


def sine(freq, seconds: float, phase: float = 0.0) -> np.ndarray:
    t = t_axis(seconds)
    if np.isscalar(freq):
        return np.sin(2 * np.pi * freq * t + phase)
    # Frequency envelope: integrate to keep the phase continuous through a sweep.
    freq = np.asarray(freq, dtype=np.float64)[: len(t)]
    return np.sin(2 * np.pi * np.cumsum(freq) / SAMPLE_RATE + phase)


def saw(freq, seconds: float, harmonics: int = 24, phase: float = 0.0) -> np.ndarray:
    """Additive saw, band-limited by construction so nothing aliases into the top octave."""
    t = t_axis(seconds)
    out = np.zeros_like(t)
    base = float(freq) if np.isscalar(freq) else float(np.mean(freq))
    usable = min(harmonics, max(1, int(0.45 * SAMPLE_RATE / max(base, 1.0))))
    for k in range(1, usable + 1):
        out += np.sin(2 * np.pi * base * k * t + phase * k) / k
    return out * (2.0 / np.pi)


def square(freq: float, seconds: float, harmonics: int = 16) -> np.ndarray:
    t = t_axis(seconds)
    out = np.zeros_like(t)
    usable = min(harmonics, max(1, int(0.45 * SAMPLE_RATE / max(freq, 1.0))))
    for k in range(1, usable + 1, 2):
        out += np.sin(2 * np.pi * freq * k * t) / k
    return out * (4.0 / np.pi)


def noise(seconds: float, rng: np.random.Generator) -> np.ndarray:
    return rng.standard_normal(n_samples(seconds))


def sweep(start: float, end: float, seconds: float, curve: float = 1.0) -> np.ndarray:
    """A frequency ramp usable anywhere a frequency is accepted."""
    shape = np.linspace(0.0, 1.0, n_samples(seconds)) ** curve
    return start + (end - start) * shape


# --- envelopes -------------------------------------------------------------


def exp_env(seconds: float, decay: float, attack: float = 0.001) -> np.ndarray:
    """Fast attack, exponential fall. The shape almost every dry mechanical sound wants."""
    t = t_axis(seconds)
    env = np.exp(-t / max(decay, 1e-4))
    a = max(1, n_samples(attack))
    env[:a] *= np.linspace(0.0, 1.0, a) ** 0.5
    return env


def ar_env(seconds: float, attack: float, release: float, curve: float = 2.0) -> np.ndarray:
    total = n_samples(seconds)
    a = min(total, max(1, n_samples(attack)))
    r = min(total - a, max(1, n_samples(release)))
    env = np.ones(total)
    env[:a] = np.linspace(0.0, 1.0, a) ** (1.0 / curve)
    if r > 0:
        env[total - r :] *= np.linspace(1.0, 0.0, r) ** curve
    return env


def swell_env(seconds: float, peak_at: float = 0.75) -> np.ndarray:
    """Slow rise to a late peak, then a short fall. Phase swells and deploy stingers."""
    total = n_samples(seconds)
    peak = int(np.clip(peak_at, 0.05, 0.95) * total)
    env = np.empty(total)
    env[:peak] = np.linspace(0.0, 1.0, peak) ** 1.8
    env[peak:] = np.linspace(1.0, 0.0, total - peak) ** 1.4
    return env


# --- filters ---------------------------------------------------------------


def _biquad(kind: str, x: np.ndarray, freq, q: float) -> np.ndarray:
    if np.isscalar(freq):
        w = 2.0 * np.clip(float(freq), 20.0, 0.45 * SAMPLE_RATE) / SAMPLE_RATE
        b, a = signal.butter(2, w, btype=kind) if kind in ("low", "high") else (None, None)
        if b is None:
            lo = np.clip(float(freq) / math.sqrt(max(q, 0.1) * 2), 20.0, 0.4 * SAMPLE_RATE)
            hi = np.clip(float(freq) * math.sqrt(max(q, 0.1) * 2), lo * 1.05, 0.45 * SAMPLE_RATE)
            b, a = signal.butter(2, [2 * lo / SAMPLE_RATE, 2 * hi / SAMPLE_RATE], btype="band")
        return signal.lfilter(b, a, x)

    # Time-varying cutoff: process in short blocks so a sweep stays cheap but still smooth.
    freq = np.asarray(freq, dtype=np.float64)
    out = np.zeros_like(x)
    block = 256
    zi = None
    for start in range(0, len(x), block):
        stop = min(start + block, len(x))
        f = float(np.mean(freq[start:stop])) if stop <= len(freq) else float(freq[-1])
        w = 2.0 * np.clip(f, 20.0, 0.45 * SAMPLE_RATE) / SAMPLE_RATE
        b, a = signal.butter(2, w, btype=kind)
        if zi is None:
            zi = signal.lfilter_zi(b, a) * x[0]
        out[start:stop], zi = signal.lfilter(b, a, x[start:stop], zi=zi)
    return out


def lowpass(x: np.ndarray, freq, q: float = 0.707) -> np.ndarray:
    return _biquad("low", x, freq, q)


def highpass(x: np.ndarray, freq, q: float = 0.707) -> np.ndarray:
    return _biquad("high", x, freq, q)


def bandpass(x: np.ndarray, low: float, high: float, order: int = 2) -> np.ndarray:
    lo = np.clip(low, 20.0, 0.4 * SAMPLE_RATE)
    hi = np.clip(high, lo * 1.05, 0.45 * SAMPLE_RATE)
    b, a = signal.butter(order, [2 * lo / SAMPLE_RATE, 2 * hi / SAMPLE_RATE], btype="band")
    return signal.lfilter(b, a, x)


def resonator(x: np.ndarray, freq: float, q: float = 30.0) -> np.ndarray:
    """A single ringing peak. Turns a click into a pitched body."""
    w0 = 2 * np.pi * np.clip(freq, 20.0, 0.45 * SAMPLE_RATE) / SAMPLE_RATE
    alpha = math.sin(w0) / (2 * q)
    b = np.array([alpha, 0.0, -alpha])
    a = np.array([1 + alpha, -2 * math.cos(w0), 1 - alpha])
    return signal.lfilter(b / a[0], a / a[0], x)


# --- bodies ----------------------------------------------------------------


def fm_bell(
    freq: float,
    seconds: float,
    ratio: float = 3.51,
    index: float = 5.0,
    decay: float = 0.28,
    mod_decay: float = 0.06,
) -> np.ndarray:
    """Two-operator FM with an inharmonic ratio: the plucked metallic bell of the score's motif."""
    t = t_axis(seconds)
    mod_env = np.exp(-t / max(mod_decay, 1e-4))
    modulator = np.sin(2 * np.pi * freq * ratio * t) * index * mod_env
    carrier = np.sin(2 * np.pi * freq * t + modulator)
    return carrier * exp_env(seconds, decay, attack=0.0008)


def metal_body(
    freq: float,
    seconds: float,
    rng: np.random.Generator,
    partials: int = 7,
    decay: float = 0.12,
    spread: float = 2.4,
) -> np.ndarray:
    """A struck inharmonic body. The processed-metal percussion and every mechanical clack."""
    out = silence(seconds)
    for i in range(partials):
        f = freq * (1.0 + spread * i + rng.uniform(-0.12, 0.12))
        if f > 0.45 * SAMPLE_RATE:
            break
        amp = 1.0 / (1.0 + 1.5 * i)
        out += sine(f, seconds) * exp_env(seconds, decay / (1.0 + 0.45 * i)) * amp
    return out / max(np.max(np.abs(out)), 1e-9)


def _unit(x: np.ndarray) -> np.ndarray:
    peak = float(np.max(np.abs(x)))
    return x if peak < 1e-9 else x / peak


def click(seconds: float, freq: float, rng: np.random.Generator, q: float = 24.0,
          decay: float = 0.012, bright: float = 1.0) -> np.ndarray:
    """
    A dry mechanical tick: an impulse of noise rung through a resonant peak.

    The resonator is normalised before the air layer is added, because a biquad's passband gain
    falls away with Q — left unnormalised the body all but vanishes and every tick in the game
    comes out as a thin hiss instead of a pitched click.
    """
    src = noise(seconds, rng) * exp_env(seconds, 0.0016, attack=0.0002)
    body = _unit(resonator(src, freq, q)) * exp_env(seconds, decay)
    air = _unit(highpass(noise(seconds, rng), 4200.0)) * exp_env(seconds, 0.0035) * 0.14 * bright
    return body + air


def wood_tick(seconds: float, freq: float, rng: np.random.Generator, decay: float = 0.013,
              bright: float = 0.5) -> np.ndarray:
    """
    A small struck wooden body: a fundamental plus one high partial, both rung short. This is the
    voice of the planning tick and the interface detents — mid-forward, pitched, and nothing above
    about 6 kHz.
    """
    src = noise(seconds, rng) * exp_env(seconds, 0.0012, attack=0.0002)
    low = _unit(resonator(src, freq, 9.0)) * exp_env(seconds, decay)
    high = _unit(resonator(src, freq * 2.76, 14.0)) * exp_env(seconds, decay * 0.55) * 0.45
    snap = _unit(bandpass(noise(seconds, rng), freq * 2.0, min(freq * 8.0, 11_000.0))) \
        * exp_env(seconds, 0.0022) * 0.3 * bright
    return lowpass(low + high + snap, 7000.0)


def marimba(freq: float, seconds: float, decay: float = 0.32, brightness: float = 1.0,
            rng: np.random.Generator | None = None) -> np.ndarray:
    """
    A struck wooden bar, modelled rather than stacked.

    An additive pile of exact sine partials is the fastest way to make a tuned sound and the
    fastest way to make it sound like a synthesiser: it produces two or three spectral peaks
    sitting on perfect integer ratios, with a silent noise floor and a spectrum that never
    changes shape. Measured, that is indistinguishable from a test tone, and it is what makes
    synthesised sound design read as cheap next to a sample.

    So this excites a bank of resonators with a short mallet-contact noise burst instead. The
    excitation gives a broadband onset and a continuous noise floor between the modes; the
    resonators give the tuning; and because each mode decays at its own rate the spectrum evolves
    as it rings, which is the thing a static sine stack cannot do at any level of detail. The
    ratios are the real inharmonic modes of an undercut bar, not integers.
    """
    rng = rng or np.random.default_rng(int(freq * 17) & 0xFFFF)

    # Measured modes of an undercut marimba bar. Deliberately not a harmonic series.
    ratios = (1.0, 3.932, 9.538, 16.688, 24.566)
    levels = (1.0, 0.34 * brightness, 0.13 * brightness, 0.055 * brightness, 0.022 * brightness)
    decays = (1.0, 0.34, 0.16, 0.09, 0.06)
    widths = (58.0, 44.0, 34.0, 26.0, 20.0)

    # The mallet: a very short broadband contact, low-passed by the softness of the head.
    strike = noise(seconds, rng) * exp_env(seconds, 0.0016, attack=0.00015)
    strike = lowpass(strike, min(freq * 26.0, 14_000.0))

    out = silence(seconds)
    for ratio, level, decay_scale, q in zip(ratios, levels, decays, widths):
        mode_freq = freq * ratio * (1.0 + rng.uniform(-0.004, 0.004))
        if mode_freq > 0.45 * SAMPLE_RATE:
            break
        rung = _unit(resonator(strike, mode_freq, q))
        out += rung * exp_env(seconds, max(decay * decay_scale, 0.004), attack=0.0009) * level

    # The audible thud of the mallet head itself, under the tuned part.
    contact = lowpass(strike, freq * 2.0) * exp_env(seconds, 0.008, attack=0.0004) * 0.22
    return lowpass(out + contact, 12_000.0)


def woodblock(freq: float, seconds: float, rng: np.random.Generator,
              decay: float = 0.045, bright: float = 1.0) -> np.ndarray:
    """
    A hollow struck block: the percussion of a tabletop game. Drier and woodier than `wood_tick`,
    with a hollow-body resonance rather than a pure resonant peak, sized for kit duty.
    """
    src = noise(seconds, rng) * exp_env(seconds, 0.0009, attack=0.00015)
    body = _unit(resonator(src, freq, 12.0)) * exp_env(seconds, decay)
    hollow = _unit(resonator(src, freq * 1.51, 18.0)) * exp_env(seconds, decay * 0.6) * 0.4
    tap = _unit(bandpass(noise(seconds, rng), 2200.0, 7000.0)) * exp_env(seconds, 0.0018) * 0.22 * bright
    return lowpass(body + hollow + tap, 8500.0)


def plastic_knock(freq: float, seconds: float, rng: np.random.Generator,
                  decay: float = 0.03) -> np.ndarray:
    """
    A moulded plastic piece landing on a board. Higher and shorter than wood, with a slightly
    inharmonic ring and no low tail — the sound of a game piece, not a body hitting the floor.
    """
    src = noise(seconds, rng) * exp_env(seconds, 0.0008, attack=0.00012)
    body = _unit(resonator(src, freq, 16.0)) * exp_env(seconds, decay)
    ring = _unit(resonator(src, freq * 2.34, 22.0)) * exp_env(seconds, decay * 0.5) * 0.35
    return highpass(lowpass(body + ring, 9500.0), 180.0)


def pluck(freq: float, seconds: float, rng: np.random.Generator, decay: float = 0.18,
          damping: float = 0.42) -> np.ndarray:
    """
    Karplus-Strong: a short plucked string for the bouncy bass line and the roster confirms. Warm
    and round with none of a saw pad's grain.
    """
    length = max(int(SAMPLE_RATE / max(freq, 1.0)), 2)
    total = n_samples(seconds)
    buf = rng.uniform(-1.0, 1.0, length)
    out = np.empty(total)
    for i in range(total):
        out[i] = buf[i % length]
        nxt = (i + 1) % length
        buf[i % length] = (1.0 - damping) * buf[i % length] + damping * 0.5 * (buf[i % length] + buf[nxt])
    return lowpass(out, 6000.0) * exp_env(seconds, decay, attack=0.0006)


def pop(freq: float, seconds: float, rng: np.random.Generator, decay: float = 0.02) -> np.ndarray:
    """
    A cork/peg pop: a fast upward pitch blip with a tiny air puff. The bright board's weapon
    transient — punchy and readable without a gunpowder crack or a sub drop.
    """
    f = sweep(freq * 0.55, freq * 1.25, seconds, curve=0.35)
    tone = sine(f, seconds) * exp_env(seconds, decay, attack=0.0004)
    puff = _unit(bandpass(noise(seconds, rng), freq * 1.5, min(freq * 6.0, 12_000.0))) \
        * exp_env(seconds, decay * 0.4) * 0.28
    return tone + puff


def servo(seconds: float, start: float, end: float, rng: np.random.Generator,
          brightness: float = 1.0) -> np.ndarray:
    """Small electric-motor whine. Traversal, shields, wind-downs."""
    f = sweep(start, end, seconds, curve=0.8)
    tone = sine(f, seconds) * 0.55 + sine(f * 2.02, seconds) * 0.22
    grit = bandpass(noise(seconds, rng), 900.0, 4200.0) * 0.18 * brightness
    return (tone + grit) * ar_env(seconds, 0.02, seconds * 0.45)


def air(seconds: float, low: float, high: float, rng: np.random.Generator) -> np.ndarray:
    """Filtered noise movement: whooshes, hisses, smoke."""
    band = bandpass(noise(seconds, rng), low, high, order=2)
    return band * ar_env(seconds, seconds * 0.25, seconds * 0.6)


def sub_thump(freq: float, seconds: float, drop: float = 0.45, decay: float = 0.12) -> np.ndarray:
    """Body without boom: a pitched-down sine that lands and gets out of the way."""
    f = sweep(freq, freq * drop, seconds, curve=0.4)
    return sine(f, seconds) * exp_env(seconds, decay, attack=0.0015)


# --- shaping ---------------------------------------------------------------


def soft_clip(x: np.ndarray, drive: float = 1.0) -> np.ndarray:
    return np.tanh(x * drive) / math.tanh(max(drive, 1e-6))


def pad(x: np.ndarray, seconds: float) -> np.ndarray:
    """Fit a buffer to an exact length, padding with silence or truncating."""
    target = n_samples(seconds)
    if len(x) >= target:
        return x[:target]
    return np.concatenate([x, np.zeros(target - len(x))])


def mix(*layers: np.ndarray) -> np.ndarray:
    length = max(len(layer) for layer in layers)
    out = np.zeros(length)
    for layer in layers:
        out[: len(layer)] += layer
    return out


def at(buffer: np.ndarray, layer: np.ndarray, seconds: float, gain: float = 1.0) -> np.ndarray:
    """Place a layer at a time offset inside an existing buffer."""
    start = n_samples(seconds)
    stop = min(len(buffer), start + len(layer))
    if start < len(buffer):
        buffer[start:stop] += layer[: stop - start] * gain
    return buffer


def declick(x: np.ndarray, ms: float = 2.0) -> np.ndarray:
    n = min(len(x) // 2, n_samples(ms / 1000.0))
    if n < 2:
        return x
    x = x.copy()
    x[:n] *= np.linspace(0.0, 1.0, n)
    x[-n:] *= np.linspace(1.0, 0.0, n)
    return x


def normalize(x: np.ndarray, peak_db: float = -3.0) -> np.ndarray:
    target = 10.0 ** (peak_db / 20.0)
    current = float(np.max(np.abs(x)))
    if current < 1e-9:
        return x
    return x * (target / current)


def rms_normalize(x: np.ndarray, rms_target_db: float, peak_ceiling_db: float = -3.0) -> np.ndarray:
    """
    Match by loudness rather than by peak, then hold a peak ceiling.

    Peak-matching a set of music beds is misleading: a sustained drone and a sparse percussive loop
    can share a peak and still differ by nearly 10 dB in perceived level, which is exactly what a
    crossfade between two beds exposes. Gain is set from RMS so the tracks sit together, and only
    then is the peak pulled down if it would otherwise run out of headroom.
    """
    current = rms_db(x)
    if current <= -200.0:
        return x

    scaled = x * (10.0 ** ((rms_target_db - current) / 20.0))
    peak = peak_db(scaled)
    if peak > peak_ceiling_db:
        scaled = scaled * (10.0 ** ((peak_ceiling_db - peak) / 20.0))
    return scaled


def limit(x: np.ndarray, ceiling_db: float = -1.0) -> np.ndarray:
    """Catch stray transients without squashing the body of the clip."""
    ceiling = 10.0 ** (ceiling_db / 20.0)
    over = float(np.max(np.abs(x))) / ceiling
    if over > 1.0:
        x = soft_clip(x / ceiling, drive=1.0 + 0.6 * min(over - 1.0, 2.0)) * ceiling
    return np.clip(x, -ceiling, ceiling)


def loop_seamless(x: np.ndarray, crossfade_seconds: float) -> np.ndarray:
    """
    Fold a tail back over the head so the clip joins itself without a seam. The result is shorter
    than the input by the crossfade length, which keeps musical loops on exact bar boundaries when
    the caller renders one crossfade's worth of extra material.
    """
    n = n_samples(crossfade_seconds)
    if n <= 1 or len(x) <= 2 * n:
        return x
    head, tail = x[:-n].copy(), x[-n:]
    ramp = np.linspace(0.0, 1.0, n)
    # Equal-power so the overlap does not dip in the middle.
    head[:n] = head[:n] * np.sqrt(ramp) + tail * np.sqrt(1.0 - ramp)
    return head


def rms_db(x: np.ndarray) -> float:
    value = float(np.sqrt(np.mean(np.square(x))))
    return 20.0 * math.log10(max(value, 1e-9))


def peak_db(x: np.ndarray) -> float:
    return 20.0 * math.log10(max(float(np.max(np.abs(x))), 1e-9))


def write_wav(path: Path, data: np.ndarray, stereo: bool = False) -> dict:
    """16-bit PCM at 48 kHz. Mono unless the caller says otherwise."""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = limit(np.nan_to_num(data), ceiling_db=-1.0)

    if stereo and data.ndim == 1:
        data = np.stack([data, data], axis=-1)
    channels = 2 if (stereo or data.ndim == 2) else 1

    ints = np.clip(np.round(data * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(channels)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(ints.tobytes())

    # Report the true peak and, separately, the peak after folding to mono. The two diverge when
    # the channels are decorrelated, and the gap is the honest measure of how much of a stereo clip
    # survives a phone speaker.
    mono = data if data.ndim == 1 else data.mean(axis=1)
    return {
        "name": path.stem,
        "seconds": round(len(mono) / SAMPLE_RATE, 3),
        "channels": channels,
        "peak_db": round(peak_db(data), 2),
        "mono_peak_db": round(peak_db(mono), 2),
        "rms_db": round(rms_db(mono), 2),
    }


def seeded(name: str) -> np.random.Generator:
    """A generator keyed to the clip name, so every render of that clip is identical."""
    return np.random.default_rng(abs(hash(name)) % (2**32) if False else _stable_seed(name))


def _stable_seed(name: str) -> int:
    seed = 2166136261
    for char in name.encode("utf-8"):
        seed = ((seed ^ char) * 16777619) & 0xFFFFFFFF
    return seed
