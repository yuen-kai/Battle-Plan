import math
from functools import lru_cache

from .maps import CELL_SIZE, COLUMNS, ROWS

WALL_HALF = 0.5
WALL_CHAMFER = 0.4 / CELL_SIZE
PROJECTILE_RADIUS = 0.25 / CELL_SIZE
DEFAULT_BODY_RADIUS = 0.65 / CELL_SIZE
BODY_RADIUS = {"Ramrod": 0.78 / CELL_SIZE}
CORNERS = ((1, 1), (-1, 1), (-1, -1), (1, -1))


SMOKE_INSET = 0.5 - 0.0001


def containing_cell(point):
    return (math.floor(point[0] + 0.5), math.floor(point[1] + 0.5))


def segment_crosses_cells(start, end, cells):
    if not cells or (abs(start[0] - end[0]) < 1e-5 and abs(start[1] - end[1]) < 1e-5):
        return False
    if start[0] > end[0] or (start[0] == end[0] and start[1] > end[1]):
        start, end = end, start
    start_cell, end_cell = containing_cell(start), containing_cell(end)
    for cell in cells:
        if cell == start_cell or cell == end_cell:
            continue
        enter, leave = 0.0, 1.0
        for axis in (0, 1):
            delta = end[axis] - start[axis]
            lo, hi = cell[axis] - SMOKE_INSET, cell[axis] + SMOKE_INSET
            if abs(delta) <= 1e-12:
                if not lo <= start[axis] <= hi:
                    break
                continue
            t1, t2 = (lo - start[axis]) / delta, (hi - start[axis]) / delta
            if t1 > t2:
                t1, t2 = t2, t1
            enter, leave = max(enter, t1), min(leave, t2)
            if enter > leave:
                break
        else:
            return True
    return False


def body_radius(name):
    return BODY_RADIUS.get(name, DEFAULT_BODY_RADIUS)


def has_grid_line_of_sight(viewer, target, walls):
    x, y = viewer
    dx, dy = abs(target[0] - viewer[0]), abs(target[1] - viewer[1])
    sx = 1 if target[0] >= viewer[0] else -1
    sy = 1 if target[1] >= viewer[1] else -1
    cx = cy = 0

    def blocks(cell):
        return cell != viewer and cell != target and cell in walls

    while cx < dx or cy < dy:
        h, v = (1 + 2 * cx) * dy, (1 + 2 * cy) * dx
        if h == v:
            if blocks((x + sx, y)) and blocks((x, y + sy)):
                return False
            x += sx
            y += sy
            cx += 1
            cy += 1
        elif h < v:
            x += sx
            cx += 1
        else:
            y += sy
            cy += 1
        if blocks((x, y)):
            return False
    return True


CONTACT_CACHE_LIMIT = 100_000


@lru_cache(maxsize=None)
def shared_board(walls):
    return Board(walls)


