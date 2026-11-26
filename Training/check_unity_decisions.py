import argparse
import json
from collections import Counter
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import torch

from battlesim.env import ACTION_COUNT, MOVE_ACTIONS, target_action
from battlesim.maps import MapLayout
from battlesim.observe import (
    PLANE_COUNT,
    UNIT_TYPES,
    canonical_mask,
    canonical_planes,
    encode_decision,
    world_action,
)
from battlesim.paths import STAY, legal_steps
from battlesim.sim import Config, Match
from battlesim.units import load_unit
from train import Policy

ROOT = Path(__file__).resolve().parent
PLANE_NAMES = [
    "wall", "visible", "smoke", "own hp", "own cooldown", "enemy hp", "memory age", "active path",
    "active head", "claimed", "enemy threat", "enemy marker", "own marker", "alerted",
] + [f"own {name}" for name in UNIT_TYPES] + [f"enemy {name}" for name in UNIT_TYPES] + [
    "team path",
] + [f"ally {name} ability" for name in UNIT_TYPES] + ["escape"]
SCALAR_NAMES = [f"unit {name}" for name in UNIT_TYPES] + [
    "steps taken", "move distance", "hp", "cooldown", "round", "own alive", "enemies alive",
    "dodge", "max dive", "fog", "team",
] + [f"enemy {slot} {field}" for slot in range(5) for field in [*UNIT_TYPES, "hp", "alive", "cooldown"]] + ["revising"]


def rebuild(record, stats):
    units = sorted(record["units"], key=lambda u: (u["team"], u["slot"]))
    teams = [[u for u in units if u["team"] == team] for team in (0, 1)]
    layout = MapLayout("unity", frozenset(map(tuple, record["walls"])))
    rosters = [[stats[u["type"]] for u in team] for team in teams]
    spawns = [[(round(u["x"]), round(u["y"])) for u in team] for team in teams]
    match = Match(layout, rosters, spawns, Config(fog_of_war=record["fog"]))
    by_slot = {}
    for sim_unit, real in zip(match.units, teams[0] + teams[1]):
        sim_unit.x, sim_unit.y = real["x"], real["y"]
        sim_unit.hp = real["hp"]
        sim_unit.alive = sim_unit.collider = real["alive"]
        sim_unit.cooldown = int(real["cooldown"])
        by_slot[(real["team"], real["slot"])] = sim_unit
    match.smoke = frozenset(map(tuple, record["smoke"]))
    match.round_number = record["round"]
    match._positions_version += 1
    return match, by_slot


def simulate(record, stats):
    match, by_slot = rebuild(record, stats)
    team = record["team"]
    unit = by_slot[(team, record["unit"])]
    path = [tuple(cell) for cell in record["path"]]
    claimed = set(map(tuple, record["claimed"]))
    teammates = [
        (by_slot[(team, mate["slot"])], [tuple(c) for c in mate["path"]], tuple(mate["target"]) if mate["target"] else None)
        for mate in record.get("teammates", [])
    ]
    builder = SimpleNamespace(
        match=match, team=team, unit=unit, paths={unit.uid: path}, claimed=claimed,
        dodge=record["dodge"], max_dive=record["maxDive"], revising=record.get("revising", False),
        teammate_plans=lambda: teammates,
    )
    memory = SimpleNamespace(last_known={
        index: ((m["x"], m["y"]), UNIT_TYPES[m["type"]], m["hp"], m["round"])
        for index, m in enumerate(record["memory"])
    })
    if record["dodge"]:
        telegraphs = []
        for t in record["telegraphs"]:
            caster = by_slot[(t["team"], t["slot"])]
            target = (t["tx"], t["ty"])
            telegraphs.append((t["team"], caster.stats.abilityName, target, match.telegraph_cells(caster, target)))
        alerted = set(map(tuple, record["alerted"]))
        match.pending = {
            "telegraphs": telegraphs,
            "alerted": {team: {u.uid for u in match.living(team) if u.cell in alerted}},
        }
        mask = legal_steps(path, record["maxDive"], match.walls) + [False] * (ACTION_COUNT - MOVE_ACTIONS)
    else:
        mask = legal_steps(path, unit.stats.moveDist, match.walls) + [False] * (ACTION_COUNT - MOVE_ACTIONS)
        if path[-1] in claimed and any(mask[1:MOVE_ACTIONS]):
            mask[STAY] = False
        if len(path) == 1:
            for cell in match.legal_ability_targets(unit):
                mask[target_action(cell)] = True
    planes, scalars = encode_decision(builder, memory)
    return planes.reshape(-1), scalars, np.array(mask, dtype=bool)


