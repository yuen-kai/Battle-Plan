#!/usr/bin/env python3
"""Assembles a Battle Plan trailer from captured frame sequences.

There is more than one film cut from the same footage, so the edits live in CUTS below, keyed by
name: one entry per shot, naming the beat it comes from, which take to pull it out of, where to
start inside it, how long to hold, and any captions laid over it. Retiming a trailer means editing
its table and re-running this; nothing here needs Unity, so footage is only captured once and a
cut may draw on several shoots at once.

Three things keep a cut feeling like one film rather than a list of clips:

* Shots are dissolved rather than butt-joined, briefly, so one image grows out of the last.
* Shot lengths shorten through a section instead of sitting at a uniform five seconds, which is
  what made the crew read as a list.
* Nothing is ever cropped in here. A board shot is framed in-engine at a distance that fits the
  whole 15x10 board; punching in on it in the edit is what cuts its near edge off.

Frames are written by TrailerStudio at 60fps into Captures/Trailer/shots/<take>/<beat>/, each beat
starting with a 0.4s pre-roll that is skipped here.

Cards are drawn with Pillow rather than ffmpeg's drawtext: the local ffmpeg is built without
freetype, and rendering them ourselves buys real letter-spacing in the project's own faces.

Usage:  python3 Tools/trailer/build.py [cut] [take]
        cut  — trailer | gameplay | characters   (default: trailer)
        take — default shoot tag for shots that do not name their own (default: v2)
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
FPS = 60
PREROLL_FRAMES = 24
WIDTH, HEIGHT = 1920, 1080

FONTS = ROOT / "Assets/Fonts"
OSWALD_BOLD = FONTS / "OswaldCaps/OswaldCaps-Bold.ttf"
OSWALD = FONTS / "OswaldCaps/OswaldCaps-Regular.ttf"
JOST = FONTS / "Jost/Jost-Regular.ttf"

# Tactical Toybox palette.
ORANGE = (241, 143, 1, 255)
BONE = (232, 226, 212, 255)
INK = (20, 26, 30, 255)

MARGIN = 120

DISSOLVE = 0.28
SEAM = 0.5

# A montage is cut on the beat, so its joins are short enough to read as cuts while still growing
# one image out of the last. xfade will not accept a zero-length transition.
SNAP = 0.12

# A step card wants to get out of the way of the shot it is announcing.
CARD_CUT = 0.16

GRADE = "eq=contrast=1.08:saturation=1.06:gamma=0.98,vignette=PI/5"

# The whole-board shots are graded harder than the rest. At the distance that fits a 15x10 board
# inside the frame a plan ribbon is about ten pixels across against a pale grey deck, so the route
# colours need the saturation to carry; and the standard vignette dims the outermost routes, which
# on this board are the two the plan opens and closes on.
BOARD_GRADE = "eq=contrast=1.14:saturation=1.30:gamma=0.97,vignette=PI/6.5"


def cap(at, dur, lines, label=None, rise=0.35, fall=0.4):
    return dict(at=at, dur=dur, lines=lines, label=label, rise=rise, fall=fall)


def step_card(act, dur, number, word, xf=CARD_CUT):
    """A numbered step, and nothing else.

    The numeral carries the palette so the line needs no second row to say what it is: three of
    these, counted off, are the whole of this film's narration. They do not fade down into the
    footage either — a card that dips to black before every shot is the opposite of a fast cut.
    """
    return dict(act=act, dur=dur, xf=xf, fade=0.0, card=[
        ([(f"{number}. ", ORANGE), (word, BONE)], OSWALD_BOLD, 168, BONE, -104, 16),
    ])


# Wordmark and eyebrow only, centred as a pair. No spec strip, no tagline.
def end_card(act, dur=6.0, xf=0.6):
    return dict(act=act, dur=dur, xf=xf, card=[
        ("BATTLE PLAN", OSWALD_BOLD, 140, BONE, -100, 14),
        ("SIMULTANEOUS-TURN TACTICS", OSWALD, 38, ORANGE, 64, 16),
    ])


# Each act opens on a full-frame statement, then plays its footage clean. Only the crew reveal
# carries captions, because that is the one section where the picture cannot say which unit it is
# looking at; everywhere else the text would be talking over the game.
CONFIDENT = dict(rise=0.16, fall=0.3)


# --- The original three-act trailer ----------------------------------------------------------

TRAILER = [
    dict(act=1, dur=3.2, xf=0.0, card=[
        ("VICTORY IS DETERMINED BY", OSWALD_BOLD, 76, BONE, -86, 8),
        ("WHO HAS THE BETTER PLAN", OSWALD_BOLD, 76, BONE, 8, 8),
    ]),
    # Drawing the plan, then the plans running. No captions: the two shots are the mechanic.
    dict(act=1, beat="a1-plan", inp=0.4, dur=4.4),
    dict(act=1, beat="a1-execute", inp=0.2, dur=3.5),

    dict(act=2, dur=2.8, card=[
        ("WE GOT THE ALL-STAR CREW HANDLED", OSWALD_BOLD, 72, BONE, -36, 8),
    ]),
    # Each unit is introduced twice: a close-up that says who he is, then the board shot that
    # proves it. The name and the boast sit on the portrait so the action shot plays clean.
    dict(act=2, beat="p-sniper", inp=0.5, dur=2.4, xf=SEAM, captions=[
        cap(0.15, 2.25, ["NOTHING ESCAPES HIS VIEW."], label="SNIPER", **CONFIDENT)]),
    dict(act=2, beat="a2-sniper", inp=1.2, dur=3.0),

    # The crew accelerates: each pair is a little tighter than the one before it.
    dict(act=2, beat="p-pogo", inp=0.5, dur=2.3, captions=[
        cap(0.15, 2.15, ["NO WALL HAS EVER HELD HIM."], label="POGORIDER", **CONFIDENT)]),
    dict(act=2, beat="a2-pogo", inp=0.9, dur=2.8),

    dict(act=2, beat="p-shotgunner", inp=0.5, dur=2.2, captions=[
        cap(0.15, 2.05, ["HE GOES THROUGH THE DOOR FIRST."], label="SHOTGUNNER", **CONFIDENT)]),
    dict(act=2, beat="a2-shotgunner", inp=0.8, dur=3.4),

    dict(act=2, beat="p-soldier", inp=0.5, dur=2.1, captions=[
        cap(0.15, 1.95, ["HE HAS AN ANSWER FOR EVERYTHING."], label="SOLDIER", **CONFIDENT)]),
    dict(act=2, beat="a2-soldier", inp=0.5, dur=2.6),

    dict(act=2, beat="p-commander", inp=0.5, dur=2.1, captions=[
        cap(0.15, 1.95, ["AND HE HAS A PLAN."], label="COMMANDER", **CONFIDENT)]),
    dict(act=2, beat="a2-commander", inp=0.3, dur=3.2),

    dict(act=3, dur=3.0, xf=SEAM, card=[
        ("IT'S YOUR JOB TO", OSWALD_BOLD, 66, BONE, -104, 8),
        ("OUTPLAY YOUR OPPONENTS", OSWALD_BOLD, 92, BONE, -4, 10),
    ]),
    # Round four of a King of the Hill match, planning and execution in one unbroken take. The
    # clip starts as the planning phase opens with the crew's real routes drawn on the board, and
    # runs straight through the round those routes describe — so the fight the camera watches is
    # visibly the consequence of the plan it just showed, not a planning shot edited in front of an
    # unrelated one. Boundaries come from the phase log: planning opens at 26.10s, execution at
    # 30.15s, and the next planning phase at 34.88s. Never cropped; the frame is the whole board.
    dict(act=3, beat="a3-match", take="v2", inp=26.05, dur=8.75),

    end_card(4),
]


# --- Gameplay trailer: three steps, counted off -----------------------------------------------
#
# The loop is the product, so the film is the loop and nothing else: a step card, the step, the
# next step card. Three cards, three sections, twenty-five seconds. Every frame comes from take
# `n1`, one Concourse shoot.
#
# Sections one and two are the same turn — one route table in TrailerStudio builds both the plan
# and the round that runs it, so the fight in EXECUTE is provably the consequence of the routes
# drawn in PLAN rather than a planning shot edited in front of an unrelated fight. EXECUTE even
# opens with those routes still lying on the board, and strikes them as the crews step off.
#
# There are no abilities anywhere in the first two sections. That is the point of the pairing:
# PLAN is five orders and REPEAT is what orders turn into.

GAMEPLAY_TAKE = "n1"

GAMEPLAY = [
    # Five routes drawn one at a time, top of the board to the bottom, and held for a moment once
    # the last one lands at 2.71s. Nothing else happens on this board, so nothing else is shown.
    step_card(1, 1.5, 1, "PLAN", xf=0.0),
    # Five orders written on one after another at the rate a hand drags a path, the next only
    # starting once the last has finished. The last lands at 6.51s and the finished plan is then
    # held on its own, because that still picture is what the section is for and cutting on the
    # last stroke throws it away.
    dict(act=1, beat="n-plan", inp=0.12, dur=7.05, grade=BOARD_GRADE, xf=CARD_CUT),

    # One unbroken take at the speed it was filmed at. A unit covers a cell a second and the
    # longest order in the turn is five of them, so the crossing takes as long as it takes; the
    # shot is not cut, retimed or trimmed in the middle. It runs from the plan lying on the board
    # through the crews stepping off, the whole crossing, and into the contact it buys.
    step_card(2, 1.4, 2, "EXECUTE"),
    dict(act=2, beat="n-exec", inp=0.00, dur=8.40, grade=BOARD_GRADE, xf=CARD_CUT),

    # Nine shots, none longer than 1.7s, and never the same kind of image twice running. Six are
    # close on one unit doing one thing to another; three are whole-board shots of a different
    # map, spaced so the montage keeps changing viewpoint and so the game stops looking like it
    # owns one arena. Each is cut to sit on its own kill — the times are the ones the shoot report
    # logged, not guesses.
    step_card(3, 1.4, 3, "REPEAT"),
    # Ten units at once, so the section opens on a round rather than on a trick. Two fall.
    dict(act=3, beat="n-melee", inp=1.30, dur=1.50, xf=CARD_CUT),
    # Foundry, whole board: nothing on it is more than a few cells from a wall, so eight units end
    # up in one knot around the hill and a grenade into that knot kills all four defenders.
    dict(act=3, beat="m-foundry", take="foundry", inp=1.75, dur=1.20, grade=BOARD_GRADE, xf=SNAP),
    # Area Lock down row four; the crosser is obliterated at 1.92s.
    dict(act=3, beat="n-lock", inp=1.05, dur=1.35, xf=SNAP),
    # Point blank at 1.37s, then Shield Rush through the gap it just made.
    dict(act=3, beat="n-breach", inp=1.25, dur=1.55, xf=SNAP),
    # Bastion, whole board: the defenders screen the (8,7) doorway and the crew breaches it anyway.
    # Cut on the moment the bank is doing both things at once — standing solid over the cells the
    # crew has no look into, and thinned to a wash over the one the Shotgunner has just stepped
    # into. The defending sniper goes down at 4.07s.
    dict(act=3, beat="m-bastion", take="bastion", inp=2.85, dur=1.40, grade=BOARD_GRADE, xf=SNAP),
    # No ability at all: four units walk into range and trade, and one of them loses at 2.63s.
    dict(act=3, beat="n-crossfire", inp=1.95, dur=1.15, xf=SNAP),
    # Over the wall and down behind a sniper looking the other way; backstab kill at 2.58s.
    dict(act=3, beat="n-vault", inp=1.50, dur=1.30, xf=SNAP),
    # Causeway, whole board: two spines, a centre court and two side corridors. Area Lock is laid
    # the full width of the court while both corridors fight their own fight.
    dict(act=3, beat="m-causeway", take="causeway", inp=0.95, dur=1.20, grade=BOARD_GRADE,
         xf=SNAP),
    # Held longest, because three units die on the same frame at 2.43s and the section has to land
    # on something. Long enough, too, that the end card's dissolve begins on the debris rather
    # than on the fireball, which the wordmark cannot be read over.
    dict(act=3, beat="n-grenade", inp=1.70, dur=1.70, xf=SNAP),

    end_card(4, dur=5.0),
]


# --- Character showcase: the crew, twice each -------------------------------------------------
#
# The reveal from the main trailer standing on its own. Each unit is introduced twice: a close-up
# that says who he is, then the board shot that proves it. The name and the boast sit on the
# portrait so the action shot plays clean, and each pair is tighter than the last so the crew
# accelerates instead of reading as five equal entries in a list.

CHARACTERS = [
    dict(act=1, dur=3.0, xf=0.0, card=[
        ("WE GOT THE ALL-STAR CREW HANDLED", OSWALD_BOLD, 72, BONE, -36, 8),
    ]),

    dict(act=2, beat="p-sniper", inp=0.5, dur=2.4, xf=SEAM, captions=[
        cap(0.15, 2.25, ["NOTHING ESCAPES HIS VIEW."], label="SNIPER", **CONFIDENT)]),
    dict(act=2, beat="a2-sniper", inp=1.2, dur=3.0),

    dict(act=2, beat="p-pogo", inp=0.5, dur=2.3, captions=[
        cap(0.15, 2.15, ["NO WALL HAS EVER HELD HIM."], label="POGORIDER", **CONFIDENT)]),
    dict(act=2, beat="a2-pogo", inp=0.9, dur=2.8),

    dict(act=2, beat="p-shotgunner", inp=0.5, dur=2.2, captions=[
        cap(0.15, 2.05, ["HE GOES THROUGH THE DOOR FIRST."], label="SHOTGUNNER", **CONFIDENT)]),
    dict(act=2, beat="a2-shotgunner", inp=0.8, dur=3.4),

    dict(act=2, beat="p-soldier", inp=0.5, dur=2.1, captions=[
        cap(0.15, 1.95, ["HE HAS AN ANSWER FOR EVERYTHING."], label="SOLDIER", **CONFIDENT)]),
    dict(act=2, beat="a2-soldier", inp=0.5, dur=2.6),

    dict(act=2, beat="p-commander", inp=0.5, dur=2.1, captions=[
        cap(0.15, 1.95, ["AND HE HAS A PLAN."], label="COMMANDER", **CONFIDENT)]),
    dict(act=2, beat="a2-commander", inp=0.3, dur=3.2),

    end_card(3),
]


CUTS = {
    "trailer": dict(edit=TRAILER, take="v2", out="BattlePlan_Trailer.mp4"),
    "gameplay": dict(edit=GAMEPLAY, take=GAMEPLAY_TAKE, out="BattlePlan_Gameplay.mp4"),
    "characters": dict(edit=CHARACTERS, take="v2", out="BattlePlan_Characters.mp4"),
}


def font(path, size):
    return ImageFont.truetype(str(path), size)


def tracked_width(draw, text, face, tracking):
    if not text:
        return 0
    return sum(draw.textlength(ch, font=face) for ch in text) + tracking * (len(text) - 1)


def draw_tracked(draw, x, y, text, face, fill, tracking=0):
    for ch in text:
        draw.text((x, y), ch, font=face, fill=fill)
        x += draw.textlength(ch, font=face) + tracking


MAX_LINE = WIDTH - MARGIN - 260


def caption_png(path, label, lines):
    """Lower-left caption: optional unit name in orange over one or two lines in bone."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    measure = ImageDraw.Draw(image)

    # Shrink to fit rather than run off the frame; a few lines are much longer than the rest.
    size = 76 if len(lines) == 1 else 56
    while size > 40:
        line_face = font(OSWALD_BOLD, size)
        widest = max(tracked_width(measure, text, line_face, 2) for text in lines)
        if widest <= MAX_LINE:
            break
        size -= 2

    gap = size + 12
    line_face = font(OSWALD_BOLD, size)
    label_face = font(OSWALD, 40)

    top = HEIGHT - 210 - gap * (len(lines) - 1)
    widest = max(tracked_width(measure, text, line_face, 2) for text in lines)

    # A soft plate behind the type keeps it legible over a bright board without a hard bar.
    ImageDraw.Draw(image).rectangle(
        [0, top - (100 if label else 40), int(MARGIN + widest + 150), HEIGHT - 94],
        fill=(10, 14, 17, 122),
    )

    draw = ImageDraw.Draw(image)
    if label:
        draw_tracked(draw, MARGIN, top - 62, label, label_face, ORANGE, tracking=9)
    for index, text in enumerate(lines):
        draw_tracked(draw, MARGIN, top + gap * index, text, line_face, BONE, tracking=2)

    # An accent rule ties the caption to the game's chrome.
    draw.rectangle(
        [MARGIN - 26, top - (58 if label else 4), MARGIN - 18, HEIGHT - 114], fill=ORANGE
    )

    image.save(path)


