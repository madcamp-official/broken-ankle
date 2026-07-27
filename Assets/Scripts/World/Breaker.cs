using Ashburn.Interaction;
using Ashburn.Noise;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// The breaker box. Restores power to the building.
    ///
    /// A real class rather than a <see cref="SimpleInteractable"/> with a UnityEvent, for the reason
    /// SimpleInteractable's own summary predicted: the behaviour grew. It has to ask
    /// <see cref="PowerGrid"/> rather than flip lights itself, refuse once the power is already on,
    /// and make a noise loud enough to be worth thinking about — none of which fits in a list of
    /// SetActive calls, and all of which broke the moment the prop became a prefab.
    ///
    /// Throwing it is loud on purpose. Ashburn.MD section 9 turns noise into the way out, and this
    /// is the first place the players find that out.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Breaker : MonoBehaviour, IInteractable
    {
        [Tooltip("Shown while the player is in range.")]
        [SerializeField] string prompt = "Restore power";

        [Tooltip("Whether it can be thrown again once the power is on. Off for a one-way repair.")]
        [SerializeField] bool reusable;

        [Tooltip("How far the clunk carries, in world units. Louder than a run: this is not a " +
                 "quiet thing to do.")]
        [SerializeField] float noiseRange = 16f;

        public string Prompt => prompt;

        public bool CanInteract(GameObject interactor)
        {
            var grid = PowerGrid.Current;
            if (grid == null)
                return false;

            return reusable || !grid.IsPowered;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
                return;

            var grid = PowerGrid.Current;
            grid.SetPowered(reusable ? !grid.IsPowered : true);

            // Whoever threw it hears their own noise as Self; a partner across the building hears
            // it as something happening in a room they are not in.
            NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self);
        }
    }
}
