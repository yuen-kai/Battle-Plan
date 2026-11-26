from dataclasses import dataclass

TEAM_SIZE = 5


@dataclass
class Reward:
    win: float = 1.0
    loss: float = -1.0
    draw: float = -0.5
    hp_weight: float = 0.5
    count_weight: float = 0.5
    combo_weight: float = 0.0
    round_cost: float = 0.01
    shaping_fade: float = 0.8
    gamma: float = 0.98
    progress: float = 0.0

    def shaping_scale(self):
        if self.shaping_fade <= 0:
            return 0.0
        return max(0.0, 1.0 - self.progress / self.shaping_fade)

    def potential(self, match, team):
        def standing(side):
            units = [u for u in match.units if u.team == side]
            hp = sum(max(0.0, u.hp) for u in units if u.alive) / sum(u.stats.maxHealth for u in units)
            alive = sum(u.alive for u in units) / len(units)
            return hp, alive

        own_hp, own_alive = standing(team)
        enemy_hp, enemy_alive = standing(1 - team)
        return self.hp_weight * (own_hp - enemy_hp) + self.count_weight * (own_alive - enemy_alive)

    def round_reward(self, before, match, team):
        after = 0.0 if match.finished else self.potential(match, team)
        combos = match.combos(team) - match.combos(1 - team)
        reward = self.shaping_scale() * (self.gamma * after - before + self.combo_weight * combos / TEAM_SIZE)
        reward -= self.round_cost
        if match.finished:
            reward += self.draw if match.winner is None else self.win if match.winner == team else self.loss
        return reward
