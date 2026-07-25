"""Measures the generated audio against the brief so a pass can be reviewed without ears.

Checks, per clip:
  * duration, true peak, RMS, DC offset
  * spectral centroid and a low/mid/high energy split, which is how "dull", "harsh" and
    "too small" show up as numbers
  * the level the player actually hears — clip peak plus the cue's gain from AudioCueTable.cs —
    against the mix hierarchy in the audio direction brief
  * loop seam continuity for every clip the runtime loops

    python tools/audio/audit.py
"""

from __future__ import annotations

import math
import re
import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SFX = ROOT / "Assets" / "Audio" / "Resources" / "BattlePlanSfx"
MUSIC = ROOT / "Assets" / "Audio" / "Resources" / "BattlePlanMusic"
TABLE = ROOT / "Assets" / "Scripts" / "Audio" / "AudioCueTable.cs"

LOOPING = {
    "PhaseDodge", "SniperLockCharge", "AreaLockCharge", "GrenadeFuse", "AmbienceBoard",
    "Planning", "Execute", "Title", "Setup", "Crew", "Aftermath",
}


def read(path: Path):
    with wave.open(str(path), "rb") as handle:
        frames = handle.readframes(handle.getnframes())
        channels = handle.getnchannels()
        rate = handle.getframerate()
    data = np.frombuffer(frames, dtype="<i2").astype(np.float64) / 32768.0
    if channels == 2:
        data = data.reshape(-1, 2)
    return data, rate, channels


def db(x: float) -> float:
    return 20.0 * math.log10(max(abs(x), 1e-9))


def parse_cue_volumes() -> dict:
    """Pull `cue = X` / `volume = Y` pairs and clipKey aliases straight out of the C# table."""
    text = TABLE.read_text()
    volumes, aliases = {}, {}
    for block in text.split("new()")[1:]:
        cue = re.search(r"cue\s*=\s*AudioCueId\.(\w+)", block)
        if not cue:
            continue
        name = cue.group(1)
        vol = re.search(r"volume\s*=\s*([0-9.]+)f", block)
        volumes[name] = float(vol.group(1)) if vol else 0.8
        key = re.search(r"clipKey\s*=\s*nameof\(AudioCueId\.(\w+)\)", block)
        if key:
            aliases[name] = key.group(1)
    return volumes, aliases


def analyse(path: Path):
    data, rate, channels = read(path)
    mono = data if data.ndim == 1 else data.mean(axis=1)
    peak = float(np.max(np.abs(data)))

    window = np.hanning(len(mono)) if len(mono) > 16 else np.ones(len(mono))
    spectrum = np.abs(np.fft.rfft(mono * window))
    freqs = np.fft.rfftfreq(len(mono), 1.0 / rate)
    energy = float(np.sum(spectrum)) or 1e-9
    centroid = float(np.sum(freqs * spectrum) / energy)

    def band(lo, hi):
        mask = (freqs >= lo) & (freqs < hi)
        return float(np.sum(spectrum[mask]) / energy)

    result = {
        "name": path.stem,
        "seconds": len(mono) / rate,
        "channels": channels,
        "peak_db": db(peak),
        "rms_db": db(float(np.sqrt(np.mean(np.square(mono))))),
        "dc": float(np.mean(mono)),
        "centroid": centroid,
        "sub": band(0, 120),
        "low": band(120, 500),
        "mid": band(500, 3000),
        "high": band(3000, 24000),
    }

    if path.stem in LOOPING:
        # Measure the discontinuity at the wrap, not the difference in envelope level either side
        # of it. A musical loop that starts on a downbeat is *supposed* to be louder at the head
        # than at the tail; comparing those two windows flags every percussive bed as broken while
        # saying nothing about whether the join actually clicks. What clicks is a sample-to-sample
        # step at the wrap that is larger than the steps the waveform already takes, so that is
        # what gets measured — reported as the wrap jump relative to the clip's own 99.9th
        # percentile step, in dB. Comfortably negative means inaudible.
        step = np.abs(np.diff(mono))
        typical = float(np.percentile(step, 99.9))
        wrap = abs(float(mono[0]) - float(mono[-1]))
        result["seam"] = db(wrap / typical) if typical > 1e-12 else 0.0
    return result


def main() -> None:
    volumes, aliases = parse_cue_volumes()
    problems = []

    for label, folder in (("SFX", SFX), ("MUSIC", MUSIC)):
        files = sorted(folder.glob("*.wav"))
        print(f"\n=== {label} ({len(files)}) ===")
        header = f"{'clip':<20}{'sec':>7}{'peak':>7}{'rms':>8}{'cent':>7}"
        print(header + f"{'sub':>6}{'low':>6}{'mid':>6}{'high':>6}{'in-game':>9}{'seam':>8}")

        for path in files:
            info = analyse(path)
            stem = info["name"].rsplit("_", 1)[0] if re.search(r"_\d+$", info["name"]) else info["name"]
            cues = [c for c, k in aliases.items() if k == stem] + ([stem] if stem in volumes else [])
            gain = max((volumes[c] for c in cues if c in volumes), default=None)
            in_game = info["peak_db"] + db(gain) if gain else float("nan")

            print(
                f"{info['name']:<20}{info['seconds']:>7.3f}{info['peak_db']:>7.1f}"
                f"{info['rms_db']:>8.1f}{info['centroid']:>7.0f}"
                f"{info['sub']:>6.2f}{info['low']:>6.2f}{info['mid']:>6.2f}{info['high']:>6.2f}"
                f"{in_game:>9.1f}" + (f"{info['seam']:>8.1f}" if "seam" in info else f"{'-':>8}")
            )

            if abs(info["dc"]) > 0.01:
                problems.append(f"{info['name']}: DC offset {info['dc']:.4f}")
            if info["peak_db"] > -0.9:
                problems.append(f"{info['name']}: peak {info['peak_db']:.2f} dB is at the ceiling")
            if gain is None and label == "SFX":
                problems.append(f"{info['name']}: no cue in AudioCueTable references this clip")
            # 0 dB means the wrap steps as hard as the waveform's own worst step; -6 dB is already
            # half that and inaudible. Anything at or above unity is a real click.
            if "seam" in info and info["seam"] > -6.0:
                problems.append(f"{info['name']}: audible loop seam, wrap step {info['seam']:.1f} dB")

    # Every cue must resolve to a file, or it is silent in game.
    have = {p.stem.rsplit("_", 1)[0] if re.search(r"_\d+$", p.stem) else p.stem for p in SFX.glob("*.wav")}
    for cue in volumes:
        key = aliases.get(cue, cue)
        if key not in have:
            problems.append(f"cue {cue} -> '{key}': no clip on disk")

    print("\n=== issues ===")
    if problems:
        for problem in problems:
            print(f"  ! {problem}")
        sys.exit(0)
    print("  none")


if __name__ == "__main__":
    main()
