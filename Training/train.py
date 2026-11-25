import argparse
import json
import random
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

from battlesim.baselines import advance_policy
from battlesim.env import MOVE_ACTIONS, DodgeBuilder, Game, PathBuilder
from battlesim.observe import PLANE_COUNT, SCALAR_COUNT, encode_decision
from battlesim.reward import Reward
from battlesim.maps import COLUMNS, ROWS
from battlesim.sim import Config

RUNS_DIR = Path(__file__).resolve().parent / "runs"


class Policy(nn.Module):
    def __init__(self, width=32, hidden=256):
        super().__init__()
        self.conv = nn.Sequential(
            nn.Conv2d(PLANE_COUNT, width, 3, padding=1),
            nn.ReLU(),
            nn.Conv2d(width, width, 3, padding=1),
            nn.ReLU(),
        )
        self.body = nn.Sequential(
            nn.Linear(width * ROWS * COLUMNS + SCALAR_COUNT, hidden), nn.ReLU()
        )
        self.context = nn.Linear(hidden, width)
        self.target_head = nn.Conv2d(width, 1, 1)
        self.move_logits = nn.Linear(hidden, MOVE_ACTIONS)
        self.value = nn.Linear(hidden, 1)

    def forward(self, planes, scalars, masks):
        board = self.conv(planes)
        features = self.body(torch.cat([board.flatten(1), scalars], dim=1))
        conditioned = torch.relu(board + self.context(features)[:, :, None, None])
        targets = self.target_head(conditioned).flatten(1)
        logits = torch.cat([self.move_logits(features), targets], dim=1).masked_fill(~masks, -1e9)
        return logits, self.value(features).squeeze(-1)


class Slot:
    def __init__(self, seed, opponent, max_rounds, reward):
        self.reward = reward
        self.rng = random.Random(seed)
        self.game = Game(config=Config(max_rounds=max_rounds), seed=seed)
        self.opponent = opponent(self.rng)
        self.builder = PathBuilder(self.game.match, 0)
        self.opponent_dives = {}
        self.episode_return = 0.0

    def observe(self):
        planes, scalars = encode_decision(self.builder, self.game.memory[0])
        return planes, scalars, np.array(self.builder.mask(), dtype=bool)

    def step(self, action):
        self.builder.act(action)
        if not self.builder.done:
            return 0.0, False, False
        match = self.game.match
        if self.builder.dodge:
            dives = self.builder.dives()
        else:
            plans = self.builder.plans()
            plans.update(self.game.plan(1, self.opponent))
            alerted = match.begin_round(plans)
            self.opponent_dives = self.game.dodge(1, alerted[1], self.opponent) if 1 in alerted else {}
            if 0 in alerted:
                self.builder = DodgeBuilder(match, 0, alerted[0])
                return 0.0, False, False
            dives = {}
        match.apply_dodges({**dives, **self.opponent_dives})
        before = self.reward.potential(match, 0)
        self.game.finish_round()
        reward = self.reward.round_reward(before, match, 0)
        if match.finished:
            return reward, True, True
        self.builder = PathBuilder(match, 0)
        return reward, False, True


class VecGames:
    def __init__(self, count, opponent, max_rounds, seed, reward):
        self.reward = reward
        self.opponent = opponent
        self.max_rounds = max_rounds
        self.next_seed = seed
        self.slots = [self._new_slot() for _ in range(count)]

    def _new_slot(self):
        self.next_seed += 1
        return Slot(self.next_seed, self.opponent, self.max_rounds, self.reward)

    def observe(self):
        planes, scalars, masks = zip(*(slot.observe() for slot in self.slots))
        return (
            torch.from_numpy(np.stack(planes)),
            torch.from_numpy(np.stack(scalars)),
            torch.from_numpy(np.stack(masks)),
        )

    def step(self, actions):
        rewards, dones, boundaries, finished = [], [], [], []
        for i, (slot, action) in enumerate(zip(self.slots, actions)):
            reward, done, boundary = slot.step(int(action))
            slot.episode_return += reward
            rewards.append(reward)
            dones.append(done)
            boundaries.append(boundary)
            if done:
                finished.append((slot.episode_return, slot.game.match.winner))
                self.slots[i] = self._new_slot()
        return (
            np.array(rewards, dtype=np.float32),
            np.array(dones, dtype=np.float32),
            np.array(boundaries, dtype=bool),
            finished,
        )


