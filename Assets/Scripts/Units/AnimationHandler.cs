using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationHandler : MonoBehaviour
{
    Animator animator;

    void Start()
    {
        animator = ResolveAnimator();
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

    public void PlayAnimation(string animationName)
    {
        if (animator == null)
            return;
        if (animationName == "Moving")
        {
            animator.Play("Person Walking");
            animator.Play("Gun Still", 1);
        }
        else if (animationName == "Aiming")
        {
            animator.Play("Person Aiming");
            animator.Play("Gun Still", 1);
        }
        else if (animationName == "Idle")
        {
            animator.Play("Person Idle");
            animator.Play("Gun Idle", 1);
        }
    }

    public void TriggerAnimation(string animationName, float delay = 1f)
    {
        if (animator == null)
            return;
        if (animationName == "Shoot")
        {
            animator.Play("Person Shoot");
            animator.Play("Gun Shoot", 1);
        }
        StartCoroutine(PlayAnimationWithDelay("Idle", delay));
    }

    IEnumerator PlayAnimationWithDelay(string animationName, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayAnimation(animationName);
    }
}