class Board:
    def __init__(self, walls):
        self.walls = frozenset(walls)
        self.polygons = {cell: self._polygon(cell) for cell in self.walls}
        self._viewer_cache = {}
        self._obstacle_cache = {}
        self._contact_cache = {}

    def _polygon(self, cell):
        cx, cy = cell
        points = []
        for ox, oy in CORNERS:
            if (cx + ox, cy + oy) in self.walls:
                points.append((cx + ox * WALL_HALF, cy + oy * WALL_HALF))
                continue
            inset = WALL_HALF - WALL_CHAMFER
            if ox * oy > 0:
                points.append((cx + ox * WALL_HALF, cy + oy * inset))
                points.append((cx + ox * inset, cy + oy * WALL_HALF))
            else:
                points.append((cx + ox * inset, cy + oy * WALL_HALF))
                points.append((cx + ox * WALL_HALF, cy + oy * inset))
        return points

    def viewer_cells(self, cell, vision_range, smoke=frozenset()):
        if smoke:
            key = (cell, vision_range, smoke)
            cached = self._viewer_cache.get(key)
            if cached is None:
                cached = frozenset(
                    target
                    for target in self.viewer_cells(cell, vision_range)
                    if not segment_crosses_cells(cell, target, smoke)
                )
                self._viewer_cache[key] = cached
            return cached
        key = (cell, vision_range)
        cached = self._viewer_cache.get(key)
        if cached is None:
            cached = frozenset(self._compute_viewer_cells(cell, vision_range))
            self._viewer_cache[key] = cached
        return cached

    def _compute_viewer_cells(self, cell, vision_range):
        vx, vy = cell
        r = max(0, vision_range)
        for dy in range(-r, r + 1):
            span = r - abs(dy)
            for dx in range(-span, span + 1):
                target = (vx + dx, vy + dy)
                if 0 <= target[0] < COLUMNS and 0 <= target[1] < ROWS:
                    if has_grid_line_of_sight(cell, target, self.walls):
                        yield target

    def wall_obstacles(self, origin, max_distance, radius):
        key = (origin, max_distance, radius)
        cached = self._obstacle_cache.get(key)
        if cached is None:
            cached = tuple(self._compute_wall_obstacles(origin, max_distance, radius))
            self._obstacle_cache[key] = cached
        return cached

    def _compute_wall_obstacles(self, origin, max_distance, radius):
        ox, oy = origin
        for (wx, wy), polygon in self.polygons.items():
            if math.hypot(wx - ox, wy - oy) > max_distance + WALL_HALF * 1.5 + radius:
                continue
            near = _point_polygon_distance(origin, polygon) - radius
            if near <= 0:
                continue
            centre = math.atan2(wy - oy, wx - ox)
            lo, hi = math.inf, -math.inf
            for vx, vy in polygon:
                dist = math.hypot(vx - ox, vy - oy)
                angle = (math.atan2(vy - oy, vx - ox) - centre + math.pi) % (2 * math.pi) - math.pi
                widen = math.asin(min(1.0, radius / dist))
                lo, hi = min(lo, angle - widen), max(hi, angle + widen)
            yield near, centre, lo, hi

    def sweep_hits_wall(self, start, end, radius):
        return self.first_wall_contact(start, end, radius) is not None

    def first_wall_contact(self, start, end, radius):
        key = (start, end, radius)
        if key not in self._contact_cache:
            if len(self._contact_cache) >= CONTACT_CACHE_LIMIT:
                self._contact_cache.clear()
            self._contact_cache[key] = self._compute_first_wall_contact(start, end, radius)
        return self._contact_cache[key]

    def _compute_first_wall_contact(self, start, end, radius):
        dx, dy = end[0] - start[0], end[1] - start[1]
        length = math.hypot(dx, dy)
        if length <= 1e-9:
            return None
        ux, uy = dx / length, dy / length
        pad = WALL_HALF + radius
        lo_x, hi_x = min(start[0], end[0]) - pad, max(start[0], end[0]) + pad
        lo_y, hi_y = min(start[1], end[1]) - pad, max(start[1], end[1]) + pad
        best = None
        for (wx, wy), polygon in self.polygons.items():
            if wx < lo_x or wx > hi_x or wy < lo_y or wy > hi_y:
                continue
            if _point_polygon_distance(start, polygon) <= radius:
                continue
            t = _ray_polygon_sweep(start, ux, uy, polygon, radius)
            if t is not None and t <= length and (best is None or t < best):
                best = t
        return best


def ray_circle(px, py, ux, uy, cx, cy, radius):
    fx, fy = px - cx, py - cy
    b = fx * ux + fy * uy
    c = fx * fx + fy * fy - radius * radius
    disc = b * b - c
    if disc < 0:
        return None
    t = -b - math.sqrt(disc)
    return t if t >= 0 else None


def _ray_segment_offset(px, py, ux, uy, ax, ay, bx, by, radius):
    ex, ey = bx - ax, by - ay
    length = math.hypot(ex, ey)
    if length <= 1e-12:
        return None
    nx, ny = -ey / length, ex / length
    best = None
    for side in (1.0, -1.0):
        ox, oy = ax + nx * radius * side, ay + ny * radius * side
        denom = ux * ey - uy * ex
        if abs(denom) <= 1e-12:
            continue
        wx, wy = ox - px, oy - py
        t = (wx * ey - wy * ex) / denom
        s = (wx * uy - wy * ux) / denom
        if t >= 0 and 0 <= s <= 1 and (best is None or t < best):
            best = t
    return best


def _ray_polygon_sweep(start, ux, uy, polygon, radius):
    px, py = start
    best = None
    count = len(polygon)
    for i in range(count):
        ax, ay = polygon[i]
        bx, by = polygon[(i + 1) % count]
        for t in (
            _ray_segment_offset(px, py, ux, uy, ax, ay, bx, by, radius),
            ray_circle(px, py, ux, uy, ax, ay, radius),
        ):
            if t is not None and (best is None or t < best):
                best = t
    return best


def _point_polygon_distance(point, polygon):
    px, py = point
    inside = True
    best = math.inf
    count = len(polygon)
    for i in range(count):
        ax, ay = polygon[i]
        bx, by = polygon[(i + 1) % count]
        ex, ey = bx - ax, by - ay
        if ex * (py - ay) - ey * (px - ax) < 0:
            inside = False
        length_sq = ex * ex + ey * ey
        t = max(0.0, min(1.0, ((px - ax) * ex + (py - ay) * ey) / length_sq))
        best = min(best, math.hypot(px - (ax + t * ex), py - (ay + t * ey)))
    return 0.0 if inside else best
