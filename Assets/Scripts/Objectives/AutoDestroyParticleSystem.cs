using UnityEngine;

/// <summary>
/// Destroys the GameObject this component is attached to after its ParticleSystem (and any children's) has finished playing.
/// This is useful for one-shot effects like explosions or collection particles.
/// It finds the first ParticleSystem on this GameObject or its children and monitors it.
/// </summary>
public class AutoDestroyParticleSystem : MonoBehaviour
{
    private ParticleSystem ps;

    public void Start()
    {
        // Find the particle system in this object or any of its children.
        // This is robust enough to work even if the particle system is not on the root of the prefab.
        ps = GetComponentInChildren<ParticleSystem>();

        if (ps == null)
        {
            Debug.LogWarning($"AutoDestroyParticleSystem on {gameObject.name} could not find a ParticleSystem to track. The GameObject will be destroyed after 3 seconds as a fallback.", this);
            Destroy(gameObject, 3f);
        }
    }

    public void Update()
    {
        // If we have a particle system, check if it's still alive.
        if (ps != null)
        {
            // IsAlive returns false when the system has stopped emitting and all particles are dead.
            if (!ps.IsAlive())
            {
                Destroy(gameObject);
            }
        }
    }
} 