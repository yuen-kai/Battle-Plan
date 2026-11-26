import random

import pytest

from battlesim.baselines import advance_policy
from battlesim.env import DodgeBuilder, TeamMemory
from battlesim.maps import MapLayout
from battlesim.paths import DOWN, STAY, UP
from battlesim.sim import Match
from battlesim.units import load_roster

ROSTER = load_roster()


def board(team0, team1, walls=()):
    layout = MapLayout("test", frozenset(walls))
    return Match(
        layout,
        [[ROSTER[n] for n, _ in team0], [ROSTER[n] for n, _ in team1]],
        [[c for _, c in team0], [c for _, c in team1]],
    )


def unit(match, team, slot=0):
    return [u for u in match.units if u.team == team][slot]


def test_cooldown_blocks_the_next_n_rounds():
    match = board([("Soldier", (1, 0))], [("Ramrod", (13, 9))])
    soldier = unit(match, 0)
    match.play_round({soldier.uid: ("ability", (1, 5))})
    seen = [soldier.cooldown]
    for _ in range(3):
        assert match.legal_ability_targets(soldier) == [] or soldier.cooldown == 0
        match.play_round({})
        seen.append(soldier.cooldown)
    assert seen == [3, 2, 1, 0]
    assert match.legal_ability_targets(soldier)


def test_grenade_hits_within_radius_not_through_walls_or_allies():
    match = board(
        [("Soldier", (7, 1)), ("Ramrod", (8, 6))],
        [("Ramrod", (7, 6)), ("Ramrod", (5, 6)), ("Ramrod", (9, 6))],
        walls=[(8, 6 + 1)],
    )
    soldier, ally = unit(match, 0, 0), unit(match, 0, 1)
    ally.x, ally.y = 1.0, 9.0
    centre, far, beside = unit(match, 1, 0), unit(match, 1, 1), unit(match, 1, 2)
    match.play_round({soldier.uid: ("ability", (7, 6))})
    assert centre.hp == pytest.approx(80)
    assert far.hp == pytest.approx(160)
    assert beside.hp == pytest.approx(160)


def test_wall_shields_grenade_splash():
    match = board([("Soldier", (7, 1))], [("Ramrod", (8, 6))], walls=[(8, 5)])
    match.play_round({unit(match, 0).uid: ("ability", (8, 4))})
    assert unit(match, 1).hp == pytest.approx(160)


def test_grenade_alerts_and_a_straight_two_cell_dive_escapes():
    match = board([("Soldier", (7, 1))], [("Ramrod", (7, 6)), ("Ramrod", (7, 9))])
    target, outside = unit(match, 1, 0), unit(match, 1, 1)
    alerted = match.begin_round({unit(match, 0).uid: ("ability", (7, 6))})
    assert alerted == {1: {target.uid}}
    match.apply_dodges({target.uid: [(8, 6), (9, 6)]})
    match.execute()
    assert target.hp == pytest.approx(160)
    assert target.cell == (9, 6)
    assert outside.hp == pytest.approx(160)


def test_scripted_dodge_never_steps_toward_known_enemies_on_either_side():
    for team, dodger_cell, thrower_cell, forward in ((0, (7, 3), (7, 7), UP), (1, (7, 6), (7, 2), DOWN)):
        cells = {team: [("Ramrod", dodger_cell)], 1 - team: [("Soldier", thrower_cell)]}
        match = board(cells[0], cells[1])
        thrower = unit(match, 1 - team)
        alerted = match.begin_round({thrower.uid: ("ability", dodger_cell)})
        memory = TeamMemory()
        memory.last_known[thrower.uid] = (thrower_cell, "Soldier", 1.0, 0)
        moves = {
            advance_policy(random.Random(seed))(DodgeBuilder(match, team, alerted[team]), memory)
            for seed in range(40)
        }
        assert forward not in moves and STAY not in moves
        assert len(moves) > 1


def test_diving_cancels_the_divers_own_ability_without_spending_it():
    match = board([("Soldier", (7, 1))], [("Soldier", (7, 5))])
    mine, theirs = unit(match, 0), unit(match, 1)
    alerted = match.begin_round({mine.uid: ("ability", (7, 5)), theirs.uid: ("ability", (7, 1))})
    assert theirs.uid in alerted[1] and mine.uid in alerted[0]
    match.apply_dodges({theirs.uid: [(8, 5), (9, 5)]})
    match.execute()
    assert theirs.cooldown == 0
    assert mine.cooldown == 3


def test_smoke_blocks_sight_for_this_round_and_the_next():
    match = board([("Commander", (7, 1))], [("Ramrod", (7, 7))])
    commander = unit(match, 0)
    match.play_round({commander.uid: ("ability", (7, 4))})
    assert (7, 4) in match.smoke and len(match.smoke) == 9
    assert (7, 7) not in match.visible_cells(0)
    match.play_round({})
    assert match.smoke == frozenset()


def test_pogo_lands_on_its_target():
    match = board([("PogoRider", (2, 0))], [("Ramrod", (13, 9))])
    rider = unit(match, 0)
    match.play_round({rider.uid: ("ability", (2, 6))})
    assert rider.cell == (2, 6) and rider.collider


def test_area_lock_kills_the_first_enemy_on_the_line_only():
    match = board(
        [("Sniper", (7, 0)), ("Commander", (7, 2))],
        [("Soldier", (7, 8)), ("Soldier", (7, 9))],
    )
    sniper = unit(match, 0, 0)
    near, behind = unit(match, 1, 0), unit(match, 1, 1)
    near.x, behind.x = 7.0, 7.0
    match.play_round({sniper.uid: ("ability", (7, 5))})
    assert not near.alive
    assert behind.alive


def test_area_lock_stops_at_walls():
    match = board([("Sniper", (7, 0))], [("Soldier", (7, 8))], walls=[(7, 6)])
    match.play_round({unit(match, 0).uid: ("ability", (7, 4))})
    assert unit(match, 1).hp == pytest.approx(120)


def test_ram_charge_shoves_enemies_on_its_line_to_the_left():
    match = board([("Ramrod", (7, 2))], [("Commander", (7, 4))])
    ramrod, victim = unit(match, 0), unit(match, 1)
    match.play_round({ramrod.uid: ("ability", (7, 3))})
    assert ramrod.cell == (7, 5)
    assert victim.cell == (6, 4)


def test_illegal_ability_targets_fall_back_to_standing_still():
    match = board([("Soldier", (7, 1))], [("Ramrod", (13, 9))])
    soldier = unit(match, 0)
    match.play_round({soldier.uid: ("ability", (7, 9))})
    assert soldier.cooldown == 0
