import math

EPS = 1e-9


def angle_of(dx, dy):
    return math.atan2(dy, dx)


def wrap_angle(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def interval_overlap(lo, hi, a, b):
    return max(0.0, min(hi, b) - max(lo, a))


def zone_hit_probability(center, half_width, spread_deg, zone_weights):
    if spread_deg <= EPS:
        return 1.0 if abs(center) <= half_width else 0.0
    s = math.radians(spread_deg)
    lo, hi = center - half_width, center + half_width
    zones = ((-s, -s / 3), (-s / 3, s / 3), (s / 3, s))
    total = sum(zone_weights)
    probability = 0.0
    for (a, b), weight in zip(zones, zone_weights):
        probability += weight / total * interval_overlap(lo, hi, a, b) / (b - a)
    return probability


def intercept_time(shooter, position_at, fire_time, bullet_speed, max_flight, reach=0.0):
    lo, hi = 0.0, max_flight
    def gap(flight):
        px, py = position_at(fire_time + flight)
        return math.hypot(px - shooter[0], py - shooter[1]) - reach - bullet_speed * flight
    if gap(hi) > 0:
        return None
    for _ in range(30):
        mid = 0.5 * (lo + hi)
        if gap(mid) > 0:
            lo = mid
        else:
            hi = mid
    return hi


def segment_point_distance(ax, ay, bx, by, px, py):
    dx, dy = bx - ax, by - ay
    length_sq = dx * dx + dy * dy
    if length_sq <= EPS:
        return math.hypot(px - ax, py - ay)
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / length_sq))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def first_hit_shares(obstacles, spread_rad):
    if spread_rad <= EPS:
        for distance, lo, hi, key in sorted(obstacles, key=lambda o: o[0]):
            if lo <= 0.0 <= hi:
                return {key: 1.0} if key is not None else {}
        return {}
    total = 2 * spread_rad
    covered = []
    shares = {}
    for distance, lo, hi, key in sorted(obstacles, key=lambda o: o[0]):
        a, b = max(lo, -spread_rad), min(hi, spread_rad)
        if a >= b:
            continue
        free = (b - a) - sum(interval_overlap(a, b, c, d) for c, d in covered)
        if key is not None and free > EPS:
            shares[key] = shares.get(key, 0.0) + free / total
        covered = _merge(covered + [(a, b)])
    return shares


def _merge(intervals):
    merged = []
    for a, b in sorted(intervals):
        if merged and a <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], b))
        else:
            merged.append((a, b))
    return merged
