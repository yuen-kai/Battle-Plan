"""Measures the signatures that separate bodied sound design from thin synthesis.

"Sounds like a synth demo" is not purely subjective. Sounds that read as cheap share measurable
traits, and each column here targets one of them:

  * modes      - count of distinct spectral peaks. A sine-plus-harmonics stack has a handful; a
                 struck physical object has dozens. Low modal density is the single clearest
                 fingerprint of thin synthesis.
  * inharm     - how far the partials sit from an exact harmonic series, 0 = perfectly harmonic.
                 Real wood, plastic and metal are inharmonic; a pure oscillator stack is not, and
                 perfectly harmonic content is what makes a sound read as "electronic".
  * flat       - spectral flatness (Wiener entropy). Near 0 is a pure tone, near 1 is white noise.
                 Percussive material should sit in a broad middle; either extreme reads as fake.
  * attack     - time from onset to peak, in milliseconds. Real impacts are fast but not
                 instantaneous, and a 0.0 ms attack is a synthesiser artefact that reads as a click.
  * flux       - mean frame-to-frame spectral change. Static spectra sound synthetic; real sounds
                 evolve as different partials decay at different rates.
  * crest      - peak-to-RMS in dB. Percussive sounds need a high crest; a low one means the
                 transient has been squashed and the cue will read as dull and small.

Reference bands come from measuring the same statistics on physically-modelled and recorded
percussion, and are printed alongside so the numbers can be judged rather than just listed.

    python tools/audio/richness.py [clip ...]
"""

from __future__ import annotations

import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SFX = ROOT / "Assets" / "Audio" / "Resources" / "BattlePlanSfx"

# Only the statistics with a defensible threshold get to fail a clip. `attack` and `flux` are
# printed as diagnostics but carry no band: on a multi-event clip "attack" measures the distance to
# the loudest event rather than an onset, and flux is inflated on clips too short to hold many
# analysis frames. Thresholds that fire on healthy material teach you to ignore the tool.
BANDS = {
    "inharm": (0.04, 1.0),
    "flat": (0.008, 0.6),
    "crest": (8.0, 30.0),
}

# Modal count is strongly confounded by duration — a 45 ms tick cannot physically resolve as many
# modes as a 2 s one, and measured across this set the median rises 8 -> 118 -> 169 -> 200 from the
# shortest duration band to the longest. So the floor scales with length instead of being flat.
MODE_FLOORS = ((0.15, 3), (0.5, 20), (1.5, 40), (99.0, 60))


def mode_floor(seconds: float) -> int:
    for limit, floor in MODE_FLOORS:
        if seconds < limit:
            return floor
    return MODE_FLOORS[-1][1]


def load(path: Path) -> tuple[np.ndarray, int]:
    with wave.open(str(path)) as handle:
        rate = handle.getframerate()
        channels = handle.getnchannels()
        raw = np.frombuffer(handle.readframes(handle.getnframes()), "<i2")
    data = raw.astype(np.float64) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    return data, rate


def spectral_peaks(mono: np.ndarray, rate: int) -> tuple[int, float]:
    """Count prominent spectral peaks and estimate how inharmonic they are."""
    window = np.hanning(len(mono))
    spectrum = np.abs(np.fft.rfft(mono * window))
    freqs = np.fft.rfftfreq(len(mono), 1.0 / rate)

    keep = (freqs > 60.0) & (freqs < 16000.0)
    spectrum, freqs = spectrum[keep], freqs[keep]
    if spectrum.size < 8 or spectrum.max() <= 0:
        return 0, 0.0
    spectrum = spectrum / spectrum.max()

    # A peak counts when it is a local maximum and stands clear of the local noise floor.
    floor = np.median(spectrum) * 4.0
    threshold = max(floor, 0.02)
    interior = np.arange(1, len(spectrum) - 1)
    is_peak = (
        (spectrum[interior] > spectrum[interior - 1])
        & (spectrum[interior] > spectrum[interior + 1])
        & (spectrum[interior] > threshold)
    )
    peak_index = interior[is_peak]
    if peak_index.size == 0:
        return 0, 0.0

    peak_freqs = freqs[peak_index]
    strongest = peak_freqs[np.argmax(spectrum[peak_index])]

    # Distance of each partial from the nearest exact multiple of the strongest peak.
    if strongest <= 0:
        return int(peak_index.size), 0.0
    ratios = peak_freqs / strongest
    deviation = np.abs(ratios - np.round(ratios))
    return int(peak_index.size), float(np.mean(deviation))


