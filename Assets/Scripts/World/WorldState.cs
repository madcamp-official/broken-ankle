using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// The things the two of them have done that stay done: a keycard picked up, a door opened.
    ///
    /// Flags rather than events, because <c>Multiplayer.MD</c> 6절 makes the distinction that
    /// matters here — a door being open is not something that happened, it is something that is
    /// true. An event is missed by whoever was not listening at the time, and a player who walks
    /// into the building a minute later has to find that door open.
    ///
    /// Kept as a plain set with no idea a network exists. <c>WorldStateSync</c> mirrors it into the
    /// room so both machines agree; with nothing connected this is simply the whole truth.
    ///
    /// Shared between the two players rather than held per character. Both of them are meant to get
    /// through the door the keycard opens, and a key in the wrong partner's pocket while they are
    /// three rooms apart is a lock with no key in the building.
    /// </summary>
    public static class WorldState
    {
        static readonly HashSet<string> _set = new();

        /// <summary>Raised when a flag is newly set, with its name. The sync layer listens.</summary>
        public static event Action<string> Set;

        /// <summary>Everything true right now. Used to hand the whole state to a late arrival.</summary>
        public static IReadOnlyCollection<string> All => _set;

        /// <summary>Whether this has happened.</summary>
        public static bool Has(string flag) => !string.IsNullOrEmpty(flag) && _set.Contains(flag);

        /// <summary>
        /// Records that it has. Raises <see cref="Set"/> only the first time, so the sync layer does
        /// not echo a flag that arrived from the other machine straight back at it.
        /// </summary>
        public static void Raise(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !_set.Add(flag))
                return;

            Set?.Invoke(flag);
        }

        /// <summary>
        /// Takes a flag that arrived from somewhere else, without telling anybody yet.
        ///
        /// Silent because the sync layer wants to put a whole batch in before anything reacts: a
        /// door opened by a key that arrived in the same packet must not read the keyring while it
        /// is still half filled. Follow with <see cref="Announce"/>.
        /// </summary>
        public static void Accept(string flag)
        {
            if (!string.IsNullOrEmpty(flag))
                _set.Add(flag);
        }

        /// <summary>Tells everything about a flag already put in by <see cref="Accept"/>.</summary>
        public static void Announce(string flag)
        {
            if (!string.IsNullOrEmpty(flag))
                Set?.Invoke(flag);
        }

        /// <summary>The flag naming a key somebody is carrying.</summary>
        public static string KeyFlag(string key) => "key:" + key;

        /// <summary>Whether the pair has this key on them.</summary>
        public static bool HasKey(string key) => !string.IsNullOrEmpty(key) && Has(KeyFlag(key));

        /// <summary>
        /// The flag naming which of the two picked something up.
        ///
        /// Separate from <see cref="KeyFlag"/> because they answer different questions, and only one
        /// of them is allowed to be slow. A door asks "do they have it" every frame a player stands
        /// in front of it and gets an O(1) set lookup; the menu asks "whose pocket is it in" only
        /// when somebody opens it, and pays a scan for the answer. Folding the two into one flag
        /// would put that scan in the door's prompt.
        /// </summary>
        public static string CarryFlag(int slot, string item) => "carry:" + slot + ":" + item;

        /// <summary>The start of every <see cref="CarryFlag"/> belonging to one player.</summary>
        public static string CarryPrefix(int slot) => "carry:" + slot + ":";

        /// <summary>
        /// Forgets everything, for a run that is starting over.
        ///
        /// Reloading the scene is not enough on its own: this lives in a static, so without it a
        /// second attempt would begin with every door already unlocked by the first.
        ///
        /// The listeners go too. They belong to objects the reload is about to destroy, and a flag
        /// raised afterwards would be delivered to a door that no longer exists.
        /// </summary>
        public static void Clear()
        {
            _set.Clear();
            Set = null;
        }

        // Statics outlive a play session when the editor skips its domain reload, and a door left
        // open from the last run would be open before anybody had found its key.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Clear();
    }
}
