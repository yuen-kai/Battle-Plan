import argparse
import random
import time
from collections import Counter

from battlesim.baselines import advance_policy
from battlesim.env import Game, random_policy
from battlesim.sim import Config


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--matches", type=int, default=200)
    parser.add_argument("--sample-hits", action="store_true")
    args = parser.parse_args()

    rng = random.Random(0)
    results = Counter()
    rounds = 0
    sim_seconds = 0.0
    start = time.perf_counter()
    for i in range(args.matches):
        game = Game(config=Config(sample_hits=args.sample_hits), seed=i)
        policies = (advance_policy(rng), random_policy(rng) if i % 2 else advance_policy(rng))
        while not game.match.finished:
            sim_seconds += game.play_round(policies)
        rounds += game.match.round_number
        results[game.match.winner] += 1
    elapsed = time.perf_counter() - start
    print(f"{args.matches} matches, {rounds} rounds, {elapsed:.2f}s wall")
    print(f"{args.matches / elapsed * 3600:,.0f} matches/hour on one core")
    print(f"{rounds / args.matches:.1f} rounds/match, {sim_seconds / max(1, rounds):.1f} game-seconds/round")
    print(f"simulated {sim_seconds / elapsed:,.0f}x faster than real time")
    print("winners:", dict(results))


if __name__ == "__main__":
    main()
