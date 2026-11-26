import argparse
import random
import struct
from pathlib import Path

import numpy as np
import torch

from battlesim.baselines import advance_policy
from battlesim.reward import Reward
from train import Policy, Slot

ROOT = Path(__file__).resolve().parent
ACTOR = [
    "conv.0.weight", "conv.0.bias", "conv.2.weight", "conv.2.bias", "conv.4.weight", "conv.4.bias",
    "body.0.weight", "body.0.bias", "context.weight", "context.bias",
    "target_head.weight", "target_head.bias", "step_head.weight", "step_head.bias",
    "move_logits.weight", "move_logits.bias",
]
MAGIC = b"BPNN"


def half_rounded(state):
    return {key: value.half().float() if key in ACTOR else value for key, value in state.items()}


def write_weights(state, path):
    flat = np.concatenate([state[key].numpy().ravel() for key in ACTOR]).astype(np.float16)
    with open(path, "wb") as file:
        file.write(MAGIC + struct.pack("<i", flat.size))
        file.write(flat.tobytes())
    return flat.size


def collect(policy, games, seed):
    rng = random.Random(seed)
    samples = []
    for game in range(games):
        slot = Slot(seed + game, advance_policy, 30, Reward(), fog=game % 2 == 0)
        done = False
        while not done:
            planes, scalars, mask, _ = slot.observe()
            with torch.no_grad():
                logits = policy.logits(
                    torch.from_numpy(planes[None]), torch.from_numpy(scalars[None]), torch.from_numpy(mask[None])
                )[0]
            samples.append((planes, scalars, mask, logits.numpy(), slot.builder.dodge))
            _, done, _ = slot.step(int(logits.argmax()))
    return samples, rng


def write_fixture(samples, path):
    with open(path, "wb") as file:
        file.write(struct.pack("<i", len(samples)))
        for planes, scalars, mask, logits, _ in samples:
            file.write(planes.astype("<f4").tobytes())
            file.write(scalars.astype("<f4").tobytes())
            file.write(mask.astype(np.uint8).tobytes())
            file.write(logits.astype("<f4").tobytes())


def main():
    parser = argparse.ArgumentParser(description="Export the trained policy for Unity as float16 weights.")
    parser.add_argument("checkpoint", nargs="?", default=str(ROOT / "models" / "bot.pt"))
    parser.add_argument("--out", default=str(ROOT.parent / "Assets" / "Resources" / "TrainedBot.bytes"))
    parser.add_argument("--fixture", default=str(ROOT / "fixtures" / "trained_bot_logits.bin"))
    parser.add_argument("--games", type=int, default=40, help="games played to compare float16 against float32")
    args = parser.parse_args()

    state = torch.load(args.checkpoint, map_location="cpu")
    count = write_weights(state, args.out)
    print(f"wrote {count:,} weights ({count * 2 / 1e6:.1f} MB) to {args.out}")

    full, half = Policy(), Policy()
    full.load_state_dict(state)
    half.load_state_dict(half_rounded(state))
    full.eval()
    half.eval()
    samples, rng = collect(half, args.games, seed=90_000_000)
    with torch.no_grad():
        agree = sum(
            int(full.logits(torch.from_numpy(p[None]), torch.from_numpy(s[None]), torch.from_numpy(m[None]))[0].argmax())
            == int(logits.argmax())
            for p, s, m, logits, _ in samples
        )
    print(f"float16 picks the same action as float32 in {agree / len(samples):.2%} of {len(samples):,} decisions")

    planning = [s for s in samples if not s[4]]
    dodges = [s for s in samples if s[4]]
    dodges = rng.sample(dodges, min(8, len(dodges)))
    chosen = rng.sample(planning, 56) + dodges
    Path(args.fixture).parent.mkdir(parents=True, exist_ok=True)
    write_fixture(chosen, args.fixture)
    print(f"wrote {len(chosen)} decisions ({len(dodges)} dodges) to {args.fixture}")


if __name__ == "__main__":
    main()
