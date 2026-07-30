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

        [Tooltip("Shown instead while it will not open. Say what is missing — a door that only says " +
                 "'Locked' sends the player back over ground they have already searched.")]
        [SerializeField] string lockedPrompt = "Locked";

        [Tooltip("Held shut by the story rather than by a lock, and no key opens it. For a door " +
                 "something else unlocks later through SetLocked; to gate it on an item, leave this " +
                 "off and name the item in Required Key instead.")]
        [SerializeField] bool locked;

        [Tooltip("Name of the item the pair must be carrying, matching the Key on a KeyItem. Empty " +
                 "needs nothing. Either player carrying it is enough: both of them have to get " +
                 "through, so a key in the partner's pocket must not strand the other one.")]
        [SerializeField] string requiredKey;

        [Tooltip("Whether the building needs power before this will open.")]
        [SerializeField] bool needsPower;

        [Header("Noise")]
        [Tooltip("How far the door carries, in world units. A door is not a quiet way to leave.")]
        [SerializeField] float noiseRange = 9f;

        public string Prompt => IsOpenable ? prompt : lockedPrompt;

        /// <summary>The item this door wants, or empty if it wants nothing.</summary>
        public string RequiredKey => requiredKey;

        /// <summary>Whether it would open right now.</summary>
        public bool IsOpenable
        {
            get
            {
                if (locked)
                    return false;

                // Asked of the pair rather than of whoever is standing here. See the note on the
                // field: both of them are meant to get through, so the one without the keycard in
                // their own pocket is not the one to stop.
                if (!string.IsNullOrEmpty(requiredKey) && !WorldState.HasKey(requiredKey))
                    return false;

                if (!needsPower)
                    return true;

                // The map this door is in, not the one it leads to: a front door is unlocked by
                // the power in the street, not by the house behind it.
                var grid = PowerGrid.Of(this);
                return grid != null && grid.IsPowered;
            }
        }

        /// <summary>Locks or unlocks it, for a scripted moment. Not the key path.</summary>
        public void SetLocked(bool value) => locked = value;

        // The prompt still shows while locked, so the player learns the door exists and that it is
        // the door stopping them rather than the level having no way on. Asked per interactor:
        // one player being mid-travel is no reason the other cannot open a door of their own.
        public bool CanInteract(GameObject interactor) => !MapTravel.IsTravelling(interactor);

        public void Interact(GameObject interactor)
        {
            if (MapTravel.IsTravelling(interactor))
                return;

            // Loud, and heard by whoever is still in the map being left. Tagged with the door's map
            // rather than the traveller's, because the door is what made the sound and it is not
            // going anywhere.
            NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self, MapZone.IdOf(this));

            if (!IsOpenable)
                return;

            if (string.IsNullOrEmpty(targetMap))
            {
                Debug.LogWarning($"{nameof(MapDoor)} on '{name}' has no target map set.", this);
                return;
            }

            MapTravel.Go(interactor, targetMap, targetEntry);
        }

        void OnDrawGizmos()
        {
            // Drawn from what the door wants rather than from whether it would open this instant:
            // in the editor nobody is carrying anything, and every keyed door would read as shut.
            // Amber for keyed matches LockedDoor, which is the other half of the same idea.
            if (locked)
                Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);
            else if (!string.IsNullOrEmpty(requiredKey))
                Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.9f);
            else
                Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.9f);

            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
        }
    }
}
