import argparse
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path

from battlesim.maps import parse_catalog
from battlesim.sim import Config, Match
from battlesim.units import load_unit

PARITY_DIR = Path(__file__).resolve().parent / "parity"


def load_records(paths):
    for path in paths:
        for line in Path(path).read_text().splitlines():
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                continue


def cell_pairs(flat):
    return [(flat[i], flat[i + 1]) for i in range(0, len(flat) - 1, 2)]


def replay(record, maps, stats, config):
    real_units = sorted(record["units"], key=lambda u: (u["team"], u["slot"]))
    teams = [[u for u in real_units if u["team"] == team] for team in (0, 1)]
    rosters = [[stats[u["name"]] for u in team] for team in teams]
    spawns = [[(round(u["x"]), round(u["y"])) for u in team] for team in teams]
    match = Match(maps[record["map"]], rosters, spawns, config, seed=record["round"])
    match.round_number = record["round"] - 1
    smoke = cell_pairs(record.get("smoke", []))
    if smoke:
        match.register_smoke(smoke, 1)
    plans, dives = {}, {}
    for sim_unit, real in zip(match.units, teams[0] + teams[1]):
        sim_unit.x, sim_unit.y = real["x"], real["y"]
        sim_unit.facing = real["facing"]
        sim_unit.hp = real["hp"]
        sim_unit.slot = real["slot"]
        ability = real.get("ability", False)
        sim_unit.cooldown = 0 if ability else real.get("cooldown", 0)
        if ability:
            plans[sim_unit.uid] = ("ability", (real["targetX"], real["targetY"]))
        elif real.get("dive"):
            dives[sim_unit.uid] = cell_pairs(real["path"])
        else:
            plans[sim_unit.uid] = cell_pairs(real["path"])
    match.begin_round(plans)
    for uid, path in dives.items():
        match.pending["paths"][uid] = path
        match.pending["dives"].add(uid)
    duration = match.execute()
    return list(zip(match.units, teams[0] + teams[1])), match, duration


def pearson(xs, ys):
    if len(xs) < 2 or statistics.pstdev(xs) == 0 or statistics.pstdev(ys) == 0:
        return float("nan")
    return statistics.correlation(xs, ys)