def choose(policy, planes, scalars, mask, team):
    with torch.no_grad():
        logits = policy.logits(
            torch.from_numpy(np.ascontiguousarray(canonical_planes(planes[: policy.planes], team))[None]),
            torch.from_numpy(scalars[None, : policy.scalars]),
            torch.from_numpy(canonical_mask(mask, team)[None]),
        )[0]
    return world_action(int(logits.argmax()), team)


def main():
    parser = argparse.ArgumentParser(description="Compare the Unity trained bot's inputs with the simulator's.")
    parser.add_argument("recording", help="unity-decisions-*.jsonl written by DevTrainedBotDuel.Run(record: true)")
    parser.add_argument("--checkpoint", default=str(ROOT / "models" / "bot.pt"))
    args = parser.parse_args()

    records = [json.loads(line) for line in Path(args.recording).read_text().splitlines() if line.strip()]
    stats = {name: load_unit(name) for name in {u["type"] for r in records for u in r["units"]}}
    policy = Policy.from_state(torch.load(args.checkpoint, map_location="cpu"))
    policy.eval()

    plane_misses, scalar_misses, examples = Counter(), Counter(), {}
    mask_misses = same_action = replayed = 0
    for record in records:
        sim_planes, sim_scalars, sim_mask = simulate(record, stats)
        unity_planes = np.zeros(sim_planes.size, dtype=np.float32)
        for index, value in record["planes"]:
            unity_planes[index] = value
        unity_scalars = np.array(record["scalars"], dtype=np.float32)
        unity_mask = np.zeros(ACTION_COUNT, dtype=bool)
        unity_mask[record["mask"]] = True

        cells = sim_planes.size // PLANE_COUNT
        for plane in range(PLANE_COUNT):
            diff = np.abs(sim_planes[plane * cells:(plane + 1) * cells] - unity_planes[plane * cells:(plane + 1) * cells])
            if diff.max() > 1e-4:
                plane_misses[PLANE_NAMES[plane]] += 1
                examples.setdefault(PLANE_NAMES[plane], (record["round"], record["dodge"], int((diff > 1e-4).sum())))
        for index in np.flatnonzero(np.abs(sim_scalars - unity_scalars) > 1e-4):
            scalar_misses[SCALAR_NAMES[index]] += 1
            examples.setdefault(SCALAR_NAMES[index], (float(unity_scalars[index]), float(sim_scalars[index])))
        mask_misses += int((sim_mask != unity_mask).any())

        shape = (PLANE_COUNT, 10, cells // 10)
        replayed += choose(policy, unity_planes.reshape(shape), unity_scalars, unity_mask, record["team"]) == record["action"]
        same_action += choose(policy, sim_planes.reshape(shape), sim_scalars, sim_mask, record["team"]) == record["action"]

    total = len(records)
    dodges = sum(r["dodge"] for r in records)
    print(f"{total} decisions ({dodges} dodge), {len({(r['round'], r['fog']) for r in records})} rounds")
    print(f"Unity inputs through PyTorch reproduce Unity's move: {replayed / total:.1%}")
    print(f"simulator inputs give the same move as Unity: {same_action / total:.1%}")
    print(f"legal-move masks differ: {mask_misses / total:.1%}")
    print("input layers that differ (share of decisions; first example round, dodge, cells):")
    for name, count in plane_misses.most_common():
        print(f"  {name:14} {count / total:6.1%}  {examples[name]}")
    print("numbers that differ (share of decisions; first example unity, sim):")
    for name, count in scalar_misses.most_common():
        print(f"  {name:20} {count / total:6.1%}  {examples[name]}")


if __name__ == "__main__":
    main()
