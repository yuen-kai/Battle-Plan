import random

import numpy as np
import pytest
import torch

from battlesim.baselines import advance_policy
from battlesim.env import MOVE_ACTIONS, Game
from battlesim.maps import COLUMNS, ROWS
from battlesim.observe import ACTIVE_HEAD, canonical_mask, canonical_planes, world_action
from battlesim.paths import DOWN, LEFT, RIGHT, STAY, UP
from battlesim.reward import Reward
from battlesim.sim import ALL_CELLS, Config
from train import FogSchedule, Policy, Slot, neighbour_table, pool_opponents


def test_fog_off_reveals_every_enemy_but_smoke_still_blocks_shots():
    game = Game(map_name="Concourse", config=Config(fog_of_war=False), seed=3)
    match = game.match
    assert match.visible_cells(0) == ALL_CELLS
    assert all(match.can_observe(0, enemy) for enemy in match.living(1))
    assert len(game.memory[0].last_known) == 5
    shooter, target = match.living(0)[0], match.living(1)[0]
    target.x, target.y = shooter.x, shooter.y + 2
    match._positions_version += 1
    assert match.line_of_sight(shooter, target)
    match.smoke = frozenset({(shooter.cell[0], shooter.cell[1] + 1)})
    assert not match.line_of_sight(shooter, target)


def test_fog_on_hides_distant_enemies_at_spawn():
    game = Game(map_name="Concourse", config=Config(fog_of_war=True), seed=3)
    assert not any(game.match.can_observe(0, enemy) for enemy in game.match.living(1))
    assert game.memory[0].last_known == {}


def test_team_one_view_is_the_rotated_board_and_actions_map_back():
    slot = Slot(5, advance_policy, 30, Reward(), fog=True, team=1)
    planes, _, mask, _ = slot.observe()
    head = slot.builder.paths[slot.builder.unit.uid][-1]
    assert planes[ACTIVE_HEAD, ROWS - 1 - head[1], COLUMNS - 1 - head[0]] == 1.0
    world_mask = np.array(slot.builder.mask(), dtype=bool)
    assert mask.sum() == world_mask.sum()
    for action in np.flatnonzero(mask):
        assert world_mask[world_action(int(action), 1)]
    assert world_action(UP, 1) == DOWN and world_action(LEFT, 1) == RIGHT and world_action(STAY, 1) == STAY
    assert world_action(MOVE_ACTIONS, 1) == MOVE_ACTIONS + ROWS * COLUMNS - 1


def test_canonical_view_is_identity_for_team_zero():
    planes = np.random.rand(3, ROWS, COLUMNS).astype(np.float32)
    mask = np.random.rand(MOVE_ACTIONS + ROWS * COLUMNS) > 0.5
    assert canonical_planes(planes, 0) is planes
    assert canonical_mask(mask, 0) is mask
    assert all(world_action(a, 0) == a for a in range(len(mask)))


def test_neighbour_table_steps_and_clamps_at_edges():
    table = neighbour_table()
    index = 4 * COLUMNS + 6
    assert table[index].tolist() == [index, 5 * COLUMNS + 6, 4 * COLUMNS + 7, 3 * COLUMNS + 6, 4 * COLUMNS + 5]
    assert table[0, DOWN] == 0 and table[0, LEFT] == 0


def test_untrained_policy_casts_about_as_often_as_it_takes_one_move():
    slot = Slot(11, advance_policy, 30, Reward(), fog=False, team=0)
    while not slot.builder.mask()[MOVE_ACTIONS:].count(True):
        slot = Slot(slot.rng.randrange(1 << 20), advance_policy, 30, Reward(), fog=False, team=0)
    policy = Policy()
    for layer in (policy.target_head, policy.step_head, policy.move_logits):
        torch.nn.init.zeros_(layer.weight)
        torch.nn.init.zeros_(layer.bias)
    planes, scalars, mask, _ = (torch.from_numpy(np.stack([x])) for x in slot.observe())
    probs = policy.logits(planes, scalars, mask).softmax(dim=1)[0]
    legal_moves = int(mask[0, :MOVE_ACTIONS].sum())
    assert probs[MOVE_ACTIONS:].sum().item() == pytest.approx(1 / (legal_moves + 1), rel=1e-4)


def test_slot_plays_full_matches_on_either_side_with_and_without_fog():
    rng = random.Random(0)
    for team in (0, 1):
        for fog in (True, False):
            slot = Slot(team * 10 + fog, advance_policy, 8, Reward(), fog=fog, team=team)
            done = False
            while not done:
                legal = np.flatnonzero(slot.observe()[2])
                _, done, _ = slot.step(int(rng.choice(legal)))
            assert slot.outcome() in ("win", "loss", "draw")


def test_fog_schedule_ramps_between_shares():
    schedule = FogSchedule(0.0, 0.7, 0.2, 0.6)
    assert schedule(0.0) == 0.0
    assert schedule(0.2) == 0.0
    assert schedule(0.4) == pytest.approx(0.35)
    assert schedule(0.9) == 0.7
    assert FogSchedule(0.7, 0.7, 0.2, 0.6)(0.0) == 0.7


def test_newest_copy_gets_its_share_and_older_copies_split_the_rest():
    assert [(o.path, w) for o, w in pool_opponents(["a"], 0.5)] == [("a", 1.0)]
    weights = {o.path: w for o, w in pool_opponents(["a", "b", "c"], 0.5)}
    assert weights == {"a": 0.25, "b": 0.25, "c": 0.5}
    weights = {o.path: w for o, w in pool_opponents(["a", "b", "c"], 0.5, ["x", "y"], 0.2)}
    assert weights == pytest.approx({"a": 0.2, "b": 0.2, "c": 0.4, "x": 0.1, "y": 0.1})


def test_gate_needs_a_clear_win_over_the_champion_and_no_regression_elsewhere():
    from evaluate import gate

    def counts(win, loss, draw):
        return {mode: {"win": win, "loss": loss, "draw": draw} for mode in ("fog", "clear")}

    champion = {"scripted": counts(110, 50, 40), "old.pt": counts(90, 70, 40)}
    steady = {"champ.pt": counts(120, 60, 20), "scripted": counts(112, 48, 40), "old.pt": counts(95, 65, 40)}
    circular = {"champ.pt": counts(120, 60, 20), "scripted": counts(112, 48, 40), "old.pt": counts(50, 120, 30)}
    narrow = {"champ.pt": counts(85, 80, 35), "scripted": counts(112, 48, 40), "old.pt": counts(95, 65, 40)}
    assert gate(steady, champion, "champ.pt")[0]
    assert not gate(circular, champion, "champ.pt")[0]
    assert not gate(narrow, champion, "champ.pt")[0]


def test_net_opponent_plays_legal_moves_from_either_side(tmp_path):
    from train import NetOpponent

    path = tmp_path / "policy.pt"
    torch.save(Policy().state_dict(), path)
    for team in (0, 1):
        slot = Slot(31 + team, NetOpponent(str(path)), 6, Reward(), fog=True, team=team)
        rng = random.Random(team)
        done = False
        while not done:
            legal = np.flatnonzero(slot.observe()[2])
            _, done, _ = slot.step(int(rng.choice(legal)))
        assert slot.outcome() in ("win", "loss", "draw")
