using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationHandler : MonoBehaviour
{
    Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
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
