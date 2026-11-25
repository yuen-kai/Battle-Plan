from .maps import in_bounds

STAY, UP, RIGHT, DOWN, LEFT = range(5)
STEPS = {UP: (0, 1), RIGHT: (1, 0), DOWN: (0, -1), LEFT: (-1, 0)}


def step_cell(cell, direction):
    dx, dy = STEPS[direction]
    return (cell[0] + dx, cell[1] + dy)


def legal_steps(path, move_dist, walls, blocked=frozenset()):
    mask = [True, False, False, False, False]
    if len(path) - 1 >= move_dist:
        return mask
    visited = set(path)
    for direction in STEPS:
        nxt = step_cell(path[-1], direction)
        mask[direction] = (
            in_bounds(nxt) and nxt not in walls and nxt not in visited and nxt not in blocked
        )
    return mask


def is_legal_path(path, move_dist, walls):
    if not path or len(path) - 1 > move_dist or len(set(path)) != len(path):
        return False
    for a, b in zip(path, path[1:]):
        if abs(a[0] - b[0]) + abs(a[1] - b[1]) != 1 or not in_bounds(b) or b in walls:
            return False
    return True
