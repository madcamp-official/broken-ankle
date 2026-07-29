using Ashburn.Player;
using Photon.Pun;
using UnityEngine;

namespace Ashburn.Net
{
    /// <summary>
    /// Tells a networked character which player it is, on every machine holding a copy of it.
    ///
    /// This cannot be left to whoever called <c>PhotonNetwork.Instantiate</c>. That call builds the
    /// character on the partner's machine too, and the code that made it never touches that copy —
    /// but the copy is precisely the one that has to come out as a partner rather than as the
    /// viewer. So the character configures itself, from the only two things PUN hands it: who owns
    /// it, and the slot number sent along with the instantiation.
    ///
    /// Everything it then does goes through <see cref="PlayerSpawner.Configure"/>, the same call the
    /// offline test uses. Nothing about being a player is decided here.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class NetworkPlayerBinder : MonoBehaviourPun
    {
        /// <summary>Which player this is. 0 is A, 1 is B. -1 before Start has read it.</summary>
        public int Slot { get; private set; } = -1;

        void Start()
        {
            // The offline test builds the same prefab with a plain Instantiate. That character has a
            // PhotonView, because the prefab does, but it was never created through PUN and the
            // spawner has already told it everything it needs. Configuring it again from here made
            // both players come out as an unowned player A: no input, no camera, and two extra holds
            // on the starting map. An instantiation id is exactly the thing PUN sets and Unity does
            // not, so it is the one honest way to tell the two apart.
            if (photonView.InstantiationId == 0)
            {
                enabled = false;
                return;
            }

            Slot = ReadSlot();

            var spawner = FindAnyObjectByType<PlayerSpawner>();
            if (spawner == null)
            {
                Debug.LogError($"{nameof(NetworkPlayerBinder)} on '{name}' found no " +
                               $"{nameof(PlayerSpawner)}, so this character has no map and no role.",
                               this);
                return;
            }

            // Both come from ownership, and over the network both are always the same: the character
            // you drive is the one you look through. They are separate for the split-keyboard test,
            // where the second player is controlled here without the screen belonging to them.
            var mine = photonView.IsMine;

            // splitKeyboard is false: each player has a keyboard to themselves, so both use the
            // first action map. Picking it by slot would hand player B the half-keyboard bindings
            // meant for two people sitting at one desk.
            spawner.Configure(gameObject, Slot, viewer: mine, controlled: mine, splitKeyboard: false);
        }

        /// <summary>
        /// The slot this character was created for.
        ///
        /// Read from the instantiation data rather than from the owner's properties, because it
        /// arrives welded to the object: a custom property is replicated separately and can still be
        /// on its way when this runs, which would put a character at the wrong spawn point for as
        /// long as it took to arrive.
        /// </summary>
        int ReadSlot()
        {
            var data = photonView.InstantiationData;
            if (data != null && data.Length > 0 && data[0] is int slot)
                return slot;

            // The properties are the fallback rather than the source, and reaching here at all means
            // somebody created this character without saying who it is.
            var owner = photonView.Owner;
            if (owner != null && owner.CustomProperties.TryGetValue(NetworkGame.SlotKey, out var value)
                && value is int published)
            {
                Debug.LogWarning($"'{name}' was created without a slot number. Falling back on the " +
                                 $"owner's published slot ({published}).", this);
                return published;
            }

            Debug.LogError($"'{name}' has no slot number and its owner has not published one, so " +
                           "it does not know which player it is. It will use player A's spawn " +
                           "point, which is very likely somebody else's.", this);
            return 0;
        }
    }
}