def main():
    parser = argparse.ArgumentParser(description="Replay recorded Unity rounds in the simulator.")
    parser.add_argument("files", nargs="*", help="rounds-*.jsonl files (default: newest in parity/)")
    parser.add_argument("--worst", type=int, default=8)
    parser.add_argument("--no-frame-delays", action="store_true")
    parser.add_argument(
        "--production-frames",
        dest="match_frames",
        action="store_false",
        help="simulate at the production frame rate instead of the recording's",
    )
    args = parser.parse_args()

    files = args.files or sorted(PARITY_DIR.glob("rounds-*.jsonl"))[-1:]
    if not files:
        raise SystemExit("No parity recordings found in Training/parity/.")
    maps = parse_catalog()
    stats = defaultdict(lambda: None)

    rows = []
    rounds = []
    for record in load_records(files):
        for unit in record["units"]:
            if stats[unit["name"]] is None:
                stats[unit["name"]] = load_unit(unit["name"])
        frame = record["duration"] / max(1, record["frames"])
        config = Config(dt=frame, game_frame=frame, unity_frame_delays=not args.no_frame_delays) if args.match_frames else Config(unity_frame_delays=not args.no_frame_delays)
        pairs, match, sim_duration = replay(record, maps, stats, config)
        team_damage = {"real": [0.0, 0.0], "sim": [0.0, 0.0]}
        any_real_death = False
        for sim_unit, real in pairs:
            real_taken = real["hp"] - real["hpAfter"]
            sim_taken = min(real["hp"], real["hp"] - sim_unit.hp)
            attacker = 1 - real["team"]
            team_damage["real"][attacker] += real_taken
            team_damage["sim"][attacker] += sim_taken
            any_real_death |= not real["aliveAfter"]
            rows.append(
                {
                    "name": real["name"],
                    "real": real_taken,
                    "sim": sim_taken,
                    "real_dead": not real["aliveAfter"],
                    "sim_dead": not sim_unit.alive,
                    "real_shots": real["shots"],
                    "sim_shots": match.shots_fired.get(sim_unit.uid, 0),
                    "cell_match": (round(real["xAfter"]), round(real["yAfter"])) == sim_unit.cell,
                    "clean": None,
                }
            )
        for row in rows[-len(pairs):]:
            row["clean"] = not any_real_death
        rounds.append(
            {
                "abilities": sorted({stats[u["name"]].abilityName for u in record["units"] if u.get("ability")}),
                "dives": sum(bool(u.get("dive")) for u in record["units"]),
                "id": f"match {record['match']} round {record['round']} ({record['map']})",
                "real": team_damage["real"],
                "sim": team_damage["sim"],
                "real_seconds": record["duration"],
                "sim_seconds": sim_duration,
                "frame_seconds": record["duration"] / max(1, record["frames"]),
            }
        )

    combat = [r for r in rows if r["real"] > 0 or r["sim"] > 0]
    real_total = sum(r["real"] for r in rows)
    sim_total = sum(r["sim"] for r in rows)
    print(f"{len(rounds)} rounds, {len(rows)} unit-rounds, {len(combat)} with damage in either")
    print(f"total damage  real {real_total:8.0f}   sim {sim_total:8.0f}   sim/real {sim_total / max(1e-9, real_total):.2f}")
    if combat:
        mae = statistics.fmean(abs(r["real"] - r["sim"]) for r in combat)
        print(f"per unit-round damage taken: MAE {mae:.1f}, correlation {pearson([r['real'] for r in combat], [r['sim'] for r in combat]):.2f}")
        clean = [r for r in combat if r["clean"]]
        if clean:
            print(f"  rounds with no real deaths: MAE {statistics.fmean(abs(r['real'] - r['sim']) for r in clean):.1f}, "
                  f"correlation {pearson([r['real'] for r in clean], [r['sim'] for r in clean]):.2f}, n={len(clean)}")
    team_real = [d for r in rounds for d in r["real"]]
    team_sim = [d for r in rounds for d in r["sim"]]
    print(f"per team-round damage dealt: correlation {pearson(team_real, team_sim):.2f}")

    deaths = defaultdict(int)
    for r in rows:
        deaths[(r["real_dead"], r["sim_dead"])] += 1
    print(f"deaths: both {deaths[(True, True)]}, real only {deaths[(True, False)]}, sim only {deaths[(False, True)]}")
    print(f"end cell agreement: {statistics.fmean(r['cell_match'] for r in rows):.1%}")
    print(f"round length: real {statistics.fmean(r['real_seconds'] for r in rounds):.2f}s, "
          f"sim {statistics.fmean(r['sim_seconds'] for r in rounds):.2f}s, "
          f"real frame {statistics.fmean(r['frame_seconds'] for r in rounds) * 1000:.0f} ms of game time")

    print("\nby unit type      dmg taken real   sim   ratio | shots real   sim")
    by_name = defaultdict(list)
    for r in rows:
        by_name[r["name"]].append(r)
    for name, group in sorted(by_name.items()):
        real = sum(r["real"] for r in group)
        sim = sum(r["sim"] for r in group)
        shots_real = sum(r["real_shots"] for r in group)
        shots_sim = sum(r["sim_shots"] for r in group)
        ratio = sim / real if real else math.nan
        print(f"  {name:<12} {real:10.0f} {sim:8.0f} {ratio:7.2f} | {shots_real:8d} {shots_sim:6d}")

    by_ability = defaultdict(list)
    for r in rounds:
        for name in r["abilities"]:
            by_ability[name].append(r)
        if r["dives"]:
            by_ability["(rounds with dives)"].append(r)
    if by_ability:
        print("\nrounds where an ability was cast   rounds   team dmg real    sim  corr")
        for name, group in sorted(by_ability.items()):
            real = [d for r in group for d in r["real"]]
            sim = [d for r in group for d in r["sim"]]
            print(f"  {name:<32} {len(group):6d} {sum(real):12.0f} {sum(sim):6.0f} {pearson(real, sim):5.2f}")

    print(f"\nlargest disagreements (team damage dealt, [team0, team1]):")
    worst = sorted(rounds, key=lambda r: -sum(abs(a - b) for a, b in zip(r["real"], r["sim"])))
    for r in worst[: args.worst]:
        real = ", ".join(f"{d:.0f}" for d in r["real"])
        sim = ", ".join(f"{d:.0f}" for d in r["sim"])
        print(f"  {r['id']}: real [{real}] sim [{sim}]")


if __name__ == "__main__":
    main()
