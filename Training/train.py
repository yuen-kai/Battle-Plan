import argparse
import json
import math
import multiprocessing as mp
import random
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

from battlesim.baselines import advance_policy
from battlesim.env import MOVE_ACTIONS, DodgeBuilder, Game, PathBuilder
from battlesim.maps import COLUMNS, ROWS, in_bounds
from battlesim.observe import (
    ACTIVE_HEAD,
    LEGACY_PLANES,
    PLANE_COUNT,
    PRIVILEGED_COUNT,
    SCALAR_COUNT,
    canonical_mask,
    canonical_planes,
    encode_decision,
    encode_privileged,
    world_action,
)
from battlesim.paths import STEPS
from battlesim.reward import Reward
from battlesim.sim import Config

RUNS_DIR = Path(__file__).resolve().parent / "runs"
CELLS = ROWS * COLUMNS


def neighbour_table():
    table = torch.zeros(CELLS, MOVE_ACTIONS, dtype=torch.long)
    for y in range(ROWS):
        for x in range(COLUMNS):
            index = y * COLUMNS + x
            table[index, 0] = index
            for move, (dx, dy) in STEPS.items():
                nx, ny = x + dx, y + dy
                table[index, move] = ny * COLUMNS + nx if in_bounds((nx, ny)) else index
    return table


class Policy(nn.Module):
    def __init__(self, width=64, hidden=256, critic_width=32, planes=PLANE_COUNT, scalars=SCALAR_COUNT):
        super().__init__()
        self.planes = planes
        self.scalars = scalars
        self.revise = planes > LEGACY_PLANES
        self.conv = nn.Sequential(
            nn.Conv2d(planes, width, 3, padding=1),
            nn.ReLU(),
            nn.Conv2d(width, width, 3, padding=1),
            nn.ReLU(),
            nn.Conv2d(width, width, 3, padding=1),
            nn.ReLU(),
        )
        self.body = nn.Sequential(nn.Linear(width * CELLS + scalars, hidden), nn.ReLU())
        self.context = nn.Linear(hidden, width)
        self.target_head = nn.Conv2d(width, 1, 1)
        self.step_head = nn.Conv2d(width, 1, 1)
        self.move_logits = nn.Linear(hidden, MOVE_ACTIONS)
        self.critic_conv = nn.Sequential(
            nn.Conv2d(planes + PRIVILEGED_COUNT, critic_width, 3, padding=1),
            nn.ReLU(),
            nn.Conv2d(critic_width, critic_width, 3, padding=1),
            nn.ReLU(),
        )
        self.critic_body = nn.Sequential(
            nn.Linear(critic_width * CELLS + scalars, hidden), nn.ReLU(), nn.Linear(hidden, 1)
        )
        self.register_buffer("neighbours", neighbour_table())

    def logits(self, planes, scalars, masks):
        board = self.conv(planes)
        features = self.body(torch.cat([board.flatten(1), scalars], dim=1))
        conditioned = torch.relu(board + self.context(features)[:, :, None, None])
        legal_targets = masks[:, MOVE_ACTIONS:].sum(dim=1).clamp(min=1).float()
        targets = self.target_head(conditioned).flatten(1) - legal_targets.log()[:, None]
        head = planes[:, ACTIVE_HEAD].flatten(1).argmax(dim=1)
        steps = self.step_head(conditioned).flatten(1).gather(1, self.neighbours[head])
        moves = self.move_logits(features) + steps
        return torch.cat([moves, targets], dim=1).masked_fill(~masks, -1e9)

    @classmethod
    def from_state(cls, state):
        width = state["conv.0.weight"].shape[0]
        planes = state["conv.0.weight"].shape[1]
        policy = cls(width=width, planes=planes, scalars=state["body.0.weight"].shape[1] - width * CELLS)
        policy.load_state_dict(state)
        return policy

    def adopt(self, state):
        state = dict(state)
        old_planes = state["conv.0.weight"].shape[1]
        grown = self.state_dict()
        for key in ("conv.0.weight", "critic_conv.0.weight"):
            weight = torch.zeros_like(grown[key])
            weight[:, :old_planes] = state[key][:, :old_planes]
            weight[:, self.planes:] = state[key][:, old_planes:]
            state[key] = weight
        for key in ("body.0.weight", "critic_body.0.weight"):
            weight = torch.zeros_like(grown[key])
            weight[:, : state[key].shape[1]] = state[key]
            state[key] = weight
        self.load_state_dict(state)

    def value(self, planes, scalars, privileged):
        board = self.critic_conv(torch.cat([planes, privileged], dim=1))
        return self.critic_body(torch.cat([board.flatten(1), scalars], dim=1)).squeeze(-1)