def card_png(path, rows):
    """Full-frame card on ink. Each row is (text, font path, size, colour, dy, tracking).

    A row's text may be a list of (text, colour) runs instead of a string, which sets them as one
    centred line in more than one colour — how the step cards tint their numeral without breaking
    the line into two rows that would then have to be centred against each other.
    """
    image = Image.new("RGBA", (WIDTH, HEIGHT), INK)
    draw = ImageDraw.Draw(image)
    for text, face_path, size, colour, dy, tracking in rows:
        face = font(face_path, size)
        runs = text if isinstance(text, list) else [(text, colour)]
        width = sum(tracked_width(draw, part, face, tracking) for part, _ in runs)
        width += tracking * (len(runs) - 1)
        x = (WIDTH - width) / 2
        for part, run_colour in runs:
            draw_tracked(draw, x, HEIGHT / 2 + dy, part, face, run_colour, tracking)
            x += tracked_width(draw, part, face, tracking) + tracking
    image.save(path)


def run(args):
    subprocess.run(args, check=True)


def build_shot(take, entry, out, work):
    beat = entry["beat"]
    src = ROOT / "Captures/Trailer/shots" / entry.get("take", take) / beat
    frames = sorted(src.glob("f*.jpg"))
    if not frames:
        raise SystemExit(f"no frames for beat {beat} in take {src.parent.name}")

    # Gameplay is never retimed. A unit crosses a cell a second and a round takes as long as it
    # takes; speeding that up in the edit sells a game that does not exist, so a shot is only ever
    # as long or as short as the footage it names.
    start = PREROLL_FRAMES + int(round(entry["inp"] * FPS))
    available = (len(frames) - start) / FPS
    if available <= 0:
        raise SystemExit(f"{beat}: in-point is past the end of the take")
    duration = round(min(entry["dur"], available), 3)

    args = [
        "ffmpeg", "-y", "-loglevel", "error",
        "-framerate", str(FPS),
        "-start_number", str(start),
        "-i", str(src / "f%04d.jpg"),
    ]

    # A live round is filmed wide so nothing is cropped; the punch-in onto the fighting happens
    # here, where the action's position is already known. (width, height, x, y) in source pixels.
    grade = entry.get("grade", GRADE)
    if entry.get("crop"):
        w, h, x, y = entry["crop"]
        grade = f"crop={w}:{h}:{x}:{y},scale={WIDTH}:{HEIGHT}:flags=lanczos,{grade}"

    captions = entry.get("captions", [])
    graph = [f"[0:v]{grade}[v0]"]
    for index, caption in enumerate(captions):
        png = work / f"{beat}-cap{index}.png"
        caption_png(png, caption["label"], caption["lines"])
        args += ["-loop", "1", "-i", str(png)]

        rise, fall = caption["rise"], caption["fall"]
        at = caption["at"]
        out_at = min(duration, at + caption["dur"]) - fall
        graph.append(
            f"[{index + 1}:v]format=rgba,"
            f"fade=in:st={at:.3f}:d={rise}:alpha=1,"
            f"fade=out:st={out_at:.3f}:d={fall}:alpha=1[c{index}]"
        )
        graph.append(f"[v{index}][c{index}]overlay=0:0[v{index + 1}]")

    args += [
        "-filter_complex", ";".join(graph),
        "-map", f"[v{len(captions)}]",
        "-t", f"{duration:.3f}",
        "-c:v", "libx264", "-crf", "16", "-preset", "medium",
        "-pix_fmt", "yuv420p", "-r", str(FPS),
        str(out),
    ]
    run(args)
    return duration


