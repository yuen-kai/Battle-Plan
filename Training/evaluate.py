import argparse
import math
import multiprocessing as mp
from functools import partial
from pathlib import Path

import torch

from battlesim.baselines import advance_policy
from train import NetOpponent, cached_policy, evaluate

OPPONENTS = {
    "scripted": advance_policy,
    "teacher": partial(advance_policy, explore_bias=0.0),
}


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


def main():
    parser = argparse.ArgumentParser(description="Evaluate trained policies against scripted bots or other checkpoints.")
    parser.add_argument("checkpoints", nargs="+")
    parser.add_argument("--opponents", nargs="+", default=["scripted", "teacher"], help="scripted, teacher, or checkpoint paths")
    parser.add_argument("--matches", type=int, default=400, help="per opponent and fog mode, split evenly between sides")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--max-rounds", type=int, default=30)
    parser.add_argument("--seed", type=int, default=50_000_000)
    args = parser.parse_args()

    chunk = 20
    scores = {}
    with mp.get_context("spawn").Pool(args.workers) as pool:
        for checkpoint in args.checkpoints:
            print(checkpoint, flush=True)
            margins = []
            for opponent in args.opponents:
                for mode, fog in (("fog", True), ("clear", False)):
                    jobs = [
                        (checkpoint, opponent, chunk, args.seed + begin, fog, args.max_rounds)
                        for begin in range(0, args.matches, chunk)
                    ]
                    totals = {"win": 0, "loss": 0, "draw": 0}
                    for result in pool.map(play_chunk, jobs):
                        for key, value in result.items():
                            totals[key] += value
                    n = sum(totals.values())
                    win, win_half = interval(totals["win"], n)
                    loss, loss_half = interval(totals["loss"], n)
                    margins.append(win - loss)
                    print(
                        f"  vs {Path(opponent).stem if opponent not in OPPONENTS else opponent:12} {mode:5} n={n}: "
                        f"win {win:.0%} ±{win_half:.0%}  loss {loss:.0%} ±{loss_half:.0%}  draw {totals['draw'] / n:.0%}",
                        flush=True,
                    )
            scores[checkpoint] = sum(margins) / len(margins)
    if len(scores) > 1:
        print("\nscore = mean(win - loss) over every opponent and fog mode")
        for checkpoint, score in sorted(scores.items(), key=lambda item: -item[1]):
            print(f"  {score:+.2f}  {checkpoint}")


if __name__ == "__main__":
    main()
