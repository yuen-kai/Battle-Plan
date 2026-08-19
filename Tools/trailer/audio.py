#!/usr/bin/env python3
"""Scores a Battle Plan trailer from the game's own music and muxes it onto the silent cut.

There is no generated score here. Each film runs on a track the game already ships, which keeps it
honest about what the game sounds like: the crew reveal plays under the crew-select theme it was
written for, and the gameplay cut plays under the in-match track. Only the end card changes song,
and it arrives without a gap.

The shape is the same in every cut and is the strongest thing holding a film together: **one song,
played once, straight through**. It is never replaced and never restarted at a section break — the
playhead runs forward across the whole film and only what is done to it changes, opening filtered
and quiet and lifting to full. A cut's recipe in SCORES below is therefore one stage per act, in
order, and each stage says what to do to the take rather than which take to start.

The gameplay cut takes that all the way: its title card is the same take still running, and with
`land_on_end` the take is entered late by exactly its overrun so the film's last frame is the
song's last musical moment. The other two hand the title card to the menu theme instead.

Act boundaries are read from the cut's timeline, so retiming an edit rescores it automatically.

Run after Tools/trailer/build.py.
Usage:  python3 Tools/trailer/audio.py [cut]
        cut — trailer | gameplay | characters   (default: trailer)
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MUSIC = ROOT / "Assets/Music"
TRAILER_DIR = ROOT / "Captures/Trailer"

SR = 44100

# The crew-select theme and the menu theme. Nothing else is used: the game's other two tracks are
# the in-match bed and the settings pad, both around 65-70bpm, and neither carries a cut.
CREW_THEME = MUSIC / "Battle_Plan_Character_Selections.wav"
CLOSER = MUSIC / "Battle_Plan_Main_Menu.wav"

THEME = "theme"
CLOSING = "closer"


def stage(source, *filters):
    """One act of the bed. A filter may be a string, or a callable given the act's length."""
    return dict(source=source, filters=filters)


def fade_out(seconds):
    return lambda length: (
        f"afade=t=out:st={max(0.0, length - min(seconds, length)):.3f}:"
        f"d={min(seconds, length):.3f}"
    )


SCORES = {
    # Acts one to three are one continuous performance: filtered down at the open, opened up for
    # the crew, and carried straight on under the gameplay. It used to degrade over act three,
    # which only made sense while that act was about a crew losing; over a straight round it would
    # be scoring a defeat the picture is not showing.
    "trailer": dict(
        video="BattlePlan_Trailer.mp4",
        out="BattlePlan_Trailer_Scored.mp4",
        theme=CREW_THEME,
        closer=CLOSER,
        acts=[
            stage(THEME, "lowpass=f=760", "volume=0.5", "afade=t=in:st=0:d=1.8"),
            stage(THEME, "volume=1.0", "afade=t=in:st=0:d=0.9"),
            stage(THEME, "volume=1.0", fade_out(1.2)),
            stage(CLOSING, "volume=1.05", "afade=t=in:st=0:d=0.5", fade_out(2.6)),
        ],
    ),
    # The gameplay cut runs on the crew-select theme, which is the most driving thing the game
    # ships: 103bpm against the in-match track's 70, nearly twice the attacks per second, and
    # fifteen times the energy above 1kHz. The in-match track is a slow brooding bed — right for
    # sitting inside a round, wrong under a montage cut at a shot and a half a second, where it
    # made the film feel like it was running at half speed.
    #
    # It still builds across the three steps, but only just: the opening is veiled rather than
    # muffled, because a third of this film is PLAN and burying it defeats the point of changing
    # track. Every move is quick for the same reason — a fade that takes a third of a section
    # is the section.
    "gameplay": dict(
        video="BattlePlan_Gameplay.mp4",
        out="BattlePlan_Gameplay_Scored.mp4",
        theme=CREW_THEME,
        closer=CLOSER,
        acts=[
            stage(THEME, "lowpass=f=2600", "volume=0.74", "afade=t=in:st=0:d=0.5"),
            stage(THEME, "lowpass=f=6000", "volume=0.88", "afade=t=in:st=0:d=0.35"),
            stage(THEME, "volume=1.08", "afade=t=in:st=0:d=0.3"),
            # The title card does not change song. It is the same take still running, at the same
            # level and with no fade back in, so there is no seam at the last section break —
            # which is also why act three no longer fades out under it.
            stage(THEME, "volume=1.08", fade_out(0.8)),
        ],
        # ...and the take is entered late by exactly as much as it overruns the film, so the last
        # frame lands on the song's last musical moment. A film that ends when its music does
        # needs no help getting off the air.
        land_on_end=True,
    ),
    # The crew reel under the crew-select theme, which is the track it was written for. The opening
    # card is the same pass held back a little so the reveal has somewhere to go.
    "characters": dict(
        video="BattlePlan_Characters.mp4",
        out="BattlePlan_Characters_Scored.mp4",
        theme=CREW_THEME,
        closer=CLOSER,
        acts=[
            stage(THEME, "lowpass=f=900", "volume=0.6", "afade=t=in:st=0:d=1.2"),
            stage(THEME, "volume=1.0", "afade=t=in:st=0:d=0.8", fade_out(1.2)),
            stage(CLOSING, "volume=1.05", "afade=t=in:st=0:d=0.5", fade_out(2.6)),
        ],
    ),
}


