from battlesim.maps import parse_catalog
from battlesim.paths import is_legal_path, legal_steps
from battlesim.units import load_roster


def test_original_five_load_with_asset_values():
    roster = load_roster()
    assert roster["Sniper"].targetLockDuration == 2.0
    assert roster["PogoRider"].backstabMultiplier == 2.0
    assert roster["Ramrod"].bulletSpread == 25.0
    assert roster["Commander"].canShootWhileMoving is False


def test_maps_parse_top_row_as_row_nine():
    concourse = parse_catalog()["Concourse"]
    assert len(concourse.walls) == 18
    assert (4, 8) in concourse.walls and (0, 5) in concourse.walls
    assert all((14 - x, 9 - y) in concourse.walls for x, y in concourse.walls)


def test_legal_steps_respect_walls_bounds_revisits_and_length():
    walls = frozenset({(1, 1)})
    assert legal_steps([(0, 0)], 3, walls) == [True, True, True, False, False]
    assert legal_steps([(0, 0), (1, 0)], 3, walls) == [True, False, True, False, False]
    assert legal_steps([(0, 0), (1, 0), (2, 0), (3, 0)], 3, walls) == [True, False, False, False, False]


def test_path_legality():
    walls = frozenset({(1, 1)})
    assert is_legal_path([(0, 0), (1, 0), (2, 0)], 3, walls)
    assert not is_legal_path([(0, 0), (1, 1)], 3, walls)
    assert not is_legal_path([(0, 0), (0, 1), (1, 1)], 3, walls)
    assert not is_legal_path([(0, 0), (1, 0), (0, 0)], 3, walls)
