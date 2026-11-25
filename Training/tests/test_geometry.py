import math

from battlesim.geometry import first_hit_shares, intercept_time, zone_hit_probability

WEIGHTS = (1.0, 2.0, 1.0)


def test_zero_spread_is_all_or_nothing():
    assert zone_hit_probability(0.0, 0.1, 0.0, WEIGHTS) == 1.0
    assert zone_hit_probability(0.2, 0.1, 0.0, WEIGHTS) == 0.0


def test_window_covering_whole_spread_always_hits():
    assert math.isclose(zone_hit_probability(0.0, 1.0, 10.0, WEIGHTS), 1.0)


def test_centre_window_uses_centre_zone_density():
    s = math.radians(9.0)
    half = s / 6
    expected = 2.0 / 4.0 * (2 * half) / (2 * s / 3)
    assert math.isclose(zone_hit_probability(0.0, half, 9.0, WEIGHTS), expected)


def test_window_outside_spread_misses():
    assert zone_hit_probability(math.radians(20), 0.01, 3.0, WEIGHTS) == 0.0


def test_intercept_of_stationary_target():
    flight = intercept_time((0.0, 0.0), lambda t: (6.0, 0.0), 0.0, 3.0, 5.0)
    assert math.isclose(flight, 2.0, rel_tol=1e-6)


def test_intercept_beyond_range_is_none():
    assert intercept_time((0.0, 0.0), lambda t: (30.0, 0.0), 0.0, 3.0, 5.0) is None


def test_nearer_obstacle_takes_the_overlap():
    shares = first_hit_shares([(5.0, -0.1, 0.1, "far"), (2.0, 0.0, 0.2, "near")], 0.2)
    assert math.isclose(shares["near"], 0.5)
    assert math.isclose(shares["far"], 0.25)


def test_walls_absorb_their_band_without_a_share():
    shares = first_hit_shares([(5.0, -0.2, 0.2, "body"), (1.0, -0.2, 0.0, None)], 0.2)
    assert shares == {"body": 0.5} or math.isclose(shares["body"], 0.5)


def test_zero_spread_picks_nearest_obstacle_on_the_aim_line():
    assert first_hit_shares([(5.0, -0.1, 0.1, "a"), (3.0, -0.1, 0.1, "b")], 0.0) == {"b": 1.0}
    assert first_hit_shares([(5.0, -0.1, 0.1, "a"), (3.0, -0.1, 0.1, None)], 0.0) == {}
