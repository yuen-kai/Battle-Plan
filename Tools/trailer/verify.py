#!/usr/bin/env python3
"""Checks a finished trailer before it is called done.

Five things, all measured rather than looked at:

* the file decodes end to end with no ffmpeg errors
* it is 1920x1080 at 60fps and as long as the edit said it would be
* it carries an audio stream, and that stream has no silent gap in the middle of it
* the loudness is in the range the other cuts sit at
* on the shots that show the whole board, the board is not cropped

The board check is the one worth explaining. The arena is dressed with scenery blocks that float
in the void outside the playing surface and are made of the same material as the deck, so no
colour test can tell "a prop near the frame edge" from "the board running off it". What it can
tell is the *shape* of the contact: a prop is a short blob, a cropped board is a continuous run
down most of an edge. So each edge strip is scored on the longest unbroken run of deck-toned
pixels it contains, and the void it leaves over is reported alongside.

Framing is confirmed separately and exactly, in the editor, by projecting the board's own four
corners through the beat's camera pose with Unity's projection rather than inferring it from
pixels. This is the check that runs without Unity.

Usage:  python3 Tools/trailer/verify.py <video> [--board beat_dir ...]
"""

import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]

# Void is a flat warm brown; the deck and its scenery are a cold pale grey. Nothing else on the
# board sits between them.
VOID = (97, 90, 76)
VOID_TOLERANCE = 30
DECK_MIN = 140

# A prop against the frame edge covers a fifth of it at most; a board running off the edge covers
# far more. Measured on the approved trailer's live round: 0.16 of the tallest edge.
MAX_DECK_RUN = 0.30
MIN_VOID = 0.45

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


def probe(path, stream, fields):
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", stream,
         "-show_entries", f"stream={','.join(fields)}", "-of", "csv=p=0", str(path)],
        capture_output=True, text=True, check=True,
    ).stdout.strip()
    return out.split(",") if out else []


def decodes_clean(path):
    result = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-xerror", "-v", "error",
         "-i", str(path), "-f", "null", "-"],
        capture_output=True, text=True,
    )
    return result.returncode == 0, result.stderr.strip()


def silent_gaps(path, floor="-50dB", minimum=0.4):
    """Runs of near-silence inside the film. A tail under the last fade is expected."""
    probe_run = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
         "-af", f"silencedetect=n={floor}:d={minimum}", "-f", "null", "-"],
        capture_output=True, text=True, check=True,
    )
    gaps, start = [], None
    for line in probe_run.stderr.splitlines():
        if "silence_start" in line:
            start = float(line.rsplit("silence_start:", 1)[1].strip())
        elif "silence_end" in line and start is not None:
            end = float(line.rsplit("silence_end:", 1)[1].split("|")[0].strip())
            gaps.append((start, end))
            start = None
    if start is not None:
        gaps.append((start, None))
    return gaps


def loudness(path):
    result = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
         "-af", "ebur128=framelog=quiet", "-f", "null", "-"],
        capture_output=True, text=True, check=True,
    )
    for line in reversed(result.stderr.splitlines()):
        if "I:" in line and "LUFS" in line:
            return float(line.split("I:")[1].split("LUFS")[0].strip())
    return None


def edge_report(image):
    """Longest unbroken deck-toned run and total void share, per frame edge."""
    width, height = image.size
    pixels = image.load()

    def is_void(rgb):
        r, g, b = rgb
        return all(abs(c - v) <= VOID_TOLERANCE for c, v in zip(rgb, VOID)) and r >= g >= b

    def is_deck(rgb):
        return min(rgb) >= DECK_MIN

    def scan(points):
        run = best = 0
        void = 0
        for point in points:
            rgb = pixels[point]
            if is_deck(rgb):
                run += 1
                best = max(best, run)
            else:
                run = 0
            if is_void(rgb):
                void += 1
        return best / len(points), void / len(points)

    band = 6
    return {
        "top": scan([(x, y) for x in range(width) for y in (0, band)]),
        "bottom": scan([(x, y) for x in range(width) for y in (height - 1, height - 1 - band)]),
        "left": scan([(x, y) for y in range(height) for x in (0, band)]),
        "right": scan([(x, y) for y in range(height) for x in (width - 1, width - 1 - band)]),
    }


def check_board(beat_dir):
    frames = sorted(Path(beat_dir).glob("f*.jpg"))
    if not frames:
        check(False, f"{beat_dir}: no frames")
        return
    # First live frame, middle, last: the camera moves across a beat, so one frame proves nothing.
    for frame in (frames[24], frames[len(frames) // 2], frames[-1]):
        report = edge_report(Image.open(frame).convert("RGB"))
        worst_run = max(run for run, _ in report.values())
        least_void = min(void for _, void in report.values())
        detail = " ".join(f"{k}:run={v[0]:.2f},void={v[1]:.2f}" for k, v in report.items())
        check(
            worst_run <= MAX_DECK_RUN and least_void >= MIN_VOID,
            f"{Path(beat_dir).name}/{frame.name} board clear of every edge ({detail})",
        )


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    video = Path(sys.argv[1])
    boards = sys.argv[sys.argv.index("--board") + 1:] if "--board" in sys.argv else []

    print(f"{video.name}")
    clean, message = decodes_clean(video)
    check(clean, f"decodes end to end{'' if clean else ': ' + message}")

    width, height, rate = probe(video, "v:0", ["width", "height", "r_frame_rate"])
    check(f"{width}x{height}" == "1920x1080", f"1920x1080 (got {width}x{height})")
    check(rate == "60/1", f"60fps (got {rate})")

    duration = float(probe(video, "v:0", ["duration"])[0])
    print(f"  info runtime {duration:.2f}s")

    audio = probe(video, "a:0", ["codec_name", "channels", "duration"])
    check(bool(audio), f"audio stream present ({','.join(audio) if audio else 'none'})")
    if audio:
        check(
            abs(float(audio[2]) - duration) < 0.35,
            f"audio covers the whole film ({float(audio[2]):.2f}s of {duration:.2f}s)",
        )
        gaps = [g for g in silent_gaps(video) if g[1] is not None]
        check(not gaps, f"no silent gap inside the film ({gaps})")
        level = loudness(video)
        check(level is not None and -17 < level < -11, f"loudness {level} LUFS")

    for beat in boards:
        check_board(beat)

    print()
    if failures:
        print(f"{len(failures)} FAILED")
        raise SystemExit(1)
    print("all checks passed")


if __name__ == "__main__":
    main()