def envelope_stats(mono: np.ndarray, rate: int) -> tuple[float, float]:
    absolute = np.abs(mono)
    peak = float(absolute.max())
    if peak <= 0:
        return 0.0, 0.0
    onset = int(np.argmax(absolute > peak * 0.1))
    apex = int(np.argmax(absolute))
    attack_ms = max(apex - onset, 0) / rate * 1000.0
    rms = float(np.sqrt(np.mean(mono**2)))
    crest = 20.0 * np.log10(peak / rms) if rms > 0 else 0.0
    return attack_ms, crest


def flatness_and_flux(mono: np.ndarray, rate: int) -> tuple[float, float]:
    size = 1024
    hop = 256
    if len(mono) < size * 2:
        size, hop = 256, 64
    frames = []
    for start in range(0, max(len(mono) - size, 1), hop):
        chunk = mono[start : start + size]
        if len(chunk) < size:
            break
        magnitude = np.abs(np.fft.rfft(chunk * np.hanning(size)))
        frames.append(magnitude)
    if len(frames) < 2:
        return 0.0, 0.0

    stack = np.array(frames)
    energetic = stack[stack.sum(axis=1) > stack.sum(axis=1).max() * 0.05]
    if energetic.size == 0:
        energetic = stack

    safe = energetic + 1e-12
    geometric = np.exp(np.mean(np.log(safe), axis=1))
    arithmetic = np.mean(safe, axis=1)
    flatness = float(np.mean(geometric / arithmetic))

    # Normalise each frame before differencing so flux measures spectral *change*, not decay.
    norm = stack / (np.linalg.norm(stack, axis=1, keepdims=True) + 1e-12)
    flux = float(np.mean(np.abs(np.diff(norm, axis=0)).sum(axis=1)))
    return flatness, flux


def analyse(path: Path) -> dict:
    mono, rate = load(path)
    modes, inharm = spectral_peaks(mono, rate)
    attack, crest = envelope_stats(mono, rate)
    flat, flux = flatness_and_flux(mono, rate)
    return {
        "name": path.stem,
        "seconds": len(mono) / rate,
        "modes": modes,
        "inharm": inharm,
        "flat": flat,
        "attack": attack,
        "flux": flux,
        "crest": crest,
    }


def main() -> None:
    names = sys.argv[1:]
    paths = [SFX / f"{n}.wav" for n in names] if names else sorted(SFX.glob("*.wav"))
    paths = [p for p in paths if p.exists()]

    print(f"{'clip':<20}{'modes':>7}{'inharm':>8}{'flat':>7}{'attack':>8}{'flux':>7}{'crest':>7}   flags")
    weak = []
    for path in paths:
        info = analyse(path)
        flags = []
        for key, (low, high) in BANDS.items():
            if not (low <= info[key] <= high):
                flags.append(f"{key}{'<' if info[key] < low else '>'}")
        floor = mode_floor(info["seconds"])
        if info["modes"] < floor:
            flags.append(f"modes<{floor}")
        if flags:
            weak.append((info["name"], flags))
        print(
            f"{info['name']:<20}{info['modes']:>7d}{info['inharm']:>8.3f}{info['flat']:>7.3f}"
            f"{info['attack']:>8.2f}{info['flux']:>7.3f}{info['crest']:>7.1f}   {' '.join(flags)}"
        )

    print("\nbands: " + "  ".join(f"{k} {v[0]}-{v[1]}" for k, v in BANDS.items())
          + "  modes >= " + "/".join(str(f) for _, f in MODE_FLOORS) + " by duration")
    print("attack and flux are diagnostics only, not pass/fail")
    print(f"\n{len(weak)}/{len(paths)} clip(s) outside a band")
    for name, flags in weak:
        print(f"  {name}: {' '.join(flags)}")


if __name__ == "__main__":
    main()
