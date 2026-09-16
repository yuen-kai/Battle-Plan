using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maps the logical states the game asks for — "Idle", "Moving", "Aiming", "Shoot", "Dodge",
/// "DiveRecovery", and an ability's own name — onto whatever the unit's rig actually has, and
/// replicates them so every client sees the pose rather than only the host.
///
/// <para>
/// That replication is the reason this is a <c>NetworkBehaviour</c>. Every caller is server-side:
/// <c>Movement</c>, <c>Shooting</c> and <c>Ability</c> all run their coroutines on the host and
/// clients receive nothing but transform snapshots, so a state set locally used to be visible to
/// the host alone. It travels as a <c>NetworkVariable</c> rather than as a <c>ClientRpc</c> for the
/// same reason <c>Movement</c>'s boost does: a unit coming out of fog, or a player joining a match
/// in progress, has to arrive in the pose the server has it in rather than in whatever pose it
/// happened to be told about while it could not see.
/// </para>
///
/// <para>
/// Two rig generations are driven from one set of names. The eight generated characters carry a
/// controller from <c>CharacterAnimationBuilder</c> with a state per logical name on one layer. The
/// hand-authored units carry clips split across a character layer and a gun layer — Sniper's
/// "Person Walking" over "Gun Still" — so those are looked up in <see cref="LegacyStates"/> when
/// the logical name itself is not a state. A rig with neither is left alone rather than spammed at.
/// </para>
/// </summary>
public class AnimationHandler : NetworkBehaviour
{
    /// <summary>
    /// What the hand-authored rigs call each logical state, as a character clip over a gun clip.
    /// Only Sniper has the full set today; the rest of the five resolve to nothing and no-op.
    /// </summary>
    private static readonly Dictionary<string, (string Person, string Gun)> LegacyStates = new()
    {
        { "Idle", ("Person Idle", "Gun Idle") },
        { "Moving", ("Person Walking", "Gun Still") },
        { "Aiming", ("Person Aiming", "Gun Still") },
        { "Shoot", ("Person Shoot", "Gun Shoot") },
    };

    /// <summary>
    /// Long enough to blend a pose rather than cut to it, short enough that a shot still reads as
    /// instant. Fixed seconds, not a fraction of the clip, or a long idle would take a second to
    /// arrive and a recoil would be over before it got there.
    /// </summary>
    private const float BlendSeconds = 0.12f;

    private const int GunLayer = 1;

    private readonly NetworkVariable<FixedString32Bytes> replicatedState =
        new(writePerm: NetworkVariableWritePermission.Server);

    private Animator animator;
    private bool resolved;

    /// <summary>
    /// The state to fall back to once a one-shot finishes. Tracked so a unit firing a magazine
    /// settles back into its aim between rounds instead of dropping to a neutral idle it has no
    /// business being in while it still has a target.
    /// </summary>
    private string held = "Idle";

    /// <summary>
    /// Resolved on demand rather than in <c>Start</c>, because these prefabs are also instantiated
    /// outside a match — the character carousel, portrait shoots and <c>TrailerStudio</c> all drive
    /// animation on units that were never spawned.
    /// </summary>
    private Animator Rig
    {
        get
        {
            if (!resolved)
            {
                animator = ResolveAnimator();
                resolved = true;
            }
            return animator;
        }
    }

    public override void OnNetworkSpawn()
    {
        replicatedState.OnValueChanged += OnStateReplicated;

        // A client that spawns this unit late is already behind: whatever the server last set is
        // the pose the unit is in, not the rest pose the controller starts in.
        if (!IsServer && !replicatedState.Value.IsEmpty)
            Enter(replicatedState.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        replicatedState.OnValueChanged -= OnStateReplicated;
    }

    private void OnStateReplicated(FixedString32Bytes previous, FixedString32Bytes current)
    {
        if (!IsServer)
            Enter(current.ToString());
    }

    // Each unit type's rig lives on its own imported model child (e.g. "Sniper (1)"), not on
    // this shared component's GameObject, so search descendants too. Prefer an Animator that is
    // both enabled and has a Controller assigned; units without finished animations yet may still
    // carry a placeholder Animator (auto-added by the model import) with no Controller, and
    // calling Play/SetTrigger on that would just spam console errors instead of safely no-oping.
    private Animator ResolveAnimator()
    {
        foreach (Animator candidate in GetComponentsInChildren<Animator>(true))
        {
            if (candidate.enabled && candidate.runtimeAnimatorController != null)
                return candidate;
        }
        return null;
    }

    /// <summary>Holds a state until something else is asked for.</summary>
    public void PlayAnimation(string animationName)
    {
        held = animationName;
        Enter(animationName);
        Publish(animationName);
    }

    /// <summary>
    /// Plays a one-shot and returns to whatever was being held once <paramref name="delay"/> is up.
    /// A unit working through a magazine therefore falls back to its aim between shots rather than
    /// to a neutral idle, which is what <see cref="held"/> is for.
    /// </summary>
    public void TriggerAnimation(string animationName, float delay = 1f)
    {
        Enter(animationName);
        Publish(animationName);
        StartCoroutine(PlayAnimationWithDelay(held, delay));
    }

    /// <summary>
    /// Plays a state on one client alone, for the poses that are private to whoever is being asked
    /// to answer for them. The dodge weave is the case: which units have been targeted is the
    /// dodging player's information, and a unit that visibly ducks on every screen hands the
    /// shooter a free read on what its owner is about to do.
    /// </summary>
    [ClientRpc]
    public void PlayPrivateAnimationClientRpc(FixedString32Bytes animationName, ClientRpcParams to = default)
    {
        Enter(animationName.ToString());
    }

    private void Publish(string animationName)
    {
        if (IsSpawned && IsServer)
            replicatedState.Value = new FixedString32Bytes(animationName);
    }

    private void Enter(string animationName)
    {
        Animator rig = Rig;
        if (rig == null || string.IsNullOrEmpty(animationName))
            return;

        if (Blend(rig, animationName, 0))
            return;

        if (!LegacyStates.TryGetValue(animationName, out (string Person, string Gun) legacy))
            return;

        Blend(rig, legacy.Person, 0);
        Blend(rig, legacy.Gun, GunLayer);
    }

    private static bool Blend(Animator rig, string state, int layer)
    {
        if (layer >= rig.layerCount || !rig.HasState(layer, Animator.StringToHash(state)))
            return false;

        rig.CrossFadeInFixedTime(state, BlendSeconds, layer);
        return true;
    }

    IEnumerator PlayAnimationWithDelay(string animationName, float delay)
    {
        yield return new WaitForSeconds(delay);
        Enter(animationName);
    }
}
