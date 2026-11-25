from .maps import COLUMNS, ROWS


def spawn_cells(layout, unit_count):
    band = layout.host_deployment_columns
    low = band[0] if band else (2 if unit_count <= COLUMNS - 4 else 0)
    high = band[1] if band else COLUMNS - 1 - low
    host, opponent = [], []
    for index in range(unit_count):
        progress = 0.5 if unit_count == 1 else index / (unit_count - 1)
        column = round(low + (high - low) * progress)
        host.append((column, 0))
        opponent.append((COLUMNS - 1 - column, ROWS - 1))
    return [host, opponent]
