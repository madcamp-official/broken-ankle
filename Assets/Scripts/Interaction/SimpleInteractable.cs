using UnityEngine;
using UnityEngine.Events;

namespace Ashburn.Interaction
{
    /// <summary>
    /// A ready-made <see cref="IInteractable"/> that just raises a UnityEvent, so level building
    /// does not have to wait for a bespoke script per prop. Good enough for the breaker box, doors
    /// and note pickups in the first test room; swap it for a real class once behaviour grows.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SimpleInteractable : MonoBehaviour, IInteractable
    {
        [Tooltip("Shown while the player is in range, e.g. \"Repair the breaker\".")]
        [SerializeField] string prompt = "Interact";

        [Tooltip("Off means the prompt is hidden and the object cannot be used, for props that unlock later.")]
        [SerializeField] bool interactable = true;

        [Tooltip("Uncheck for things the player may use repeatedly, such as a light switch.")]
        [SerializeField] bool singleUse = true;

        [Space]
        [SerializeField] UnityEvent onInteract;

        bool _used;

        public string Prompt => prompt;

        public bool CanInteract(GameObject interactor) => interactable && !(singleUse && _used);

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
                return;

            _used = true;
            onInteract?.Invoke();
        }

        /// <summary>Lets other systems open or lock this prop at runtime, e.g. once a fuse is found.</summary>
        public void SetInteractable(bool value) => interactable = value;
    }
}
