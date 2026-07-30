using System.Collections;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Player
{
    /// <summary>
    /// Puts characters into the level.
    ///
    /// Replaces the hand-placed Player and Ally, which could not survive networking: a character
    /// that already exists in the scene cannot be handed an owner. Here every character is created
    /// the same way and told its role on the spot, so the only thing networking has to change is
    /// who calls <see cref="Spawn"/>.
    ///
    /// With no network it fills the level itself, which is also how the split-keyboard two-player
    /// test runs. With one, <c>NetworkGame</c> does the creating and every character — the local one
    /// and the partner PUN builds from the wire — still comes back through <see cref="Configure"/>.
    /// The two paths differ by which Instantiate is called and by nothing else.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("What to spawn")]
        [Tooltip("Character prefab per player, in order: the first is A, the second B. They must " +
                 "sit directly in a Resources folder, because a networked game creates them by " +
                 "name and that is the only place PUN looks. Fewer entries than players is allowed " +
                 "and gives everybody the first one.")]
        [SerializeField] GameObject[] playerPrefabs;

        [Tooltip("Action map per player, in order. The second entry is the split-keyboard player.")]
        [SerializeField] string[] actionMaps = { "Player", "Player2" };

        [Header("Where the game starts")]
        [Tooltip("Scene name of the map both players open in. It is loaded before anyone is " +
                 "created, and must be in File > Build Profiles > Scene List.")]
        [SerializeField] string startingMap = "Street";

        [Tooltip("MapEntry in that map to start at. Empty uses the map's own spawn points.")]
        [SerializeField] string startingEntry = "";

        [Header("Offline")]
        [Tooltip("Characters to create when nothing else does it. Two runs the local co-op test. " +
                 "Ignored once a network game has taken over.")]
        [SerializeField] int offlinePlayers = 2;

        [Tooltip("Which of them the screen follows.")]
        [SerializeField] int offlineViewerIndex;

        MapZone _zone;
        bool _networked;
        int _characters;

        /// <summary>
        /// The map players are put into, once the starting map is up.
        ///
        /// Looked up again when the one being held has been destroyed. The starting map is unloaded
        /// like any other the moment the last person walks out of it, and the reference left behind
        /// is a destroyed object — which is not null enough to fail a null check but is null enough
        /// that everything handed it reports having no map at all.
        /// </summary>
        public MapZone Zone
        {
            get
            {
                if (_zone == null)
                    _zone = MapZone.Find(startingMap);

                return _zone;
            }
        }

        /// <summary>Scene name of the map the game opens in.</summary>
        public string StartingMap => startingMap;

        /// <summary>
        /// Stops the offline fill, because something else is doing the creating.
        ///
        /// Called from <c>NetworkGame.Awake</c>, which is early enough on purpose: every Awake runs
        /// before any Start, and the fill is in Start. One switch rather than two — a networked game
        /// left beside Offline Players 2 would put four characters in the map, and the two settings
        /// would have to be kept in agreement by hand forever.
        /// </summary>
        public void HandOverToNetwork() => _networked = true;

        IEnumerator Start()
        {
            // This object lives in the systems scene, which holds no level at all. Nobody can be
            // created until there is a map for them to stand in.
            yield return MapLoader.Acquire(startingMap);

            _zone = MapZone.Find(startingMap);
            if (_zone == null)
            {
                Debug.LogError($"{nameof(PlayerSpawner)} could not open the starting map " +
                               $"'{startingMap}', so there is nowhere to put anybody.", this);
                yield break;
            }

            // Networking creates on its own schedule: it has nobody to create until the room is
            // joined and the slot numbers are settled, which is long after this.
            if (_networked)
                yield break;

            // Says so rather than doing nothing quietly. An empty level used to look identical
            // whether this was set to zero or the spawner had never run at all.
            if (offlinePlayers <= 0)
            {
                Debug.LogWarning($"{nameof(PlayerSpawner)} on '{name}' has Offline Players set to " +
                                 $"{offlinePlayers} and no network game has claimed it, so it " +
                                 "creates nobody. Set it to 2 for the local two-player test.", this);
                yield break;
            }

            for (var i = 0; i < offlinePlayers; i++)
                Spawn(i, viewer: i == offlineViewerIndex, controlled: true);
        }

        /// <summary>
        /// Creates one character on this machine alone and tells it what it is.
        ///
        /// The offline path. A networked game does not come through here — PUN builds the character
        /// on both machines and it configures itself — but both end up in <see cref="Configure"/>.
        /// </summary>
        public GameObject Spawn(int index, bool viewer, bool controlled)
        {
            var prefab = PrefabFor(index);
            if (prefab == null)
                return null;

            var character = Instantiate(prefab, PositionFor(index), Quaternion.identity);
            Configure(character, index, viewer, controlled, splitKeyboard: true);
            return character;
        }

        /// <summary>
        /// The character player <paramref name="index"/> plays as.
        ///
        /// Public because the networked path does not create anybody through <see cref="Spawn"/>
        /// and still has to know which prefab a slot means. Keeping the list here rather than
        /// duplicating it there means the two players are described in one place, and a machine
        /// cannot end up creating a character its partner has never heard of.
        /// </summary>
        public GameObject PrefabFor(int index)
        {
            if (playerPrefabs == null || playerPrefabs.Length == 0)
            {
                Debug.LogError($"{nameof(PlayerSpawner)} on '{name}' has no player prefabs.", this);
                return null;
            }

            // Clamped rather than refused: one prefab for both players is how this looked before A
            // and B were told apart, and it is still a reasonable thing to want.
            var prefab = playerPrefabs[Mathf.Clamp(index, 0, playerPrefabs.Length - 1)];
            if (prefab == null)
                Debug.LogError($"{nameof(PlayerSpawner)} on '{name}' has an empty slot in its " +
                               $"player prefabs, so player {index} has no character.", this);

            return prefab;
        }

        /// <summary>
        /// Tells a character that already exists what it is.
        ///
        /// Split out from <see cref="Spawn"/> because a networked partner is built by PUN on the
        /// receiving machine, where nothing called Spawn at all. That copy still needs every one of
        /// these steps, and it is the copy that has to come out as a partner rather than as the
        /// viewer, so both paths end here.
        /// </summary>
        /// <param name="index">Which player this is. Picks the spawn point, and the role with it.</param>
        /// <param name="splitKeyboard">
        /// Whether <paramref name="index"/> also picks the action map. True for two players sharing
        /// one keyboard. False over the network, where each has a keyboard to themselves and the
        /// second map's half-keyboard bindings would strand player B.
        /// </param>
        public void Configure(GameObject character, int index, bool viewer, bool controlled,
                              bool splitKeyboard)
        {
            if (character == null)
            {
                Debug.LogError($"{nameof(PlayerSpawner)} on '{name}' was asked to configure " +
                               "nobody.", this);
                return;
            }

            // Named after the person rather than the slot, so the hierarchy reads as Nathan and
            // Grant. Which of them is at this keyboard is on the tag, set by PlayerRig, and that is
            // what the camera and the hearing ring ask for — nothing here looks a character up by
            // name.
            var prefab = PrefabFor(index);
            character.name = prefab != null ? prefab.name : $"Player {index + 1}";

            // Which of the two is carrying what, told for the same reason the map is: a networked
            // partner is built by PUN on the receiving machine, where nothing called Spawn and
            // nothing else knows which player the copy is meant to be.
            var pockets = character.GetComponent<Interaction.Inventory>();
            if (pockets != null)
                pockets.SetSlot(index);

            // Which map this character is in, told rather than worked out. Everything that must not
            // cross a map — the noise bus, the power, the darkness — reads it from here.
            var presence = character.GetComponent<MapPresence>();
            if (presence != null)
                presence.Enter(Zone);
            else
                Debug.LogError($"'{character.name}' has no {nameof(MapPresence)}, so it will " +
                               "neither hear anything nor be heard.", character);

            HoldStartingMap();

            // Before the rig, because switching maps disables and re-enables the input components
            // and the rig decides whether they should be on at all.
            var map = splitKeyboard ? index : 0;
            if (map < actionMaps.Length)
                foreach (var user in character.GetComponentsInChildren<IUsesActionMap>(true))
                    user.UseActionMap(actionMaps[map]);

            var rig = character.GetComponent<PlayerRig>();
            if (rig != null)
                rig.Apply(viewer, controlled);
            else
                Debug.LogError($"'{character.name}' has no {nameof(PlayerRig)}.", character);
        }

        /// <summary>
        /// Takes one hold on the starting map for a character that has just been put into it.
        ///
        /// The hold taken in <see cref="Start"/> stands in for the first of them, so the count ends
        /// up matching the number of people standing there and the map is let go of exactly when the
        /// last one walks out — <see cref="MapTravel"/> releases one hold per traveller.
        /// </summary>
        void HoldStartingMap()
        {
            if (_characters++ > 0)
                StartCoroutine(MapLoader.Acquire(startingMap));
        }

        /// <summary>
        /// Where character <paramref name="index"/> starts.
        ///
        /// The points come from the map, not from here: this object outlives every map and cannot
        /// hold a reference into one. A named entry wins when there is one, so a game can open on
        /// the players walking in through a particular door.
        /// </summary>
        public Vector3 PositionFor(int index)
        {
            if (!string.IsNullOrEmpty(startingEntry))
            {
                var entry = MapEntry.Find(startingEntry, Zone);
                if (entry != null)
                    return entry.PointFor(index);

                Debug.LogWarning($"Map '{startingMap}' has no entry called '{startingEntry}'. " +
                                 "Using its spawn points instead.", this);
            }

            var zone = Zone;
            var points = zone != null ? zone.GetComponentsInChildren<SpawnPoint>(true) : null;
            if (points == null || points.Length == 0)
            {
                Debug.LogWarning($"Map '{startingMap}' has no {nameof(SpawnPoint)} in it, so " +
                                 "everybody starts on its origin, most likely inside a wall.", this);
                return zone != null ? zone.transform.position : transform.position;
            }

            return points[Mathf.Clamp(index, 0, points.Length - 1)].transform.position;
        }

    }
}
