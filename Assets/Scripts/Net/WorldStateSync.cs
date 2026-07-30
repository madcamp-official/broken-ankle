using System.Collections.Generic;
using Ashburn.World;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Ashburn.Net
{
    /// <summary>
    /// Keeps the doors the pair have opened, and the keys they are carrying, the same on both
    /// machines.
    ///
    /// A room property rather than an RPC, which is <c>Multiplayer.MD</c> 6절's rule and the whole
    /// reason <see cref="WorldState"/> holds flags instead of raising events. An RPC is something
    /// that happened, so a player who was disconnected, or who joined a minute later, never hears
    /// it and walks up to a door the other one opened and finds it shut. A property is something
    /// that is true, and the server hands it to whoever arrives.
    ///
    /// The only file in the keys-and-doors path that knows Photon exists. Everything else is a set
    /// of strings and works with nothing connected.
    /// </summary>
    public class WorldStateSync : MonoBehaviourPunCallbacks
    {
        /// <summary>Key the flags are published under in the room's properties.</summary>
        public const string RoomKey = "world";

        const string FlagPrefix = "world:";
        const char Separator = '\n';

        // Announcing a flag that arrived from the room raises the same event a local one does, and
        // without this the room would be written again for state it just handed us.
        bool _adopting;

        public override void OnEnable()
        {
            base.OnEnable();
            WorldState.Set += Publish;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            WorldState.Set -= Publish;
        }

        public override void OnJoinedRoom() => Adopt(PhotonNetwork.CurrentRoom.CustomProperties);

        public override void OnRoomPropertiesUpdate(Hashtable changed) => Adopt(changed);

        /// <summary>
        /// Publishes an immutable property per flag. Two players completing different tasks in the
        /// same frame therefore write different keys instead of replacing one shared string.
        /// RoomKey is retained for compatibility with rooms created by an older build.
        /// </summary>
        void Publish(string flag)
        {
            if (_adopting || !PhotonNetwork.InRoom)
                return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { FlagPrefix + flag, true },
                { RoomKey, string.Join(Separator.ToString(), WorldState.All) },
            });
        }

        void Adopt(Hashtable properties)
        {
            if (properties == null)
                return;

            // Accept rather than Raise: these came from the room, so echoing them back would be a
            // write per flag per machine for state everybody already has.
            var arrived = new List<string>();
            if (properties.TryGetValue(RoomKey, out var value) &&
                value is string packed &&
                !string.IsNullOrEmpty(packed))
            {
                foreach (var flag in packed.Split(Separator))
                    Accept(flag, arrived);
            }

            foreach (System.Collections.DictionaryEntry pair in properties)
            {
                if (pair.Key is not string key ||
                    !key.StartsWith(FlagPrefix, System.StringComparison.Ordinal) ||
                    pair.Value is not bool set || !set)
                {
                    continue;
                }

                Accept(key.Substring(FlagPrefix.Length), arrived);
            }

            // Told after the whole set is in, so a door that opens because of one flag can already
            // see the rest — a door needing a key that arrived in the same packet must not read the
            // keyring half filled.
            _adopting = true;
            foreach (var flag in arrived)
                WorldState.Announce(flag);
            _adopting = false;
        }

        static void Accept(string flag, List<string> arrived)
        {
            if (string.IsNullOrEmpty(flag) || WorldState.Has(flag))
                return;

            WorldState.Accept(flag);
            arrived.Add(flag);
        }
    }
}
