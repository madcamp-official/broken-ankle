using UnityEngine;

namespace Morrow.Interaction
{
    /// <summary>
    /// Anything the player can walk up to and use: breaker boxes, doors, note pickups.
    /// Put this on a GameObject that also carries a Collider2D, because that collider is what
    /// <see cref="PlayerInteractor"/> searches for. The collider may sit on a child object.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Short line shown to the player while this object is the current target,
        /// e.g. "Repair the breaker". The key hint is added by the UI, not by this string.
        /// </summary>
        string Prompt { get; }

        /// <summary>
        /// False hides the prompt and blocks <see cref="Interact"/>, which is how an object
        /// says "already used" or "you need the fuse first" without being removed from the scene.
        /// </summary>
        bool CanInteract(GameObject interactor);

        void Interact(GameObject interactor);
    }
}