def build_card(entry, out, work, name):
    duration = entry["dur"]
    png = work / f"{name}.png"
    card_png(png, entry["card"])
    fade = entry.get("fade", 0.5)
    args = ["ffmpeg", "-y", "-loglevel", "error", "-loop", "1", "-i", str(png),
            "-t", f"{duration:.3f}"]
    if fade > 0:
        args += ["-vf", f"fade=out:st={duration - fade:.3f}:d={fade}"]
    args += [
        "-c:v", "libx264", "-crf", "16", "-preset", "medium",
        "-pix_fmt", "yuv420p", "-r", str(FPS),
        str(out),
    ]
    run(args)
    return duration


def assemble(pieces, transitions, final):
    """Chains the shots with dissolves so the film never butt-joins two static boards."""
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    for path, _ in pieces:
        args += ["-i", str(path)]

    graph = []
    label = "0:v"
    total = pieces[0][1]
    for index in range(1, len(pieces)):
        duration = pieces[index][1]
        blend = transitions[index]
        offset = total - blend
        nxt = f"x{index}"
        graph.append(
            f"[{label}][{index}:v]xfade=transition=fade:"
            f"duration={blend:.3f}:offset={offset:.3f}[{nxt}]"
        )
        label = nxt
        total += duration - blend

    args += [
        "-filter_complex", ";".join(graph),
        "-map", f"[{label}]",
        "-c:v", "libx264", "-crf", "17", "-preset", "medium",
        "-pix_fmt", "yuv420p", "-r", str(FPS), "-movflags", "+faststart",
        str(final),
    ]
    run(args)
    return total


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else "trailer"
    if name not in CUTS:
        raise SystemExit(f"unknown cut {name!r}; pick one of {', '.join(sorted(CUTS))}")
    cut = CUTS[name]
    take = sys.argv[2] if len(sys.argv) > 2 else cut["take"]

    build_dir = ROOT / "Captures/Trailer/build" / name
    if build_dir.exists():
        shutil.rmtree(build_dir)
    build_dir.mkdir(parents=True)

    pieces, transitions, timeline = [], [], []

    for index, entry in enumerate(cut["edit"]):
        shot = entry.get("beat") or f"card{index}"
        out = build_dir / f"{index:02d}-{shot}.mp4"
        if entry.get("beat"):
            seconds = build_shot(take, entry, out, build_dir)
        else:
            seconds = build_card(entry, out, build_dir, shot)
        pieces.append((out, seconds))
        transitions.append(entry.get("xf", DISSOLVE) if index else 0.0)
        timeline.append({"shot": shot, "act": entry["act"], "seconds": round(seconds, 3)})

    # Where each shot lands once the dissolves have eaten their overlap.
    clock = 0.0
    for index, entry in enumerate(timeline):
        clock -= transitions[index]
        entry["at"] = round(max(0.0, clock), 3)
        clock += entry["seconds"]
        print(f"  {entry['at']:6.2f}s  act{entry['act']}  {entry['shot']}")

    final = ROOT / "Captures/Trailer" / cut["out"]
    total = assemble(pieces, transitions, final)

    timeline_path = ROOT / "Captures/Trailer" / f"timeline-{name}.json"
    timeline_path.write_text(json.dumps(timeline, indent=2))
    print(f"\n{final}  ({total:.1f}s)")


if __name__ == "__main__":
    main()
