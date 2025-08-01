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

    string[] exclusiveStates = { "Moving", "Aiming" };

    public void PlayAnimationExclusive(string animationName)
    {
        if (animator != null)
        {
            Debug.Log($"Playing animation: {animationName}");
            foreach (var state in exclusiveStates)
            {
                animator.SetBool(state, state == animationName);
            }
            //animator.Play(animationName); 
        }
    }
}