def round_advantages(rewards, values, next_values, dones, boundaries, gamma, lam):
    advantages = torch.zeros_like(rewards)
    gae = torch.zeros_like(next_values)
    for t in reversed(range(rewards.shape[0])):
        following = next_values if t == rewards.shape[0] - 1 else values[t + 1]
        discount = torch.where(boundaries[t], torch.tensor(gamma), torch.tensor(1.0))
        trace = torch.where(boundaries[t], torch.tensor(lam), torch.tensor(1.0))
        delta = rewards[t] + discount * following * (1 - dones[t]) - values[t]
        gae = delta + discount * trace * (1 - dones[t]) * gae
        advantages[t] = gae
    return advantages


def evaluate(policy, opponent, matches, seed, max_rounds):
    results = {0: 0, 1: 0, None: 0}
    games = [Slot(seed + i, opponent, max_rounds, Reward()) for i in range(matches)]
    live = list(range(matches))
    with torch.no_grad():
        while live:
            obs = [games[i].observe() for i in live]
            planes, scalars, masks = (torch.from_numpy(np.stack(x)) for x in zip(*obs))
            logits, _ = policy(planes, scalars, masks)
            actions = logits.argmax(dim=1).tolist()
            still = []
            for i, action in zip(live, actions):
                _, done, _ = games[i].step(action)
                if done:
                    results[games[i].game.match.winner] += 1
                else:
                    still.append(i)
            live = still
    return results[0] / matches, results[1] / matches, results[None] / matches