def run(args):
    subprocess.run(args, check=True)


def probe_duration(path):
    return float(subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "csv=p=0", str(path)],
        capture_output=True, text=True, check=True,
    ).stdout.strip())


def act_lengths(timeline, silent):
    """Act spans measured off the finished cut, since dissolves overlap the shots they join."""
    entries = json.loads(timeline.read_text())
    total = probe_duration(silent)

    starts = {}
    for entry in entries:
        starts.setdefault(entry["act"], entry["at"])

    acts = sorted(starts)
    lengths = []
    for index, act in enumerate(acts):
        end = starts[acts[index + 1]] if index + 1 < len(acts) else total
        lengths.append(end - starts[act])
    return lengths, total


def musical_length(source):
    """Where the music actually stops, ignoring the dead air the wav ends on.

    Measured at -35dB rather than a stricter floor. These tracks do not stop dead; they decay, and
    the last half second of that decay is inaudible under a mix but is very much audible as a hole
    when a loop seam lands in it or a film is timed to end on it.
    """
    probe = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(source),
         "-af", "silencedetect=n=-35dB:d=0.25", "-f", "null", "-"],
        capture_output=True, text=True, check=True,
    )
    duration = probe_duration(source)

    for line in probe.stderr.splitlines():
        if "silence_start" in line:
            start = float(line.rsplit("silence_start:", 1)[1].strip())
            # Only a trailing run counts; a rest in the middle is part of the music.
            if duration - start > 0.5:
                return start
    return duration


def segment(work, name, source, start, duration, filters, loop=True):
    """Cuts a stretch of a track. A theme is shorter than a cut, so it loops by default."""
    out = work / f"{name}.wav"
    run([
        "ffmpeg", "-y", "-loglevel", "error",
        *(["-stream_loop", "-1"] if loop else []),
        "-ss", f"{start:.3f}", "-i", str(source),
        "-t", f"{duration:.3f}",
        "-af", ",".join(filters),
        "-ar", str(SR), "-ac", "2",
        str(out),
    ])
    return out


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else "trailer"
    if name not in SCORES:
        raise SystemExit(f"unknown cut {name!r}; pick one of {', '.join(sorted(SCORES))}")
    score = SCORES[name]

    silent = TRAILER_DIR / score["video"]
    timeline = TRAILER_DIR / f"timeline-{name}.json"
    if not silent.exists() or not timeline.exists():
        raise SystemExit(f"run Tools/trailer/build.py {name} first")

    work = TRAILER_DIR / "build" / name / "audio"
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)

    lengths, total = act_lengths(timeline, silent)
    if len(lengths) != len(score["acts"]):
        raise SystemExit(
            f"{name}: cut has {len(lengths)} acts, score has {len(score['acts'])}"
        )

    # The theme wav ends on dead air, so it is trimmed to its last musical moment and looped from
    # there; landing in that gap was an audible hole in the middle of the crew reveal.
    playable = musical_length(score["theme"])
    loop = work / "theme-loop.wav"
    run([
        "ffmpeg", "-y", "-loglevel", "error", "-i", str(score["theme"]),
        "-t", f"{playable:.3f}", "-ar", str(SR), "-ac", "2", str(loop),
    ])

    # Entering the take late by exactly its overrun puts the film's last frame on the track's last
    # musical moment. Derived rather than written down, so retiming the edit keeps the landing.
    pieces = []
    playhead = max(0.0, playable - total) if score.get("land_on_end") else 0.0
    for index, (plan, length) in enumerate(zip(score["acts"], lengths)):
        theme_stage = plan["source"] == THEME
        source = loop if theme_stage else score["closer"]
        start = playhead % playable if theme_stage else 0.0
        filters = [f(length) if callable(f) else f for f in plan["filters"]]
        # Only loop a stage that actually runs off the end of its take. Asking ffmpeg to loop a
        # span that finishes exactly on the source's last sample makes it emit a whole extra
        # iteration and restart the fade clock, which silently truncated the end of the film.
        pieces.append(
            segment(
                work,
                f"{index:02d}-act{index + 1}",
                source,
                start,
                length,
                filters,
                loop=theme_stage and start + length > playable,
            )
        )
        if theme_stage:
            playhead += length

    listing = work / "concat.txt"
    listing.write_text("".join(f"file '{p}'\n" for p in pieces))

    bed = work / "bed.wav"
    run([
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "concat", "-safe", "0", "-i", str(listing),
        # Resampling each stage loses a few frames; pad back to the cut's length so the mux cannot
        # trim the end card off the video.
        "-af", "loudnorm=I=-14:TP=-1.2:LRA=11,apad",
        "-t", f"{total:.3f}",
        "-ar", str(SR), "-ac", "2", str(bed),
    ])

    out = TRAILER_DIR / score["out"]
    run([
        "ffmpeg", "-y", "-loglevel", "error",
        "-i", str(silent), "-i", str(bed),
        "-map", "0:v:0", "-map", "1:a:0",
        "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
        "-shortest", "-movflags", "+faststart",
        str(out),
    ])

    print("acts: " + " / ".join(f"{length:.1f}s" for length in lengths))
    print(out)


if __name__ == "__main__":
    main()
