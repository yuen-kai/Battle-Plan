import pytest
import torch

from battlesim.maps import MapLayout
from battlesim.reward import Reward
from battlesim.sim import Match
from battlesim.units import load_roster
from train import round_advantages

ROSTER = load_roster()


def five_v_five():
    soldier = ROSTER["Soldier"]
    cells = [[(x, 0) for x in range(5)], [(x, 9) for x in range(5)]]
    return Match(MapLayout("test", frozenset()), [[soldier] * 5, [soldier] * 5], cells)


def kill(unit, hp=0.0):
    unit.hp = hp
    unit.alive = False


def test_even_board_has_zero_potential():
    assert Reward().potential(five_v_five(), 0) == pytest.approx(0.0)


def test_potential_counts_hp_share_and_units_standing():
    match = five_v_five()
    kill(match.units[5])
    assert Reward().potential(match, 0) == pytest.approx(0.5 * 0.2 + 0.5 * 0.2)
    assert Reward().potential(match, 1) == pytest.approx(-0.2)


def test_overkill_does_not_count():
    a, b = five_v_five(), five_v_five()
    kill(a.units[5], hp=0.0)
    kill(b.units[5], hp=-75.0)
    assert Reward().potential(a, 0) == pytest.approx(Reward().potential(b, 0))


def test_shaping_fades_out():
    reward = Reward(shaping_fade=0.8)
    scales = []
    for progress in (0.0, 0.4, 0.8, 1.0):
        reward.progress = progress
        scales.append(reward.shaping_scale())
    assert scales == pytest.approx([1.0, 0.5, 0.0, 0.0])


def test_draw_is_penalised_and_shaping_is_handed_back_at_the_end():
    match = five_v_five()
    match.finished, match.winner = True, None
    reward = Reward(draw=-0.5, round_cost=0.01)
    assert reward.round_reward(0.3, match, 0) == pytest.approx(-0.3 - 0.01 - 0.5)
    reward.progress = 1.0
    assert reward.round_reward(0.3, match, 0) == pytest.approx(-0.01 - 0.5)


def test_win_and_loss_outcomes():
    match = five_v_five()
    match.finished = True
    reward = Reward(shaping_fade=0.0, round_cost=0.0)
    match.winner = 0
    assert reward.round_reward(0.0, match, 0) == 1.0
    assert reward.round_reward(0.0, match, 1) == -1.0


def test_discount_applies_only_at_round_boundaries():
    rewards = torch.tensor([[0.0], [1.0], [0.0], [1.0]])
    values = torch.zeros(4, 1)
    dones = torch.tensor([[0.0], [0.0], [0.0], [1.0]])
    boundaries = torch.tensor([[False], [True], [False], [True]])
    advantages = round_advantages(rewards, values, torch.zeros(1), dones, boundaries, 0.5, 1.0)
    assert advantages.flatten().tolist() == pytest.approx([1.5, 1.5, 1.0, 1.0])
