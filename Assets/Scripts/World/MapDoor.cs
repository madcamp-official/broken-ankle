using Ashburn.Interaction;
using Ashburn.Noise;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// A way into another map. The front door of a house, the stairs down to a basement.
    ///
    /// Opened deliberately rather than by walking into it. A trigger would mean a player backing
    /// away from something loses the map they are standing in, and in a game where the two of them
    /// have to stay in touch, leaving should be a decision somebody made.
    ///
    /// It can be locked, which is the hook the game needs later: Ashburn.MD has doors that do not
    /// open until the power is on, and doors one player can open for the other.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class MapDoor : MonoBehaviour, IInteractable
    {
        [Header("Where it goes")]
        [Tooltip("Scene name of the map on the other side. It must be in the build's scene list " +
                 "or the load fails at runtime.")]
        [SerializeField] string targetMap;

        [Tooltip("Id of the MapEntry to arrive at, over in that scene. Empty uses its spawn points.")]
        [SerializeField] string targetEntry = "Default";

        [Header("State")]
        [Tooltip("Shown while the player is in range.")]
        [SerializeField] string prompt = "Enter";

        [Tooltip("Shown instead while it is locked.")]
        [SerializeField] string lockedPrompt = "Locked";

        [SerializeField] bool locked;

        [Tooltip("Whether the building needs power before this will open.")]
        [SerializeField] bool needsPower;

        [Header("Noise")]
        [Tooltip("How far the door carries, in world units. A door is not a quiet way to leave.")]
        [SerializeField] float noiseRange = 9f;

        public string Prompt => IsOpenable ? prompt : lockedPrompt;

        /// <summary>Whether it would open right now.</summary>
        public bool IsOpenable
        {
            get
            {
                if (locked)
                    return false;

                return !needsPower || (PowerGrid.Current != null && PowerGrid.Current.IsPowered);
            }
        }

        /// <summary>Locks or unlocks it, for a key found elsewhere or a scripted moment.</summary>
        public void SetLocked(bool value) => locked = value;

        // The prompt still shows while locked, so the player learns the door exists and that it is
        // the door stopping them rather than the level having no way on.
        public bool CanInteract(GameObject interactor) => !MapTravel.IsTravelling;

        public void Interact(GameObject interactor)
        {
            if (MapTravel.IsTravelling)
                return;

            NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self);

            if (!IsOpenable)
                return;

            if (string.IsNullOrEmpty(targetMap))
            {
                Debug.LogWarning($"{nameof(MapDoor)} on '{name}' has no target map set.", this);
                return;
            }

            MapTravel.Go(targetMap, targetEntry);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = locked ? new Color(1f, 0.4f, 0.4f, 0.9f) : new Color(0.5f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
        }
    }
}
