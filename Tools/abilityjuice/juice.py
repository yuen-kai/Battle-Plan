#!/usr/bin/env python3
"""Turn raw ability capture frames into the artefacts the review loop judges.

The capture rig writes one JPEG per sampled frame plus a `frames.csv` of timestamps. This tool
finds the moment of impact in that sequence, cuts the three frames that define how an ability reads
(wind-up, impact, aftermath), and lays them out as unlabelled strips. It also builds the blind
comparison sheets: our strip and a Clash Mini strip stacked in a randomised order, with the answer
key written to a separate file the critic never opens.

Subcommands:
    strips     build a 3-frame strip for every shot in a capture run
    contact    build a full contact sheet of a shot (all frames), for diagnosing timing
    compare    build blind A/B sheets pairing our strips against reference strips
    refstrips  build reference strips from a folder of Clash Mini frames
    reveal     print the answer key for a blind sheet
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import random
import sys
from dataclasses import dataclass, asdict
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
JUICE = ROOT / "Captures" / "AbilityJuice"
SHOTS = JUICE / "shots"
STRIPS = JUICE / "strips"
COMPARE = JUICE / "compare"
REFERENCE = JUICE / "reference"

PANEL = 512
GUTTER = 8
BACKDROP = (10, 11, 15)

# How far either side of the impact frame the other two panels are cut. Wind-up is the frame where
# a player should already be bracing; aftermath is late enough that the flash is gone and only the
# consequence remains.
DEFAULT_WINDUP_LEAD = 0.15
DEFAULT_AFTERMATH_LAG = 0.42

# Abilities whose payoff is a slow bloom rather than a flash need a wider spread to show change.
SHOT_TIMING = {
    "smoke": (0.22, 0.75),
    "shieldrush": (0.30, 0.70),
    "arealock": (0.20, 0.55),
}

# Pieces with no peak to find. A wind-up is a countdown: its three interesting moments are early,
# just-before-release, and after the cut, and none of them is the brightest frame in the shot.
ABSOLUTE_TIMING = {
    "windup": (0.35, 0.95, 1.40),
    # A hit's visual event lives in its first 130ms, but the body's recovery spring runs for well
    # over a second, so any motion-weighted search drifts onto the spring and cuts an empty board.
    "hitreact": (-0.10, 0.033, 0.467),
}


@dataclass
class ShotPick:
    shot: str
    windup_frame: int
    impact_frame: int
    aftermath_frame: int
    windup_time: float
    impact_time: float
    aftermath_time: float
    peak_score: float


def load_times(shot_dir: Path) -> list[tuple[int, float]]:
    csv_path = shot_dir / "frames.csv"
    if not csv_path.exists():
        frames = sorted(shot_dir.glob("f*.jpg"))
        return [(i, i / 30.0) for i in range(len(frames))]
    rows: list[tuple[int, float]] = []
    with csv_path.open() as handle:
        for row in csv.DictReader(handle):
            rows.append((int(row["frame"]), float(row["time_seconds"])))
    return rows


def frame_path(shot_dir: Path, index: int) -> Path:
    return shot_dir / f"f{index:04d}.jpg"


def score_frames(shot_dir: Path, times: list[tuple[int, float]]) -> np.ndarray:
    """Rank frames by how much of a money frame each one is.

    The critics judging these strips measure three things: how much of the panel is blown out, how
    much opaque material darker than the board has appeared, and how much of it carries saturated
    colour. Scoring on brightness and bulk change alone picked frames where an effect was loud but
    empty, and on one shot it cut a quarter of a second after the event had finished. This scores
    the same quantities a critic will.
    """
    frames: list[np.ndarray] = []
    for index, _ in times:
        path = frame_path(shot_dir, index)
        if not path.exists():
            frames.append(np.zeros((128, 128, 3), dtype=np.float32))
            continue
        image = Image.open(path).convert("RGB").resize((128, 128), Image.BILINEAR)
        frames.append(np.asarray(image, dtype=np.float32) / 255.0)

    stack = np.stack(frames)
    luma = stack @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    peak = stack.max(axis=3)
    saturation = (peak - stack.min(axis=3)) / np.maximum(peak, 1e-6)

    preroll = [i for i, (_, t) in enumerate(times) if t < 0]
    baseline = np.median(luma[preroll], axis=0) if preroll else np.median(luma[:3], axis=0)

    hot = np.mean((luma > 0.78) & (saturation > 0.45), axis=(1, 2))
    material = np.mean((baseline - luma) > 0.235, axis=(1, 2))
    flash = np.percentile(luma.reshape(len(luma), -1), 99.9, axis=1)

    def unit(values: np.ndarray) -> np.ndarray:
        span = float(values.max() - values.min())
        return (values - values.min()) / span if span > 1e-6 else np.zeros_like(values)

    return 0.4 * unit(hot) + 0.4 * unit(material) + 0.2 * unit(flash)


def nearest_frame(times: list[tuple[int, float]], target: float) -> int:
    return min(range(len(times)), key=lambda i: abs(times[i][1] - target))


def pick(shot_dir: Path, shot: str) -> ShotPick:
    times = load_times(shot_dir)
    if not times:
        raise SystemExit(f"no frames in {shot_dir}")

    scores = score_frames(shot_dir, times)
    live = [i for i, (_, t) in enumerate(times) if t >= 0]
    peak = max(live, key=lambda i: scores[i]) if live else int(np.argmax(scores))

    if shot in ABSOLUTE_TIMING:
        early, middle, late = ABSOLUTE_TIMING[shot]
        windup, peak, aftermath = (nearest_frame(times, t) for t in (early, middle, late))
        impact_time = times[peak][1]
    else:
        impact_time = times[peak][1]
        lead, lag = SHOT_TIMING.get(shot, (DEFAULT_WINDUP_LEAD, DEFAULT_AFTERMATH_LAG))
        windup = nearest_frame(times, impact_time - lead)
        aftermath = nearest_frame(times, impact_time + lag)

    return ShotPick(
        shot=shot,
        windup_frame=times[windup][0],
        impact_frame=times[peak][0],
        aftermath_frame=times[aftermath][0],
        windup_time=times[windup][1],
        impact_time=impact_time,
        aftermath_time=times[aftermath][1],
        peak_score=float(scores[peak]),
    )


def strip_from(paths: list[Path], panel: int = PANEL) -> Image.Image:
    width = panel * len(paths) + GUTTER * (len(paths) - 1)
    sheet = Image.new("RGB", (width, panel), BACKDROP)
    for i, path in enumerate(paths):
        tile = Image.open(path).convert("RGB")
        side = min(tile.size)
        left = (tile.width - side) // 2
        top = (tile.height - side) // 2
        tile = tile.crop((left, top, left + side, top + side)).resize(
            (panel, panel), Image.LANCZOS
        )
        sheet.paste(tile, (i * (panel + GUTTER), 0))
    return sheet


def cmd_strips(args: argparse.Namespace) -> None:
    run_dir = SHOTS / args.tag
    if not run_dir.exists():
        raise SystemExit(f"no capture run at {run_dir}")

    out_dir = STRIPS / args.tag
    out_dir.mkdir(parents=True, exist_ok=True)
    picks: list[ShotPick] = []

    for shot_dir in sorted(p for p in run_dir.iterdir() if p.is_dir()):
        shot = shot_dir.name
        if args.only and shot not in args.only:
            continue
        chosen = pick(shot_dir, shot)
        picks.append(chosen)
        frames = [
            frame_path(shot_dir, chosen.windup_frame),
            frame_path(shot_dir, chosen.impact_frame),
            frame_path(shot_dir, chosen.aftermath_frame),
        ]
        strip_from(frames).save(out_dir / f"{shot}.jpg", quality=94)
        print(
            f"{shot}: impact t={chosen.impact_time:+.2f}s "
            f"(frames {chosen.windup_frame}/{chosen.impact_frame}/{chosen.aftermath_frame})"
        )

    (out_dir / "picks.json").write_text(json.dumps([asdict(p) for p in picks], indent=2))


def cmd_contact(args: argparse.Namespace) -> None:
    shot_dir = SHOTS / args.tag / args.shot
    times = load_times(shot_dir)
    step = max(1, len(times) // args.max_frames)
    chosen = times[::step]

    columns = args.columns
    rows = (len(chosen) + columns - 1) // columns
    cell = 224
    sheet = Image.new("RGB", (columns * cell, rows * (cell + 16)), BACKDROP)
    draw = ImageDraw.Draw(sheet)

    for i, (index, time_s) in enumerate(chosen):
        path = frame_path(shot_dir, index)
        if not path.exists():
            continue
        tile = Image.open(path).convert("RGB").resize((cell, cell), Image.LANCZOS)
        x = (i % columns) * cell
        y = (i // columns) * (cell + 16)
        sheet.paste(tile, (x, y))
        draw.text((x + 4, y + cell + 2), f"{time_s:+.2f}s", fill=(190, 200, 215))

    out = STRIPS / args.tag / f"{args.shot}_contact.jpg"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=90)
    print(out)


def collect_triples(root: Path) -> dict[str, dict[str, Path]]:
    """Groups `<clip>-<id>-<phase>` frames under `root` into complete triples."""
    triples: dict[str, dict[str, Path]] = {}
    impact_dir = root / "impact"
    if not impact_dir.is_dir():
        return triples
    for path in sorted(impact_dir.glob("*-impact.*")):
        key = path.name.rsplit("-impact", 1)[0]
        windup = next(iter(sorted((root / "windup").glob(f"{key}-windup.*"))), None)
        aftermath = next(iter(sorted((root / "aftermath").glob(f"{key}-aftermath.*"))), None)
        if windup and aftermath:
            triples[key] = {"windup": windup, "impact": path, "aftermath": aftermath}
    return triples


def cmd_refstrips(args: argparse.Namespace) -> None:
    """Build 3-panel Clash Mini strips from wind-up/impact/aftermath triples.

    Frames are mined from gameplay footage by detecting the flash and cutting at -0.28s, the peak,
    and +0.38s, so a strip is one cast sampled three times rather than three unrelated stills — the
    same thing our own capture rig produces, which is what makes the comparison fair.
    """
    out_dir = STRIPS / "reference"
    out_dir.mkdir(parents=True, exist_ok=True)
    for stale in out_dir.glob("*.jpg"):
        stale.unlink()

    # The top-level folders hold the reviewed set, every frame checked by eye against a contact
    # sheet. The mining staging area is only a fallback: it still contains menus, card art and chest
    # animations, and comparing ourselves against those would flatter us.
    triples = collect_triples(REFERENCE)
    source = "reviewed"
    if not triples:
        triples = collect_triples(REFERENCE / "_staging" / "cand")
        source = "staging"
        curated_path = REFERENCE / "CURATED.txt"
        if curated_path.exists():
            allowed = {
                line.strip()
                for line in curated_path.read_text().splitlines()
                if line.strip() and not line.startswith("#")
            }
            triples = {key: group for key, group in triples.items() if key in allowed}

    if not triples:
        print("no wind-up/impact/aftermath triples found yet", file=sys.stderr)
        return

    # Rank by how much event the impact frame actually contains, so the strips we compare against
    # are Clash Mini at its loudest rather than an arbitrary sample.
    ranked: list[tuple[float, str]] = []
    for key, group in triples.items():
        image = Image.open(group["impact"]).convert("L")
        side = min(image.size)
        image = image.crop(
            (
                (image.width - side) // 2,
                (image.height - side) // 2,
                (image.width - side) // 2 + side,
                (image.height - side) // 2 + side,
            )
        ).resize((96, 96), Image.BILINEAR)
        pixels = np.asarray(image, dtype=np.float32) / 255.0
        ranked.append((float(np.percentile(pixels, 99.5) + pixels.std()), key))
    ranked.sort(reverse=True)

    for _, key in ranked[: args.limit]:
        group = triples[key]
        strip_from([group["windup"], group["impact"], group["aftermath"]]).save(
            out_dir / f"{key}.jpg", quality=94
        )
    print(
        f"{min(len(ranked), args.limit)} reference strips "
        f"from {len(triples)} {source} triples"
    )


def cmd_compare(args: argparse.Namespace) -> None:
    ours_dir = STRIPS / args.tag
    refs = sorted((STRIPS / "reference").glob("*.jpg"))
    if not refs:
        raise SystemExit("no reference strips; run refstrips first")

    COMPARE.mkdir(parents=True, exist_ok=True)
    out_dir = COMPARE / args.tag
    out_dir.mkdir(parents=True, exist_ok=True)
    key: dict[str, dict[str, str]] = {}

    for ours in sorted(ours_dir.glob("*.jpg")):
        if ours.stem.endswith("_contact"):
            continue
        if args.only and ours.stem not in args.only:
            continue

        # Deterministic per (tag, shot) so a re-run does not silently reshuffle a sheet the critic
        # already reasoned about, but unguessable without the key.
        seed = int(hashlib.sha256(f"{args.tag}/{ours.stem}".encode()).hexdigest()[:8], 16)
        rng = random.Random(seed)
        reference = rng.choice(refs)
        ours_on_top = rng.random() < 0.5

        top, bottom = (ours, reference) if ours_on_top else (reference, ours)
        top_image = Image.open(top).convert("RGB")
        bottom_image = Image.open(bottom).convert("RGB")
        width = max(top_image.width, bottom_image.width)
        sheet = Image.new(
            "RGB", (width, top_image.height + bottom_image.height + GUTTER * 3), BACKDROP
        )
        sheet.paste(top_image, ((width - top_image.width) // 2, GUTTER))
        sheet.paste(
            bottom_image,
            ((width - bottom_image.width) // 2, top_image.height + GUTTER * 2),
        )

        draw = ImageDraw.Draw(sheet)
        draw.text((6, 2), "A", fill=(150, 160, 175))
        draw.text((6, top_image.height + GUTTER * 2 - 12), "B", fill=(150, 160, 175))

        out = out_dir / f"{ours.stem}_blind.jpg"
        sheet.save(out, quality=94)
        key[ours.stem] = {
            "A": "ours" if ours_on_top else "reference",
            "B": "reference" if ours_on_top else "ours",
            "reference_file": reference.name,
        }
        print(out)

    (out_dir / "ANSWER_KEY.json").write_text(json.dumps(key, indent=2))


def cmd_reveal(args: argparse.Namespace) -> None:
    key_path = COMPARE / args.tag / "ANSWER_KEY.json"
    print(key_path.read_text())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    p_strips = sub.add_parser("strips", help="build 3-frame strips for a capture run")
    p_strips.add_argument("tag")
    p_strips.add_argument("--only", nargs="*", default=None)
    p_strips.set_defaults(func=cmd_strips)

    p_contact = sub.add_parser("contact", help="build a full contact sheet for one shot")
    p_contact.add_argument("tag")
    p_contact.add_argument("shot")
    p_contact.add_argument("--columns", type=int, default=10)
    p_contact.add_argument("--max-frames", type=int, default=60)
    p_contact.set_defaults(func=cmd_contact)

    p_ref = sub.add_parser("refstrips", help="build reference strips from Clash Mini frames")
    p_ref.add_argument("--limit", type=int, default=24)
    p_ref.set_defaults(func=cmd_refstrips)

    p_cmp = sub.add_parser("compare", help="build blind A/B sheets")
    p_cmp.add_argument("tag")
    p_cmp.add_argument("--only", nargs="*", default=None)
    p_cmp.set_defaults(func=cmd_compare)

    p_rev = sub.add_parser("reveal", help="print the answer key")
    p_rev.add_argument("tag")
    p_rev.set_defaults(func=cmd_reveal)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