@dataclass
class FogSchedule:
    start: float = 0.0
    end: float = 0.7
    ramp_begin: float = 0.2
    ramp_end: float = 0.6

    def __call__(self, progress):
        if progress >= self.ramp_end:
            return self.end
        if progress <= self.ramp_begin:
            return self.start
        share = (progress - self.ramp_begin) / (self.ramp_end - self.ramp_begin)
        return self.start + share * (self.end - self.start)


class Slot:
    def __init__(self, seed, opponent, max_rounds, reward, fog, team=None, inputs=(PLANE_COUNT, SCALAR_COUNT)):
        self.reward = reward
        self.rng = random.Random(seed)
        self.team = self.rng.randrange(2) if team is None else team
        self.fog = fog
        self.inputs = inputs
        self.revise = inputs[0] > LEGACY_PLANES
        self.game = Game(config=Config(max_rounds=max_rounds, fog_of_war=fog), seed=seed)
        self.opponent = opponent(self.rng)
        self.builder = self.new_builder()
        self.opponent_dives = {}
        self.episode_return = 0.0

    def new_builder(self):
        return PathBuilder(self.game.match, self.team, revise=self.revise, rng=self.rng if self.revise else None)

    def observe(self):
        planes, scalars = encode_decision(self.builder, self.game.memory[self.team])
        privileged = encode_privileged(self.game.match, self.team)
        mask = np.array(self.builder.mask(), dtype=bool)
        return (
            canonical_planes(planes[: self.inputs[0]], self.team),
            scalars[: self.inputs[1]],
            canonical_mask(mask, self.team),
            canonical_planes(privileged, self.team),
        )

    def outcome(self):
        winner = self.game.match.winner
        return "draw" if winner is None else "win" if winner == self.team else "loss"

    def step(self, action):
        self.builder.act(world_action(action, self.team))
        if not self.builder.done:
            return 0.0, False, False
        match = self.game.match
        enemy = 1 - self.team
        if self.builder.dodge:
            dives = self.builder.dives()
        else:
            plans = self.builder.plans()
            plans.update(self.game.plan(enemy, self.opponent))
            alerted = match.begin_round(plans)
            self.opponent_dives = self.game.dodge(enemy, alerted[enemy], self.opponent) if enemy in alerted else {}
            if self.team in alerted:
                self.builder = DodgeBuilder(match, self.team, alerted[self.team])
                return 0.0, False, False
            dives = {}
        match.apply_dodges({**dives, **self.opponent_dives})
        before = self.reward.potential(match, self.team)
        self.game.finish_round()
        reward = self.reward.round_reward(before, match, self.team)
        if match.finished:
            return reward, True, True
        self.builder = self.new_builder()
        return reward, False, True


POLICY_CACHE = {}


def cached_policy(path):
    if path not in POLICY_CACHE:
        policy = Policy.from_state(torch.load(path, map_location="cpu"))
        policy.eval()
        POLICY_CACHE[path] = policy
    return POLICY_CACHE[path]


def keep_cached(paths):
    for path in set(POLICY_CACHE) - set(paths):
        del POLICY_CACHE[path]


