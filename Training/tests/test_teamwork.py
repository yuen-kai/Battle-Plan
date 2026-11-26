from pathlib import Path

import numpy as np
import torch

from battlesim.env import MOVE_ACTIONS, Game, PathBuilder, target_action
from battlesim.observe import (
    ALLY_ABILITY,
    ESCAPE,
    LEGACY_PLANES,
    LEGACY_SCALARS,
    PLANE_COUNT,
    REVISING,
    SCALAR_COUNT,
    TEAM_PATH,
    TYPE_INDEX,
    encode_decision,
)
from battlesim.paths import RIGHT, STAY, UP
from battlesim.reward import Reward, fire_superiority
from battlesim.sim import Config
from train import NetOpponent, Policy, Slot

MODEL = Path(__file__).resolve().parents[1] / "models" / "bot.pt"


def test_a_legacy_policy_grown_into_the_new_inputs_scores_moves_exactly_as_before():
    torch.manual_seed(0)
    legacy = Policy(planes=LEGACY_PLANES, scalars=LEGACY_SCALARS)
    grown = Policy()
    grown.adopt(legacy.state_dict())
    planes = torch.rand(4, PLANE_COUNT, 10, 15)
    planes[:, LEGACY_PLANES:] = 0
    planes[:, 8] = 0
    planes[:, 8, 3, 4] = 1
    scalars = torch.rand(4, SCALAR_COUNT)
    scalars[:, REVISING] = 0
    mask = torch.rand(4, MOVE_ACTIONS + 150) > 0.3
    mask[:, 0] = True
    with torch.no_grad():
        before = legacy.logits(planes[:, :LEGACY_PLANES], scalars[:, :LEGACY_SCALARS], mask)
        after = grown.logits(planes, scalars, mask)
    assert torch.allclose(before, after, atol=1e-5)


def test_revision_pass_replans_every_unit_seeing_the_whole_team():
    game = Game(map_name="Concourse", config=Config(fog_of_war=False), seed=4)
    builder = PathBuilder(game.match, 0, revise=True)
    units = list(builder.units)
    for _ in units:
        builder.act(STAY)
    assert builder.revising and builder.index == 0 and not builder.done
    first = builder.unit
    assert {u.uid for u, _, _ in builder.teammate_plans()} == {u.uid for u in units if u is not first}
    assert builder.claimed == {builder.end_cell(o) for o in units if o is not first}
    planes, scalars = encode_decision(builder, game.memory[0])
    assert scalars[REVISING] == 1.0
    builder.act(UP if builder.mask()[UP] else RIGHT)
    while not builder.done:
        builder.act(STAY)
    assert len(builder.plans()[first.uid]) > 1


def test_teammate_routes_show_only_planned_units_in_the_first_pass():
    game = Game(map_name="Concourse", config=Config(fog_of_war=False), seed=4)
    builder = PathBuilder(game.match, 0, revise=True)
    planes, _ = encode_decision(builder, game.memory[0])
    assert planes[TEAM_PATH].sum() == 0
    mover = builder.unit
    while builder.unit is mover:
        legal = [a for a in range(1, MOVE_ACTIONS) if builder.mask()[a]]
        builder.act(legal[0] if legal else STAY)
    planes, _ = encode_decision(builder, game.memory[0])
    route = builder.paths[mover.uid]
    assert all(planes[TEAM_PATH, y, x] == 1.0 for x, y in route[1:])


def test_a_teammates_planned_grenade_shows_its_area_and_where_caught_enemies_can_dive():
    game = Game(map_name="Concourse", rosters=[["Soldier"] * 5, ["Sniper"] * 5], config=Config(fog_of_war=False), seed=2)
    match = game.match
    soldier = match.living(0)[0]
    builder = PathBuilder(match, 0)
    assert builder.unit is soldier
    target = min(builder.ability_targets(soldier), key=lambda c: (abs(c[0] - soldier.cell[0]) + abs(c[1] - soldier.cell[1]) < 2, -c[1]))
    enemy = match.living(1)[0]
    enemy.x, enemy.y = target
    match._positions_version += 1
    match.record_sightings()
    game.memory[0].update(match, 0)
    builder.act(target_action(target))
    planes, _ = encode_decision(builder, game.memory[0])
    assert planes[ALLY_ABILITY + TYPE_INDEX["Soldier"], target[1], target[0]] == 1.0
    escape = planes[ESCAPE]
    assert escape[target[1], target[0]] == 1.0
    assert escape.sum() > 1


def test_fire_superiority_rewards_outgunning_the_other_side():
    game = Game(map_name="Concourse", config=Config(fog_of_war=False), seed=3)
    match = game.match
    assert abs(fire_superiority(match, 0) + fire_superiority(match, 1)) < 1e-9
    shooters = match.living(0)[:2]
    victim = match.living(1)[0]
    for offset, shooter in zip((-1, 1), shooters):
        shooter.x, shooter.y = victim.x + offset, victim.y - 2
    match._positions_version += 1
    assert fire_superiority(match, 0) > fire_superiority(match, 1)


def test_combos_count_layered_abilities_and_hits_through_a_dive():
    game = Game(map_name="Concourse", config=Config(fog_of_war=False), seed=3)
    match = game.match
    a, b = match.living(0)[:2]
    first, second = match.living(1)[:2]
    match.note_ability_hit(first, a, damaged=True)
    match.note_ability_hit(first, b, damaged=False)
    match.note_ability_hit(second, a, damaged=True)
    match.round_dives = {second.uid}
    assert match.combos(0) == 2 and match.combos(1) == 0
    reward = Reward(combo_weight=0.5, hp_weight=0, count_weight=0, round_cost=0)
    assert reward.round_reward(0.0, match, 0) > 0 > reward.round_reward(0.0, match, 1)


def test_new_learner_plays_against_a_legacy_checkpoint():
    slot = Slot(11, NetOpponent(str(MODEL)), 4, Reward(), fog=True, team=0)
    planes, scalars, mask, _ = slot.observe()
    assert planes.shape[0] == PLANE_COUNT and scalars.shape[0] == SCALAR_COUNT
    rng = np.random.default_rng(0)
    done, steps = False, 0
    while not done and steps < 2000:
        _, _, mask, _ = slot.observe()
        _, done, _ = slot.step(int(rng.choice(np.flatnonzero(mask))))
        steps += 1
    assert done


def test_legacy_inputs_are_a_prefix_of_the_new_ones():
    slot = Slot(11, NetOpponent(str(MODEL)), 4, Reward(), fog=True, team=0, inputs=(LEGACY_PLANES, LEGACY_SCALARS))
    planes, scalars, _, _ = slot.observe()
    assert planes.shape[0] == LEGACY_PLANES and scalars.shape[0] == LEGACY_SCALARS
    assert not slot.builder.revise
