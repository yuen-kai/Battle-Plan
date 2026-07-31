using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public abstract class Ability: NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false; // disables Update(), Start(), etc.
            return;
        }
    }

    public virtual void ResetForRespawn()
    {
        StopAllCoroutines();
    }

    public abstract IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius);

    /// <summary>
    /// Samples the route this ability travels to reach <paramref name="targetSquare"/> into
    /// <paramref name="points"/>, so planning can show it before it is committed. Abilities that
    /// cross the board or fly over it override this; the default reports nothing to draw, which is
    /// right for anything that simply takes effect where it was aimed.
    /// <para>
    /// This runs on the targeting client, where the component is disabled and holds no server
    /// state, so an override may only read the caster's own transform, <paramref name="data"/>,
    /// and the static board layout. <paramref name="targetSquare"/> is the square the player
    /// picked, which for a directional ability names a direction from an adjacent cell rather than
    /// the cell the ability ends on — resolving that is the override's job, and doing it here is
    /// what keeps the drawn path honest about where a rush is really stopped by a wall.
    /// </para>
    /// </summary>
    public virtual AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        points.Clear();
        return AbilityPathKind.None;
    }
}