@dataclass(frozen=True)
class NetOpponent:
    path: str
    greedy: bool = False

    @property
    def name(self):
        return self.path

    def __call__(self, rng):
        policy = cached_policy(self.path)

        def choose(builder, memory):
            team = builder.team
            planes, scalars = encode_decision(builder, memory)
            mask = canonical_mask(np.array(builder.mask(), dtype=bool), team)
            with torch.no_grad():
                logits = policy.logits(
                    torch.from_numpy(np.ascontiguousarray(canonical_planes(planes[: policy.planes], team))[None]),
                    torch.from_numpy(scalars[None, : policy.scalars]),
                    torch.from_numpy(mask[None]),
                )[0]
            if self.greedy:
                action = int(logits.argmax())
            else:
                action = rng.choices(range(len(logits)), weights=torch.softmax(logits, 0).tolist())[0]
            return world_action(action, team)

        choose.revise = policy.revise
        return choose


@dataclass(frozen=True)
class ScriptedOpponent:
    name: str
    explore_bias: float

    def __call__(self, rng):
        return advance_policy(rng, explore_bias=self.explore_bias)


SCRIPTED = (ScriptedOpponent("scripted", 0.3), ScriptedOpponent("teacher", 0.0))


def pool_opponents(pool, latest_share, anchors=(), anchor_share=0.0, scripted_share=0.0):
    fixed = [(NetOpponent(path), anchor_share / len(anchors)) for path in anchors]
    fixed += [(bot, scripted_share / len(SCRIPTED)) for bot in SCRIPTED if scripted_share]
    own = 1.0 - sum(weight for _, weight in fixed)
    if len(pool) == 1:
        return [(NetOpponent(pool[0]), own)] + fixed
    older = own * (1 - latest_share) / (len(pool) - 1)
    return [(NetOpponent(path), older) for path in pool[:-1]] + [(NetOpponent(pool[-1]), own * latest_share)] + fixed


def stack(observations):
    return tuple(torch.from_numpy(np.stack(part)) for part in zip(*observations))


class VecGames:
    def __init__(self, count, opponents, max_rounds, seed, reward, fog_share):
        self.reward = reward
        self.opponents = opponents
        self.max_rounds = max_rounds
        self.fog_share = fog_share
        self.rng = random.Random(seed)
        self.next_seed = seed
        self.slots = [self._new_slot() for _ in range(count)]

    def _new_slot(self):
        self.next_seed += 1
        fog = self.rng.random() < self.fog_share
        factories, weights = zip(*self.opponents)
        opponent = self.rng.choices(factories, weights=weights)[0]
        slot = Slot(self.next_seed, opponent, self.max_rounds, self.reward, fog)
        slot.opponent_name = opponent.name
        return slot

    def set_opponents(self, opponents):
        self.opponents = opponents
        keep_cached(factory.path for factory, _ in opponents if isinstance(factory, NetOpponent))

    def set_progress(self, progress, fog_share):
        self.reward.progress = progress
        self.fog_share = fog_share

    def observe_arrays(self):
        return tuple(np.stack(part) for part in zip(*(slot.observe() for slot in self.slots)))

    def observe(self):
        return tuple(torch.from_numpy(part) for part in self.observe_arrays())

    def step(self, actions):
        rewards, dones, boundaries, finished = [], [], [], []
        for i, (slot, action) in enumerate(zip(self.slots, actions)):
            reward, done, boundary = slot.step(int(action))
            slot.episode_return += reward
            rewards.append(reward)
            dones.append(done)
            boundaries.append(boundary)
            if done:
                finished.append((slot.episode_return, slot.outcome(), slot.opponent_name))
                self.slots[i] = self._new_slot()
        return (
            np.array(rewards, dtype=np.float32),
            np.array(dones, dtype=np.float32),
            np.array(boundaries, dtype=bool),
            finished,
        )


