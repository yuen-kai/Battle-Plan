import argparse
import math
import multiprocessing as mp
from pathlib import Path

import torch

from train import SCRIPTED, NetOpponent, cached_policy, evaluate

OPPONENTS = {bot.name: bot for bot in SCRIPTED}


def opponent_for(name):
    return OPPONENTS[name] if name in OPPONENTS else NetOpponent(name, greedy=True)


def play_chunk(job):
    checkpoint, opponent, matches, seed, fog, max_rounds = job
    torch.set_num_threads(1)
    results = evaluate(cached_policy(checkpoint), opponent_for(opponent), matches, seed, max_rounds, fog)
    return {key: round(value * matches) for key, value in results.items()}


def interval(successes, total):
    p = successes / total
    half = 1.96 * math.sqrt(p * (1 - p) / total)
    return p, half


def margin_interval(wins, losses, total):
    win, loss = wins / total, losses / total
    return win - loss, 1.96 * math.sqrt((win + loss - (win - loss) ** 2) / total)


def label(opponent):
    return opponent if opponent in OPPONENTS else Path(opponent).stem


def pooled(by_mode):
    wins = sum(t["win"] for t in by_mode.values())
    losses = sum(t["loss"] for t in by_mode.values())
    total = sum(sum(t.values()) for t in by_mode.values())
    return margin_interval(wins, losses, total)


def gate(candidate, champion, champion_path):
    margin, half = pooled(candidate[champion_path])
    reasons = [f"vs {label(champion_path)} {margin:+.1%} ±{half:.1%}" + ("" if margin - half > 0 else " (not clearly ahead)")]
    passed = margin - half > 0
    for opponent, by_mode in candidate.items():
        if opponent == champion_path or opponent not in champion:
            continue
        mine, mine_half = pooled(by_mode)
        theirs, theirs_half = pooled(champion[opponent])
        worse = mine - theirs < -math.hypot(mine_half, theirs_half)
        passed &= not worse
        reasons.append(f"vs {label(opponent)} {mine:+.1%} (champion {theirs:+.1%})" + (" WORSE" if worse else ""))
    return passed, reasons


def main():
    parser = argparse.ArgumentParser(description="Evaluate trained policies against scripted bots or other checkpoints.")
    parser.add_argument("checkpoints", nargs="+")
    parser.add_argument("--opponents", nargs="+", default=["scripted", "teacher"], help="scripted, teacher, or checkpoint paths")
    parser.add_argument("--champion", help="current best checkpoint; a candidate passes only if it clearly beats it "
                        "and does no worse than it against every other opponent")
    parser.add_argument("--matches", type=int, default=400, help="per opponent and fog mode, split evenly between sides")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--max-rounds", type=int, default=30)
    parser.add_argument("--seed", type=int, default=50_000_000)
    args = parser.parse_args()

    checkpoints = list(dict.fromkeys(([args.champion] if args.champion else []) + args.checkpoints))
    opponents = list(dict.fromkeys(args.opponents + ([args.champion] if args.champion else [])))
    chunk = 20
    results = {}
    with mp.get_context("spawn").Pool(args.workers) as pool:
        for checkpoint in checkpoints:
            print(checkpoint, flush=True)
            results[checkpoint] = {}
            for opponent in opponents:
                if opponent == checkpoint:
                    continue
                results[checkpoint][opponent] = {}
                for mode, fog in (("fog", True), ("clear", False)):
                    jobs = [
                        (checkpoint, opponent, chunk, args.seed + begin, fog, args.max_rounds)
                        for begin in range(0, args.matches, chunk)
                    ]
                    totals = {"win": 0, "loss": 0, "draw": 0}
                    for result in pool.map(play_chunk, jobs):
                        for key, value in result.items():
                            totals[key] += value
                    results[checkpoint][opponent][mode] = totals
                    n = sum(totals.values())
                    win, win_half = interval(totals["win"], n)
                    loss, loss_half = interval(totals["loss"], n)
                    margin, margin_half = margin_interval(totals["win"], totals["loss"], n)
                    print(
                        f"  vs {label(opponent):12} {mode:5} n={n}: "
                        f"win {win:.0%} ±{win_half:.0%}  loss {loss:.0%} ±{loss_half:.0%}  draw {totals['draw'] / n:.0%}  "
                        f"margin {margin:+.0%} ±{margin_half:.0%}",
                        flush=True,
                    )
    if len(results) > 1:
        print("\nscore = mean(win - loss) over every opponent and fog mode")
        scores = {c: sum(pooled(by_mode)[0] for by_mode in r.values()) / len(r) for c, r in results.items() if r}
        for checkpoint, score in sorted(scores.items(), key=lambda item: -item[1]):
            print(f"  {score:+.2f}  {checkpoint}")
    if args.champion:
        print(f"\ngate against {args.champion}")
        for checkpoint in checkpoints[1:]:
            passed, reasons = gate(results[checkpoint], results[args.champion], args.champion)
            print(f"  {'PASS' if passed else 'fail'}  {checkpoint}")
            for reason in reasons:
                print(f"        {reason}")


if __name__ == "__main__":
    main()
