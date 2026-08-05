#!/usr/bin/env python3
"""Maintain the state file behind the live progress page.

The page polls `Captures/AbilityJuice/state.json`; this is the only writer. Every piece under
review keeps its own status, latest strip, critic verdict and a thumbnail history, so the page can
show an ability evolving across rounds rather than only its current state.

    progress.py init --pieces grenade:composite pogo:composite ...
    progress.py set grenade --status building --iteration 2
    progress.py strip grenade --path strips/r2/grenade.jpg --iteration 2
    progress.py verdict grenade --status rejected --text "..." --gap "..."
    progress.py round 3 --headline "round 3 - impact + camera"
"""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
JUICE = ROOT / "Captures" / "AbilityJuice"
STATE = JUICE / "state.json"


def load() -> dict:
    if STATE.exists():
        return json.loads(STATE.read_text())
    return {"round": 0, "headline": "", "captures": 0, "pieces": []}


def save(state: dict) -> None:
    state["updated"] = time.strftime("%H:%M:%S")
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(state, indent=2))


def find(state: dict, name: str) -> dict:
    for piece in state["pieces"]:
        if piece["name"] == name:
            return piece
    piece = {"name": name, "status": "pending", "iteration": 0, "history": []}
    state["pieces"].append(piece)
    return piece


def cmd_init(args: argparse.Namespace) -> None:
    state = load()
    if args.reset:
        state = {"round": 0, "headline": "", "captures": 0, "pieces": []}
    for spec in args.pieces:
        name, _, kind = spec.partition(":")
        piece = find(state, name)
        piece["kind"] = kind or "piece"
    save(state)
    print(f"{len(state['pieces'])} pieces")


def cmd_set(args: argparse.Namespace) -> None:
    state = load()
    piece = find(state, args.name)
    if args.status:
        piece["status"] = args.status
    if args.iteration is not None:
        piece["iteration"] = args.iteration
    if args.kind:
        piece["kind"] = args.kind
    save(state)


def cmd_strip(args: argparse.Namespace) -> None:
    state = load()
    piece = find(state, args.name)
    piece["strip"] = args.path
    piece["stamp"] = int(time.time())
    if args.iteration is not None:
        piece["iteration"] = args.iteration
    history = piece.setdefault("history", [])
    iteration = piece.get("iteration", 0)
    if not any(entry.get("iteration") == iteration for entry in history):
        history.append({"iteration": iteration, "strip": args.path})
    state["captures"] = state.get("captures", 0) + 1
    save(state)


def cmd_verdict(args: argparse.Namespace) -> None:
    state = load()
    piece = find(state, args.name)
    if args.status:
        piece["status"] = args.status
    if args.text:
        piece["verdict"] = args.text
    if args.gap:
        piece["gap"] = args.gap
    save(state)


def cmd_round(args: argparse.Namespace) -> None:
    state = load()
    state["round"] = args.number
    if args.headline:
        state["headline"] = args.headline
    save(state)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    p_init = sub.add_parser("init")
    p_init.add_argument("--pieces", nargs="+", required=True)
    p_init.add_argument("--reset", action="store_true")
    p_init.set_defaults(func=cmd_init)

    p_set = sub.add_parser("set")
    p_set.add_argument("name")
    p_set.add_argument("--status")
    p_set.add_argument("--iteration", type=int)
    p_set.add_argument("--kind")
    p_set.set_defaults(func=cmd_set)

    p_strip = sub.add_parser("strip")
    p_strip.add_argument("name")
    p_strip.add_argument("--path", required=True)
    p_strip.add_argument("--iteration", type=int)
    p_strip.set_defaults(func=cmd_strip)

    p_verdict = sub.add_parser("verdict")
    p_verdict.add_argument("name")
    p_verdict.add_argument("--status")
    p_verdict.add_argument("--text")
    p_verdict.add_argument("--gap")
    p_verdict.set_defaults(func=cmd_verdict)

    p_round = sub.add_parser("round")
    p_round.add_argument("number", type=int)
    p_round.add_argument("--headline")
    p_round.set_defaults(func=cmd_round)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