def game_worker(connection, settings):
    torch.set_num_threads(1)
    games = VecGames(*settings)
    while True:
        command, data = connection.recv()
        if command == "step":
            connection.send(games.step(data) + (games.observe_arrays(),))
        elif command == "observe":
            connection.send(games.observe_arrays())
        elif command == "progress":
            games.set_progress(*data)
        elif command == "opponents":
            games.set_opponents(data)
        else:
            return


class ParallelGames:
    def __init__(self, workers, count, opponents, max_rounds, seed, reward, fog_share):
        context = mp.get_context("spawn")
        self.sizes = [count // workers + (k < count % workers) for k in range(workers)]
        self.connections = []
        for k, size in enumerate(self.sizes):
            parent, child = context.Pipe()
            settings = (size, opponents, max_rounds, seed + k * 100_000, reward, fog_share)
            context.Process(target=game_worker, args=(child, settings), daemon=True).start()
            self.connections.append(parent)
        for connection in self.connections:
            connection.send(("observe", None))
        self.latest = [connection.recv() for connection in self.connections]

    def set_progress(self, progress, fog_share):
        for connection in self.connections:
            connection.send(("progress", (progress, fog_share)))

    def set_opponents(self, opponents):
        for connection in self.connections:
            connection.send(("opponents", opponents))

    def observe(self):
        return tuple(torch.from_numpy(np.concatenate(parts)) for parts in zip(*self.latest))

    def step(self, actions):
        offset = 0
        for connection, size in zip(self.connections, self.sizes):
            connection.send(("step", actions[offset : offset + size]))
            offset += size
        results = [connection.recv() for connection in self.connections]
        self.latest = [result[4] for result in results]
        rewards, dones, boundaries = (np.concatenate([result[k] for result in results]) for k in range(3))
        return rewards, dones, boundaries, [match for result in results for match in result[3]]

    def close(self):
        for connection in self.connections:
            connection.send(("close", None))


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


def cpu_state(policy):
    return {key: value.detach().cpu() for key, value in policy.state_dict().items()}


def evaluate(policy, opponent, matches, seed, max_rounds, fog):
    device = next(policy.parameters()).device
    counts = {"win": 0, "loss": 0, "draw": 0}
    inputs = (policy.planes, policy.scalars)
    games = [Slot(seed + i // 2, opponent, max_rounds, Reward(), fog, team=i % 2, inputs=inputs) for i in range(matches)]
    live = list(range(matches))
    with torch.no_grad():
        while live:
            planes, scalars, masks, _ = (part.to(device) for part in stack(games[i].observe() for i in live))
            actions = policy.logits(planes, scalars, masks).argmax(dim=1).tolist()
            still = []
            for i, action in zip(live, actions):
                _, done, _ = games[i].step(action)
                if done:
                    counts[games[i].outcome()] += 1
                else:
                    still.append(i)
            live = still
    return {key: value / matches for key, value in counts.items()}


def eval_score(results):
    return sum(r["win"] - r["loss"] for r in results.values()) / len(results)


def main():
    parser = argparse.ArgumentParser(description="Self-play PPO on the Battle Plan simulator.")
    parser.add_argument("--steps", type=int, default=4_000_000)
    parser.add_argument("--envs", type=int, default=128)
    parser.add_argument("--rollout", type=int, default=64)
    parser.add_argument("--epochs", type=int, default=4)
    parser.add_argument("--minibatch", type=int, default=1024)
    parser.add_argument("--lr", type=float, default=3e-4)
    parser.add_argument("--lr-end", type=float, default=None, help="learning rate at the end of training, reached linearly; constant if unset")
    parser.add_argument("--gamma", type=float, default=0.98, help="discount per round")
    parser.add_argument("--lam", type=float, default=0.95, help="GAE lambda per round")
    parser.add_argument("--draw-reward", type=float, default=-0.5)
    parser.add_argument("--hp-weight", type=float, default=0.5)
    parser.add_argument("--count-weight", type=float, default=0.5)
    parser.add_argument("--support-weight", type=float, default=0.0,
                        help="fading potential for how many of your units can shoot each enemy, minus the reverse")
    parser.add_argument("--combo-weight", type=float, default=0.0,
                        help="fading bonus for enemies hit by two teammates' abilities, or hit by one after diving")
    parser.add_argument("--round-cost", type=float, default=0.01)
    parser.add_argument("--shaping-fade", type=float, default=0.8, help="fraction of training after which shaping is off")
    parser.add_argument("--fog-start", type=float, default=0.0, help="share of fog-of-war games at the start")
    parser.add_argument("--fog-end", type=float, default=0.6, help="share of fog-of-war games after the ramp")
    parser.add_argument("--fog-ramp-begin", type=float, default=0.2)
    parser.add_argument("--fog-ramp-end", type=float, default=0.6)
    parser.add_argument("--snapshot-every", type=int, default=10, help="updates between copies of the learner added to the opponent pool")
    parser.add_argument("--pool-size", type=int, default=20)
    parser.add_argument("--latest-share", type=float, default=0.5, help="share of games against the newest copy; older copies split the rest")
    parser.add_argument("--anchors", nargs="*", default=[], help="checkpoints that stay in the opponent pool for the whole run, such as past champions")
    parser.add_argument("--anchor-share", type=float, default=0.2, help="share of games against the anchors")
    parser.add_argument("--scripted-share", type=float, default=0.0, help="share of games against the scripted bots, "
                        "so the learner keeps beating a style unlike its own")
    parser.add_argument("--clip", type=float, default=0.2)
    parser.add_argument("--entropy", type=float, default=0.005)
    parser.add_argument("--critic-warmup", type=int, default=0, help="updates at the start that train only the value network")
    parser.add_argument("--max-rounds", type=int, default=30)
    parser.add_argument("--eval-every", type=int, default=20)
    parser.add_argument("--eval-matches", type=int, default=100)
    parser.add_argument("--benchmark", nargs="*", default=[], help="checkpoints to evaluate against alongside the scripted bot")
    parser.add_argument("--init", type=str, default=None, help="policy weights to start from")
    parser.add_argument("--threads", type=int, default=0)
    parser.add_argument("--workers", type=int, default=7, help="simulator processes; 0 runs games in this process")
    parser.add_argument("--device", type=str, default="mps" if torch.backends.mps.is_available() else "cpu")
    parser.add_argument("--name", type=str, default="")
    parser.add_argument("--seed", type=int, default=0)
    args = parser.parse_args()

    torch.manual_seed(args.seed)
    if args.threads:
        torch.set_num_threads(args.threads)
    run_dir = RUNS_DIR / (time.strftime("%Y%m%d-%H%M%S") + (f"-{args.name}" if args.name else ""))
    (run_dir / "pool").mkdir(parents=True, exist_ok=True)
    (run_dir / "config.json").write_text(json.dumps(vars(args), indent=2))
    log = (run_dir / "log.jsonl").open("w")

    device = torch.device(args.device)
    policy = Policy()
    if args.init:
        policy.adopt(torch.load(args.init, map_location="cpu"))
    policy.to(device)
    optimizer = torch.optim.Adam(policy.parameters(), lr=args.lr)
    reward = Reward(
        draw=args.draw_reward,
        hp_weight=args.hp_weight,
        count_weight=args.count_weight,
        support_weight=args.support_weight,
        combo_weight=args.combo_weight,
        round_cost=args.round_cost,
        shaping_fade=args.shaping_fade,
        gamma=args.gamma,
    )
    fog_schedule = FogSchedule(args.fog_start, args.fog_end, args.fog_ramp_begin, args.fog_ramp_end)
    anchors = set(args.anchors)
    scripted = {bot.name for bot in SCRIPTED}
    pool = [str(run_dir / "pool" / "u0.pt")]
    torch.save(cpu_state(policy), pool[0])

    def opponents():
        return pool_opponents(pool, args.latest_share, args.anchors, args.anchor_share, args.scripted_share)

    def group(name):
        return "anchors" if name in anchors else "scripted" if name in scripted else "pool"

    settings = (args.envs, opponents(), args.max_rounds, args.seed * 1_000_000, reward, fog_schedule(0.0))
    games = ParallelGames(args.workers, *settings) if args.workers else VecGames(*settings)
    eval_seed = 10_000_000
    benchmarks = {"scripted": advance_policy, **{Path(path).stem: NetOpponent(path, greedy=True) for path in args.benchmark}}

    def run_eval():
        return {
            f"{name} {mode}": evaluate(policy, opponent, args.eval_matches, eval_seed, args.max_rounds, fog)
            for name, opponent in benchmarks.items()
            for mode, fog in (("fog", True), ("clear", False))
        }

    def describe(results):
        return "  ".join(f"{key} {r['win']:.0%}/{r['loss']:.0%}" for key, r in results.items())

    print(f"untrained (win/loss): {describe(run_eval())}", flush=True)

    batch = args.envs * args.rollout
    updates = args.steps // batch
    best = -math.inf
    recent = []
    start = time.perf_counter()
    for update in range(1, updates + 1):
        progress = (update - 1) / updates
        fog_share = fog_schedule(progress)
        reward.progress = progress
        lr = args.lr if args.lr_end is None else args.lr + (args.lr_end - args.lr) * progress
        for param_group in optimizer.param_groups:
            param_group["lr"] = lr
        games.set_progress(progress, fog_share)
        buf = {k: [] for k in ("planes", "scalars", "masks", "privileged", "actions", "logp", "values", "rewards", "dones", "boundaries")}
        entropy_share = []
        rollout_start = time.perf_counter()
        for _ in range(args.rollout):
            planes, scalars, masks, privileged = (part.to(device) for part in games.observe())
            with torch.no_grad():
                logits = policy.logits(planes, scalars, masks)
                values = policy.value(planes, scalars, privileged)
                dist = torch.distributions.Categorical(logits=logits)
                actions = dist.sample()
                legal = masks.sum(dim=1)
                choice = legal > 1
                if choice.any():
                    entropy_share.append((dist.entropy()[choice] / legal[choice].float().log()).mean().item())
            rewards, dones, boundaries, finished = games.step(actions.tolist())
            recent.extend(finished)
            for key, value in (
                ("planes", planes), ("scalars", scalars), ("masks", masks), ("privileged", privileged),
                ("actions", actions), ("logp", dist.log_prob(actions)), ("values", values),
                ("rewards", torch.from_numpy(rewards)), ("dones", torch.from_numpy(dones)),
                ("boundaries", torch.from_numpy(boundaries)),
            ):
                buf[key].append(value)
        with torch.no_grad():
            planes, scalars, _, privileged = (part.to(device) for part in games.observe())
            next_values = policy.value(planes, scalars, privileged).cpu()
        update_start = time.perf_counter()

        values = torch.stack(buf["values"]).cpu()
        rewards = torch.stack(buf["rewards"])
        dones = torch.stack(buf["dones"])
        boundaries = torch.stack(buf["boundaries"])
        advantages = round_advantages(rewards, values, next_values, dones, boundaries, args.gamma, args.lam)
        returns = advantages + values
        explained = 1 - (returns - values).var().item() / max(returns.var().item(), 1e-8)

        flat = {key: torch.cat(buf[key]) for key in ("planes", "scalars", "masks", "privileged", "actions", "logp")}
        flat["adv"] = advantages.flatten()
        flat["ret"] = returns.flatten()
        flat = {key: value.to(device) for key, value in flat.items()}
        flat["adv"] = (flat["adv"] - flat["adv"].mean()) / (flat["adv"].std() + 1e-8)
        size = flat["actions"].shape[0]
        kls, clips = [], []
        for _ in range(args.epochs):
            order = torch.randperm(size, device=device)
            for begin in range(0, size, args.minibatch):
                idx = order[begin : begin + args.minibatch]
                logits = policy.logits(flat["planes"][idx], flat["scalars"][idx], flat["masks"][idx])
                value = policy.value(flat["planes"][idx], flat["scalars"][idx], flat["privileged"][idx])
                value_loss = (value - flat["ret"][idx]).pow(2).mean()
                dist = torch.distributions.Categorical(logits=logits)
                log_ratio = dist.log_prob(flat["actions"][idx]) - flat["logp"][idx]
                ratio = log_ratio.exp()
                adv = flat["adv"][idx]
                policy_loss = -torch.min(ratio * adv, ratio.clamp(1 - args.clip, 1 + args.clip) * adv).mean()
                loss = 0.5 * value_loss
                if update > args.critic_warmup:
                    loss = loss + policy_loss - args.entropy * dist.entropy().mean()
                optimizer.zero_grad()
                loss.backward()
                nn.utils.clip_grad_norm_(policy.parameters(), 0.5)
                optimizer.step()
                with torch.no_grad():
                    kls.append(((ratio - 1) - log_ratio).mean().item())
                    clips.append(((ratio - 1).abs() > args.clip).float().mean().item())
        if update % args.snapshot_every == 0:
            snapshot = run_dir / "pool" / f"u{update}.pt"
            torch.save(cpu_state(policy), snapshot)
            pool.append(str(snapshot))
            for retired in pool[: -args.pool_size]:
                if int(Path(retired).stem[1:]) % 100:
                    Path(retired).unlink()
            del pool[: -args.pool_size]
            games.set_opponents(opponents())

        steps = update * batch
        record = {
            "update": update,
            "steps": steps,
            "steps_per_s": steps / (time.perf_counter() - start),
            "rollout_s": update_start - rollout_start,
            "update_s": time.perf_counter() - update_start,
            "shaping": reward.shaping_scale(),
            "lr": lr,
            "fog_share": fog_share,
            "entropy": float(np.mean(entropy_share)) if entropy_share else None,
            "explained_variance": explained,
            "approx_kl": float(np.mean(kls)),
            "clip_frac": float(np.mean(clips)),
        }
        summary = (
            f"update {update}/{updates} steps {steps:,} ({record['steps_per_s']:.0f}/s) "
            f"fog {fog_share:.0%} ent {record['entropy']:.2f} ev {explained:.2f} kl {record['approx_kl']:.3f}"
        )
        if recent:
            record.update(train_return=float(np.mean([r for r, *_ in recent])), matches=len(recent))
            summary += f" return {record['train_return']:+.2f}"
            for kind in ("pool", "anchors", "scripted"):
                outcomes = [o for _, o, name in recent if group(name) == kind]
                if outcomes:
                    win, loss = outcomes.count("win") / len(outcomes), outcomes.count("loss") / len(outcomes)
                    record.update({f"train_{kind}_win": win, f"train_{kind}_loss": loss})
                    summary += f" vs {kind} {win:.0%}/{loss:.0%}"
        recent = []
        if update % args.eval_every == 0 or update == updates:
            results = run_eval()
            for key, r in results.items():
                record.update({f"eval_{key.replace(' ', '_')}_{outcome}": value for outcome, value in r.items()})
            score = eval_score(results)
            record["eval_score"] = score
            torch.save(cpu_state(policy), run_dir / "policy.pt")
            if score > best:
                best = score
                torch.save(cpu_state(policy), run_dir / "best.pt")
            summary += f" | eval {score:+.2f} {describe(results)}"
        log.write(json.dumps(record) + "\n")
        log.flush()
        print(summary, flush=True)

    if args.workers:
        games.close()
    print(f"saved {run_dir / 'policy.pt'} (best eval score {best:+.2f} in {run_dir / 'best.pt'})")


if __name__ == "__main__":
    main()
