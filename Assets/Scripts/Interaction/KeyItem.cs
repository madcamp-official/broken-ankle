using Ashburn.World;
using UnityEngine;

namespace Ashburn.Interaction
{
    /// <summary>
    /// Something lying in the world that a door asks for: a keycard, a fuse, the elevator's motor.
    ///
    /// It goes into the pockets of whoever bent down for it — see <see cref="Inventory"/> — so the
    /// two of them can see who found what. Whose pockets it is in does not decide who may open the
    /// door, though: both of them are meant to get through, and a keycard in the wrong partner's
    /// hands while they are three rooms apart is a lock with no key in the building.
    ///
    /// The pickup remembers being taken under its own name rather than the key's, so two copies of
    /// the same keycard in different buildings do not vanish together, and a player arriving later
    /// does not find one lying there that has already been used.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class KeyItem : MonoBehaviour, IInteractable
    {
        [Header("What it is")]
        [Tooltip("Name a LockedDoor asks for. 'SentilKeycard', 'ElevatorMotor'.")]
        [SerializeField] string key;

        [Tooltip("Unique within the whole game — it is what remembers this one has been taken. " +
                 "Empty uses the object's name.")]
        [SerializeField] string id;

        [Header("Prompt")]
        [SerializeField] string prompt = "줍는다";

        [Header("When taken")]
        [Tooltip("Turned off once it has been picked up. Usually the sprite. This object's own " +
                 "collider is switched off as well and needs no entry here.")]
        [SerializeField] GameObject[] hideWhenTaken;

        Collider2D _reach;

        /// <summary>Whether somebody has already picked this one up.</summary>
        public bool IsTaken => WorldState.Has(Flag);

        string Flag => "took:" + (string.IsNullOrEmpty(id) ? name : id);

        public string Prompt => prompt;

        void Awake()
        {
            _reach = GetComponent<Collider2D>();

            if (string.IsNullOrEmpty(key))
                Debug.LogError($"{nameof(KeyItem)} on '{name}' opens nothing: it has no key name.",
                               this);
        }

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;

            if (IsTaken)
                Take(silent: true);
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string flag)
        {
            if (flag == Flag)
                Take(silent: false);
        }

        public bool CanInteract(GameObject interactor) => !IsTaken;

        public void Interact(GameObject interactor)
        {
            if (IsTaken)
                return;

            // Into the pockets of whoever picked it up, which records both that this character has
            // it and that the pair do. A second keycard elsewhere sets the same second flag, and the
            // door does not care which of them was found.
            var pockets = Inventory.Of(interactor);
            if (pockets != null)
            {
                pockets.Take(key);
            }
            else
            {
                // Taken by something with no pockets — a scripted grab, or a test rig. The pair
                // still have it, because that is the half a door reads; it just sits in nobody's
                // column in the menu.
                WorldState.Raise(WorldState.KeyFlag(key));
            }

            // Separate from either of those: this says the object on the floor is gone, under its
            // own id, so two copies of one keycard do not vanish together.
            WorldState.Raise(Flag);
        }

        void Take(bool silent)
        {
            if (_reach != null)
                _reach.enabled = false;

            foreach (var go in hideWhenTaken)
                if (go != null)
                    go.SetActive(false);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}
