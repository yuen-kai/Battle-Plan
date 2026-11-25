import math

import pytest

from battlesim.maps import MapLayout
from battlesim.sim import Config, Match
from battlesim.units import load_roster

ROSTER = load_roster()


def duel(red, blue, walls=(), red_cell=(7, 3), blue_cell=(7, 6), **config):
    layout = MapLayout("test", frozenset(walls))
    return Match(layout, [[ROSTER[red]], [ROSTER[blue]]], [[red_cell], [blue_cell]], Config(**config))


def hp(match):
    return [round(u.hp, 3) for u in match.units]


def test_standing_soldiers_trade_exactly_one_magazine():
    match = duel("Soldier", "Soldier")
    match.play_round({})
    assert hp(match) == [70.0, 70.0]


def test_out_of_target_range_deals_nothing():
    match = duel("Soldier", "Soldier", red_cell=(7, 1), blue_cell=(7, 6))
    match.play_round({})
    assert hp(match) == [120.0, 120.0]


def test_wall_between_blocks_fire():
    match = duel("Soldier", "Soldier", walls=[(7, 4)])
    match.play_round({})
    assert hp(match) == [120.0, 120.0]


def test_shooter_keeps_firing_while_enemy_walks_in():
    match = duel("Soldier", "Commander", red_cell=(7, 2), blue_cell=(7, 9))
    match.play_round({1: [(7, 9), (7, 8), (7, 7), (7, 6)]})
    commander_taken = 120.0 - match.units[1].hp
    assert commander_taken > 0
    assert match.units[0].hp < 120.0


def test_crossing_target_is_hard_to_hit_with_slow_bullets():
    straight = duel("Soldier", "Sniper", red_cell=(7, 2), blue_cell=(7, 6))
    straight.play_round({1: [(7, 6), (7, 7), (7, 8), (7, 9)]})
    crossing = duel("Soldier", "Sniper", red_cell=(7, 2), blue_cell=(7, 6))
    crossing.play_round({1: [(7, 6), (8, 6), (9, 6), (10, 6)]})
    assert straight.damage_dealt[0] > crossing.damage_dealt[0]


def test_sniper_locks_two_seconds_then_hits_for_fifty():
    match = duel("Sniper", "Ramrod", red_cell=(7, 2), blue_cell=(7, 6))
    duration = match.play_round({})
    assert match.units[1].hp == pytest.approx(110.0)
    assert duration >= 2.0


def test_pogo_backstab_doubles_damage():
    behind = duel("PogoRider", "Soldier", red_cell=(7, 1), blue_cell=(7, 7))
    behind.units[1].facing = math.pi / 2
    behind.play_round({})
    front = duel("PogoRider", "Soldier", red_cell=(7, 1), blue_cell=(7, 7))
    front.units[1].facing = -math.pi / 2
    front.play_round({})
    assert 120 - behind.units[1].hp == pytest.approx(2 * (120 - front.units[1].hp))


def test_allies_cannot_share_an_end_cell():
    layout = MapLayout("test", frozenset())
    soldier = ROSTER["Soldier"]
    match = Match(layout, [[soldier, soldier], [soldier]], [[(2, 0), (4, 0)], [(14, 9)]])
    paths = {0: [(2, 0), (3, 0)], 1: [(4, 0), (3, 0)], 2: [(14, 9)]}
    match.separate_friendly_end_cells(paths)
    assert paths[0] == [(2, 0), (3, 0)]
    assert paths[1] == [(4, 0)]


def test_arriving_on_held_enemy_cell_gets_shoved():
    layout = MapLayout("test", frozenset())
    soldier = ROSTER["Soldier"]
    match = Match(layout, [[soldier], [soldier]], [[(7, 0)], [(7, 2)]])
    match.units[1].hp = 1e9
    match.units[0].hp = 1e9
    match.play_round({0: [(7, 0), (7, 1), (7, 2)]})
    assert match.units[1].cell == (7, 2)
    assert match.units[0].cell == (7, 1)


def squad(shooter, shooter_cell, enemies, walls=()):
    layout = MapLayout("test", frozenset(walls))
    names = [name for name, _ in enemies]
    return Match(
        layout,
        [[ROSTER[shooter]], [ROSTER[name] for name in names]],
        [[shooter_cell], [cell for _, cell in enemies]],
    )


def test_spread_spills_onto_an_enemy_beside_the_target():
    match = squad("Ramrod", (7, 2), [("Commander", (7, 4)), ("Commander", (8, 4))])
    for enemy in match.units[1:]:
        enemy.facing = 0.0
    match.play_round({})
    target, bystander = match.units[1], match.units[2]
    assert 120 - bystander.hp > 0
    assert (120 - target.hp) + (120 - bystander.hp) <= 80 + 1e-6


def test_wall_beside_the_line_eats_part_of_a_spread():
    open_ground = squad("Commander", (7, 2), [("Ramrod", (8, 6))])
    open_ground.play_round({})
    walled = squad("Commander", (7, 2), [("Ramrod", (8, 6))], walls=[(8, 3)])
    walled.play_round({})
    assert 0 < walled.damage_dealt[0] < open_ground.damage_dealt[0]


def test_sampled_mode_deals_whole_bullets():
    match = squad("Ramrod", (7, 2), [("Commander", (7, 4)), ("Commander", (8, 4))])
    match.config.sample_hits = True
    match.play_round({})
    for enemy in match.units[1:]:
        assert (enemy.stats.maxHealth - enemy.hp) % 8 == pytest.approx(0)


def test_consecutive_rounds_always_finish():
    match = duel("Soldier", "Commander", red_cell=(7, 1), blue_cell=(7, 8))
    for _ in range(4):
        assert match.play_round({}) < 30
