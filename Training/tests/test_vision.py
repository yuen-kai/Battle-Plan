from battlesim.vision import PROJECTILE_RADIUS, Board, has_grid_line_of_sight

RAY = 1e-6


def test_straight_shot_through_wall_is_blocked():
    board = Board({(0, 0)})
    assert board.sweep_hits_wall((-1, 0), (1, 0), PROJECTILE_RADIUS)


def test_single_corner_peek_is_open():
    board = Board({(0, 0)})
    assert not board.sweep_hits_wall((-1, 0), (0, 1), PROJECTILE_RADIUS)


def test_shallow_corner_lane_needs_projectile_clearance():
    board = Board({(0, 0)})
    assert not board.sweep_hits_wall((0, -2), (1, 1), RAY)
    assert board.sweep_hits_wall((0, -2), (1, 1), PROJECTILE_RADIUS)


def test_diagonal_pinch_between_two_walls_is_blocked():
    assert Board({(4, 4), (5, 5)}).sweep_hits_wall((5, 4), (4, 5), PROJECTILE_RADIUS)
    assert not Board({(4, 4)}).sweep_hits_wall((5, 4), (4, 5), PROJECTILE_RADIUS)


def test_grid_los_blocks_through_wall_and_allows_single_corner():
    walls = {(1, 0)}
    assert not has_grid_line_of_sight((0, 0), (2, 0), walls)
    assert has_grid_line_of_sight((0, 0), (2, 2), {(1, 0)})
    assert not has_grid_line_of_sight((0, 0), (2, 2), {(1, 0), (0, 1)})


def test_viewer_cells_are_a_manhattan_diamond_on_open_ground():
    cells = Board(set()).viewer_cells((7, 5), 2)
    assert len(cells) == 13
    assert (9, 5) in cells and (8, 6) in cells and (9, 6) not in cells


def test_smoke_blocks_lines_through_its_interior_but_not_endpoints():
    from battlesim.vision import segment_crosses_cells
    smoke = {(5, 5)}
    assert segment_crosses_cells((4, 5), (6, 5), smoke)
    assert not segment_crosses_cells((5, 5), (8, 5), smoke)
    assert not segment_crosses_cells((4, 4), (6, 6), {(5, 4)})


def test_unit_in_smoke_centre_sees_only_the_ring():
    smoke = frozenset((x, y) for x in (4, 5, 6) for y in (4, 5, 6))
    cells = Board(set()).viewer_cells((5, 5), 5, smoke)
    assert cells <= smoke
