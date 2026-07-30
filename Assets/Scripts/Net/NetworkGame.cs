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
        [Tooltip("The room both players join. Set by the title screen from the code the players " +
                 "agreed on; what is typed here is only used when nobody asks for anything else.")]
        [SerializeField] string roomName = "ashburn";


        [Tooltip("Clients with different versions never meet. Bump it when a build stops being " +
                 "compatible with the one your partner has — a changed PlayerSync packet above all, " +
                 "since that stream is read by position and a stale client reads the wrong fields " +
                 "out of it without anything reporting an error.")]
        [SerializeField] string gameVersion = "2";

        int _slot = -1;

        /// <summary>Which player this machine is. 0 is A, 1 is B. -1 before the room is joined.</summary>
        public int Slot => _slot;

        /// <summary>The room being joined or held, in whatever case the player typed it.</summary>
        public string RoomName => roomName;

        /// <summary>How far along the connection is, for a title screen to report.</summary>
        public enum Stage
        {
            /// <summary>Nothing has been asked for yet.</summary>
            Idle,

            /// <summary>Talking to Photon, or waiting for a room.</summary>
            Working,

            /// <summary>In the room. The partner may or may not have arrived.</summary>
            Joined,

            /// <summary>It did not work, and <see cref="Problem"/> says why.</summary>
            Failed,
        }

        /// <summary>Where the connection has got to.</summary>
        public Stage State { get; private set; } = Stage.Idle;

        /// <summary>
        /// What went wrong, in a sentence meant for the player rather than the console.
        ///
        /// A build has no console. Left as a log line, somebody who typed a code for a full room
        /// saw the map load with nobody in it and nothing to say why — which is what a crash looks
        /// like.
        /// </summary>
        public string Problem { get; private set; } = string.Empty;

        /// <summary>Whether this machine created the room rather than joining one.</summary>
        public bool IsHost { get; private set; }

        /// <summary>
        /// Makes a room with this code and waits in it, or joins one that already exists.
        ///
        /// Both halves are one call because Photon's own JoinOrCreate is: whoever presses first
        /// makes the room and the other one walks into it, and neither has to know which they were.
        /// </summary>
        public void Enter(string code)
        {
            if (!string.IsNullOrWhiteSpace(code))
                roomName = code.Trim();

            Problem = string.Empty;
            State = Stage.Working;

            // Already talking to the master server from a previous attempt — a code that was
            // refused, most often. Reconnecting would drop that and take longer than asking again.
            if (PhotonNetwork.IsConnectedAndReady)
            {
                JoinNamedRoom();
                return;
            }

            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }

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
            // A title screen is going to ask which room, so connecting now would take the choice
            // away from it. Asked of the scene rather than set by a checkbox: a flag that has to
            // agree with whether an object exists is a flag that will one day disagree, and the
            // symptom — everybody silently back in the same room — is one nobody would look for.
            if (FindAnyObjectByType<TitleScreen>() == null)
                Enter(roomName);
        }

        public override void OnConnectedToMaster() => JoinNamedRoom();

        void JoinNamedRoom()
        {
            // Two, hard. A third player would get a slot number nothing has a spawn point for, so
            // six people wanting to play are three rooms rather than one crowded one.
            var options = new RoomOptions { MaxPlayers = 2 };
            PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);
        }

        public override void OnJoinedRoom()
        {
            State = Stage.Joined;
            IsHost = PhotonNetwork.IsMasterClient;

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
            // Nearly always the room already holding two people. Said on screen as well as in the
            // log, because the player who needs to know is looking at a build.
            Fail(returnCode == ErrorCode.GameFull
                     ? $"'{roomName}' 방에 이미 두 사람이 있습니다."
                     : $"'{roomName}' 방에 들어가지 못했습니다. ({message})");
        }

        public override void OnCreateRoomFailed(short returnCode, string message) =>
            Fail($"'{roomName}' 방을 만들지 못했습니다. ({message})");

        public override void OnDisconnected(DisconnectCause cause)
        {
            // A disconnect after the room was joined is the partner's problem to notice, not a
            // failure of anything the title screen asked for.
            if (State == Stage.Joined)
            {
                Debug.LogWarning($"{nameof(NetworkGame)} disconnected: {cause}.", this);
                return;
            }

            Fail(cause == DisconnectCause.InvalidAuthentication
                     ? "App ID 가 없거나 잘못되었습니다. Multiplayer.MD 9절을 보세요."
                     : $"접속하지 못했습니다. ({cause})");
        }

        void Fail(string reason)
        {
            State = Stage.Failed;
            Problem = reason;
            Debug.LogError($"{nameof(NetworkGame)}: {reason}", this);
        }
    }
}
