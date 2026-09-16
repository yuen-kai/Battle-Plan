using UnityEngine;

/// <summary>
/// Makes an authored particle burst answer to raised shield slabs.
///
/// Every other layer of an explosion is clipped against the slab in its own shader (see
/// <c>BP_BlastShadow.hlsl</c>), but a burst riding Unity's own built-in particle material cannot
/// be: that material is shared engine-wide, so giving it blast-shadow values would reach every
/// other user of it. What such a burst can do instead is be stopped by the slab physically, which
/// is the more honest answer for a spray of matter anyway — it hits the barrier and glances off
/// rather than being erased at a line.
///
/// Collides with the shield layers and nothing else. The deck and the walls are deliberately left
/// out: these particles are authored to die on their own clock, and giving them a floor to land on
/// changes the burst everywhere rather than only where a shield is standing.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public sealed class BlastParticleCollision : MonoBehaviour
{
    [SerializeField]
    private float dampen = 0.68f;

    [SerializeField]
    private float bounce = 0.22f;

    [SerializeField]
    private float lifetimeLoss = 0.35f;

    private void Awake()
    {
        ParticleSystem.CollisionModule collision = GetComponent<ParticleSystem>().collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        // Resolved through BlastShadow so the shield layer names live in exactly one place.
        collision.collidesWith = BlastShadow.ShieldMask;
        collision.dampen = dampen;
        collision.bounce = bounce;
        collision.lifetimeLoss = lifetimeLoss;
    }
}
