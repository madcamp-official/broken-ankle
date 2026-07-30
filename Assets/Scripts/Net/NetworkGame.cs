using System.Collections;
using Ashburn.Player;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Ashburn.Net
{
    /// <summary>
    /// Connects, puts the two players in a room, and decides which of them is A and which is B.
    ///
    /// The slot number is the whole of it. Everything downstream — the spawn point, the point of
    /// view, whose input drives what — already keys off an index, so this is the only place that has
    /// to know a network exists in order for two people to play different halves of the game.
    ///
    /// Sits in the systems scene beside <see cref="PlayerSpawner"/>. Delete or disable it and the
    /// project falls back to the split-keyboard test with nothing else changed.
    /// </summary>
    public class NetworkGame : MonoBehaviourPunCallbacks
    {
        /// <summary>Key the slot number is published under, so any client can ask who is who.</summary>
        public const string SlotKey = "slot";

        [Header("Wiring")]
        [Tooltip("The spawner in this scene. Left empty it is looked for.")]
        [SerializeField] PlayerSpawner spawner;

        [Header("Room")]
        [Tooltip("The room both players join. A fixed name is enough for two people who already " +
                 "agreed to play; a room code would go here.")]
        [SerializeField] string roomName = "ashburn";

        [Tooltip("Clients with different versions never meet. Bump it when a build stops being " +
                 "compatible with the one your partner has — a changed PlayerSync packet above all, " +
                 "since that stream is read by position and a stale client reads the wrong fields " +
                 "out of it without anything reporting an error.")]
        [SerializeField] string gameVersion = "2";

        int _slot = -1;

        /// <summary>Which player this machine is. 0 is A, 1 is B. -1 before the room is joined.</summary>
        public int Slot => _slot;

        void Awake()
        {
            if (spawner == null)
                spawner = FindAnyObjectByType<PlayerSpawner>();

            if (spawner == null)
            {
                Debug.LogError($"{nameof(NetworkGame)} on '{name}' found no " +
                               $"{nameof(PlayerSpawner)}, so there is nothing to spawn into.", this);
                return;
            }

            // In Awake because the fill it stops is in the spawner's Start, and every Awake runs
            // before any Start whatever order the two objects happen to be in.
            spawner.HandOverToNetwork();

            // Maps are loaded additively and counted per character, which PUN's scene syncing knows
            // nothing about: it would load the starting map a second time on the joining client.
            // Each machine opens its own maps, exactly as it does offline.
            PhotonNetwork.AutomaticallySyncScene = false;
        }

        void Start()
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }

        public override void OnConnectedToMaster()
        {
            // Two, hard. A third player would get a slot number nothing has a spawn point for.
            var options = new RoomOptions { MaxPlayers = 2 };
            PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);
        }

        public override void OnJoinedRoom()
        {
            // Whoever is master at this moment is A. Read once and then published, rather than
            // asked for whenever it is wanted: master is transferred when the host leaves, and a
            // role that flips underneath a player mid-game is worse than a host that has gone.
            _slot = PhotonNetwork.IsMasterClient ? 0 : 1;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { SlotKey, _slot } });

            StartCoroutine(SpawnWhenMapIsUp());
        }

        /// <summary>
        /// Creates this machine's character, and only this machine's.
        ///
        /// PUN makes the caller the owner, which is what the player wants for their own character
        /// and exactly what must not happen to their partner's. So each side creates one and the
        /// other side receives it.
        /// </summary>
        IEnumerator SpawnWhenMapIsUp()
        {
            // The spawner opens the starting map in its own Start. Creating anybody before it is up
            // would put them at the origin, and on the joining client also before there is a
            // MapZone to hand them.
            while (spawner != null && spawner.Zone == null)
                yield return null;

            if (spawner == null)
                yield break;

            // Which character this slot plays as is the spawner's to say, so the offline test and
            // the networked game cannot drift apart on it. PUN takes a name rather than a reference,
            // which is why these prefabs have to sit directly in a Resources folder.
            var prefab = spawner.PrefabFor(_slot);
            if (prefab == null)
                yield break;

            // The position crosses the wire as world coordinates, and a map's world position is the
            // slot it claimed on load. Both clients load the starting map first and both give it
            // slot zero, so these agree. It stops being true for maps opened later — see the note
            // in PlayerRoles.MD.
            PhotonNetwork.Instantiate(prefab.name, spawner.PositionFor(_slot), Quaternion.identity,
                                      0, new object[] { _slot });
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            // Nearly always the room already holding two people.
            Debug.LogError($"Could not join the room '{roomName}': {message} ({returnCode}). If " +
                           "somebody is already playing with somebody else, this is why.", this);
        }

        public override void OnCreateRoomFailed(short returnCode, string message) =>
            Debug.LogError($"Could not create the room '{roomName}': {message} ({returnCode}).", this);

        public override void OnDisconnected(DisconnectCause cause) =>
            Debug.LogWarning($"{nameof(NetworkGame)} disconnected: {cause}. Nobody will be spawned. " +
                             "A missing App ID reports itself separately, from PhotonAppIds.", this);
    }
}