def main():
    parser = argparse.ArgumentParser(description="PPO on the Battle Plan simulator.")
    parser.add_argument("--steps", type=int, default=1_000_000)
    parser.add_argument("--envs", type=int, default=64)
    parser.add_argument("--rollout", type=int, default=128)
    parser.add_argument("--epochs", type=int, default=4)
    parser.add_argument("--minibatch", type=int, default=1024)
    parser.add_argument("--lr", type=float, default=3e-4)
    parser.add_argument("--gamma", type=float, default=0.98, help="discount per round")
    parser.add_argument("--lam", type=float, default=0.95, help="GAE lambda per round")
    parser.add_argument("--draw-reward", type=float, default=-0.5)
    parser.add_argument("--hp-weight", type=float, default=0.5)
    parser.add_argument("--count-weight", type=float, default=0.5)
    parser.add_argument("--round-cost", type=float, default=0.01)
    parser.add_argument("--shaping-fade", type=float, default=0.8, help="fraction of training after which shaping is off")
    parser.add_argument("--clip", type=float, default=0.2)
    parser.add_argument("--entropy", type=float, default=0.01)
    parser.add_argument("--max-rounds", type=int, default=30)
    parser.add_argument("--eval-every", type=int, default=10)
    parser.add_argument("--eval-matches", type=int, default=100)
    parser.add_argument("--seed", type=int, default=0)
    args = parser.parse_args()

    torch.manual_seed(args.seed)
    run_dir = RUNS_DIR / time.strftime("%Y%m%d-%H%M%S")
    run_dir.mkdir(parents=True, exist_ok=True)
    log = (run_dir / "log.jsonl").open("w")

    policy = Policy()
    optimizer = torch.optim.Adam(policy.parameters(), lr=args.lr)
    reward = Reward(
        draw=args.draw_reward,
        hp_weight=args.hp_weight,
        count_weight=args.count_weight,
        round_cost=args.round_cost,
        shaping_fade=args.shaping_fade,
        gamma=args.gamma,
    )
    games = VecGames(args.envs, advance_policy, args.max_rounds, args.seed * 1_000_000, reward)
    eval_seed = 10_000_000

    baseline = evaluate(policy, advance_policy, args.eval_matches, eval_seed, args.max_rounds)
    print(f"untrained vs scripted: win {baseline[0]:.0%} loss {baseline[1]:.0%} draw {baseline[2]:.0%}", flush=True)

    updates = args.steps // (args.envs * args.rollout)
    recent = []
    start = time.perf_counter()
    for update in range(1, updates + 1):
        reward.progress = (update - 1) / updates
        buf = {k: [] for k in ("planes", "scalars", "masks", "actions", "logp", "values", "rewards", "dones", "boundaries")}
        for _ in range(args.rollout):
            planes, scalars, masks = games.observe()
            with torch.no_grad():
                logits, values = policy(planes, scalars, masks)
                dist = torch.distributions.Categorical(logits=logits)
                actions = dist.sample()
            rewards, dones, boundaries, finished = games.step(actions.tolist())
            recent.extend(finished)
            for key, value in (
                ("planes", planes), ("scalars", scalars), ("masks", masks), ("actions", actions),
                ("logp", dist.log_prob(actions)), ("values", values),
                ("rewards", torch.from_numpy(rewards)), ("dones", torch.from_numpy(dones)),
                ("boundaries", torch.from_numpy(boundaries)),
            ):
                buf[key].append(value)
        with torch.no_grad():
            _, next_values = policy(*games.observe())

        values = torch.stack(buf["values"])
        rewards = torch.stack(buf["rewards"])
        dones = torch.stack(buf["dones"])
        boundaries = torch.stack(buf["boundaries"])
        advantages = round_advantages(rewards, values, next_values, dones, boundaries, args.gamma, args.lam)
        returns = advantages + values

        flat = {
            "planes": torch.cat(buf["planes"]), "scalars": torch.cat(buf["scalars"]),
            "masks": torch.cat(buf["masks"]), "actions": torch.cat(buf["actions"]),
            "logp": torch.cat(buf["logp"]), "adv": advantages.flatten(), "ret": returns.flatten(),
        }
        flat["adv"] = (flat["adv"] - flat["adv"].mean()) / (flat["adv"].std() + 1e-8)
        size = flat["actions"].shape[0]
        for _ in range(args.epochs):
            order = torch.randperm(size)
            for begin in range(0, size, args.minibatch):
                idx = order[begin : begin + args.minibatch]
                logits, value = policy(flat["planes"][idx], flat["scalars"][idx], flat["masks"][idx])
                dist = torch.distributions.Categorical(logits=logits)
                ratio = (dist.log_prob(flat["actions"][idx]) - flat["logp"][idx]).exp()
                adv = flat["adv"][idx]
                policy_loss = -torch.min(ratio * adv, ratio.clamp(1 - args.clip, 1 + args.clip) * adv).mean()
                value_loss = (value - flat["ret"][idx]).pow(2).mean()
                loss = policy_loss + 0.5 * value_loss - args.entropy * dist.entropy().mean()
                optimizer.zero_grad()
                loss.backward()
                nn.utils.clip_grad_norm_(policy.parameters(), 0.5)
                optimizer.step()

        steps = update * args.envs * args.rollout
        elapsed = time.perf_counter() - start
        record = {"update": update, "steps": steps, "steps_per_s": steps / elapsed, "shaping": reward.shaping_scale()}
        if recent:
            record["train_return"] = float(np.mean([r for r, _ in recent]))
            record["train_win"] = float(np.mean([w == 0 for _, w in recent]))
            record["matches"] = len(recent)
        recent = []
        if update % args.eval_every == 0 or update == updates:
            win, loss_rate, draw = evaluate(policy, advance_policy, args.eval_matches, eval_seed, args.max_rounds)
            record.update(eval_win=win, eval_loss=loss_rate, eval_draw=draw)
            torch.save(policy.state_dict(), run_dir / "policy.pt")
        log.write(json.dumps(record) + "\n")
        log.flush()
        summary = f"update {update}/{updates} steps {steps:,} ({record['steps_per_s']:.0f}/s)"
        if "train_win" in record:
            summary += f" train win {record['train_win']:.0%} return {record['train_return']:+.2f}"
        if "eval_win" in record:
            summary += f" | eval vs scripted: win {record['eval_win']:.0%} loss {record['eval_loss']:.0%} draw {record['eval_draw']:.0%}"
        print(summary, flush=True)

    print(f"saved {run_dir / 'policy.pt'}")


if __name__ == "__main__":
    main()
