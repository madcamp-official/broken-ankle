using System;
using System.Collections.Generic;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Interaction
{
    /// <summary>
    /// What one character is carrying.
    ///
    /// Kept in <see cref="WorldState"/> under this character's slot rather than in a list on the
    /// component, which buys three things and costs nothing: it crosses the wire through
    /// <c>WorldStateSync</c> with no new networking code, it survives the character walking into
    /// another map, and a player who joins late is handed it by the room along with everything else.
    /// A plain list here would be none of those, and the keycard would be in a pocket only one
    /// machine knew about.
    ///
    /// Whose pocket it is in is a record, not a rule. A door asks <see cref="WorldState.HasKey"/>,
    /// which is whether <em>either</em> of them has it, because the alternative is the bug the comment
    /// in <see cref="WorldState"/> is about: both of them have to get through the door, and a keycard
    /// in the partner's pocket while they are three rooms apart is a lock with no key in the building.
    /// So this is here to tell the players who found what, not to strand one of them.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [Tooltip("Which player this is. 0 is A, 1 is B. Set by PlayerSpawner when the character is " +
                 "configured, on both machines, so it does not have to be right in the prefab.")]
        [SerializeField] int slot;

        [Tooltip("Shown in the menu instead of the object's name. Empty uses the object's name.")]
        [SerializeField] string ownerName;

        /// <summary>Raised whenever anybody's pockets change. The menu redraws off this.</summary>
        public static event Action Changed;

        static readonly List<Inventory> _all = new();

        /// <summary>Every character on this machine that has pockets.</summary>
        public static IReadOnlyList<Inventory> All => _all;

        /// <summary>Which player this is. 0 is A, 1 is B.</summary>
        public int Slot => slot;

        /// <summary>The name to show above this character's items.</summary>
        public string OwnerName => string.IsNullOrEmpty(ownerName) ? name : ownerName;

        /// <summary>The pockets of the character that just did something, or null.</summary>
        public static Inventory Of(GameObject who) =>
            who == null ? null : who.GetComponentInParent<Inventory>();

        void OnEnable()
        {
            _all.Add(this);
            WorldState.Set += OnFlagSet;
        }

        void OnDisable()
        {
            _all.Remove(this);
            WorldState.Set -= OnFlagSet;
        }

        void OnFlagSet(string flag)
        {
            // Anybody's pickup changes what the menu should show, including the partner's arriving
            // from the room: the 소지품 tab lists both columns.
            if (flag != null && flag.StartsWith("carry:", StringComparison.Ordinal))
                Changed?.Invoke();
        }

        /// <summary>
        /// Tells this character which player it is.
        ///
        /// Called from <see cref="Ashburn.Player.PlayerSpawner.Configure"/>, which is the one place
        /// the offline test and the networked spawn both pass through, so a character cannot end up
        /// carrying things into the other player's column.
        /// </summary>
        public void SetSlot(int value)
        {
            if (slot == value)
                return;

            slot = value;
            Changed?.Invoke();
        }

        /// <summary>Whether this character in particular has it.</summary>
        public bool Has(string item) =>
            !string.IsNullOrEmpty(item) && WorldState.Has(WorldState.CarryFlag(slot, item));

        /// <summary>
        /// Puts something in this character's pockets.
        ///
        /// Two flags, and they are different things. One says this character picked it up, which is
        /// what the menu reads; the other says the pair are carrying it, which is what a door reads.
        /// A second copy of the same keycard found elsewhere sets the same second flag, and the door
        /// does not care which of them found it.
        /// </summary>
        public void Take(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                Debug.LogError($"{nameof(Inventory)} on '{name}' was handed an item with no name.",
                               this);
                return;
            }

            WorldState.Raise(WorldState.KeyFlag(item));
            WorldState.Raise(WorldState.CarryFlag(slot, item));
        }

        /// <summary>
        /// Everything this character is carrying, in no particular order.
        ///
        /// A scan of the whole world state, so it is for the menu and not for anything that runs
        /// every frame. Allocates a list rather than yielding, because the caller is drawing a
        /// column and wants to know how tall it is first.
        /// </summary>
        public List<string> Items()
        {
            var prefix = WorldState.CarryPrefix(slot);
            var items = new List<string>();

            foreach (var flag in WorldState.All)
                if (flag.StartsWith(prefix, StringComparison.Ordinal))
                    items.Add(flag.Substring(prefix.Length));

            items.Sort(StringComparer.Ordinal);
            return items;
        }

        // The event is static and the list of characters outlives a play session when the editor
        // skips its domain reload, which would leave the menu drawing off a character that no longer
        // exists and listeners from the last run still attached.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _all.Clear();
            Changed = null;
        }
    }
}
