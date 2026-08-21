using UnityEngine;

/// <summary>
/// Runs the character sandbox match: keeps the target dummy's cease-fire in step with
/// <see cref="SandboxSession.DummyFightsBack"/>. <see cref="GameLoop"/> attaches this at runtime
/// when a <see cref="SandboxSession"/> is active, and only ever on the loopback host, so it reads
/// server state directly instead of going through RPCs — the same arrangement
/// <see cref="TutorialDirector"/> uses.
///
/// The dummy's movement and abilities are handled entirely by <see cref="BotFrozenUnits"/>, so this
/// exists only for the one thing a frozen plan cannot express: whether the unit shoots. That has to
/// be re-asserted rather than set once, because the round loop reopens fire on its own schedule
/// (<c>OrderStillShooting</c> at the top of planning, <c>OrderContinueShooting</c> for the
/// return-fire window mid-execution) and because the designer can flip the toggle at any point in a
/// live match.
/// </summary>
public class SandboxDirector : MonoBehaviour
{
    private void Update()
    {
        if (!SandboxSession.IsActive)
            return;

        bool holdFire = !SandboxSession.DummyFightsBack;
        foreach (GameObject unit in GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex))
        {
            Shooting shooting = unit == null ? null : unit.GetComponent<Shooting>();
            if (shooting == null || shooting.holdFire == holdFire)
                continue;

            shooting.holdFire = holdFire;
            // holdFire is only consulted when a shooting cycle starts, so switching it on mid-burst
            // needs an explicit stand-down to take effect before the next cycle would have.
            if (holdFire)
                shooting.StandDown();
        }
    }
}
