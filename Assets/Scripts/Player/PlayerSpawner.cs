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
    /// test runs.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("What to spawn")]
        [SerializeField] GameObject playerPrefab;

        [Tooltip("Action map per player, in order. The second entry is the split-keyboard player.")]
        [SerializeField] string[] actionMaps = { "Player", "Player2" };

        [Header("Where the game starts")]
        [Tooltip("Scene name of the map both players open in. It is loaded before anyone is " +
                 "created, and must be in File > Build Profiles > Scene List.")]
        [SerializeField] string startingMap = "Street";

        [Tooltip("MapEntry in that map to start at. Empty uses the map's own spawn points.")]
        [SerializeField] string startingEntry = "";

        [Header("Offline")]
        [Tooltip("Characters to create when nothing else does it. Two runs the local co-op test; " +
                 "zero waits for a network spawn.")]
        [SerializeField] int offlinePlayers = 2;

        [Tooltip("Which of them the screen follows.")]
        [SerializeField] int offlineViewerIndex;

        MapZone _zone;

        /// <summary>The map players are put into. Set once the starting map is up.</summary>
        public MapZone Zone => _zone;

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

            // Says so rather than doing nothing quietly. An empty level used to look identical
            // whether this was set to zero or the spawner had never run at all.
            if (offlinePlayers <= 0)
            {
                Debug.LogWarning($"{nameof(PlayerSpawner)} on '{name}' has Offline Players set to " +
                                 $"{offlinePlayers}, so it creates nobody. Set it to 2 for the " +
                                 "local two-player test.", this);
                yield break;
            }

            // One hold for the map itself, then one per player, so the map is only let go of once
            // the last of them has walked out of it.
            for (var i = 1; i < offlinePlayers; i++)
                yield return MapLoader.Acquire(startingMap);

            for (var i = 0; i < offlinePlayers; i++)
                Spawn(i, viewer: i == offlineViewerIndex, controlled: true);
        }

        /// <summary>
        /// Creates one character and tells it what it is.
        ///
        /// Networking calls this once per player with <paramref name="viewer"/> and
        /// <paramref name="controlled"/> both set from ownership.
        /// </summary>
        public GameObject Spawn(int index, bool viewer, bool controlled)
        {
            if (playerPrefab == null)
            {
                Debug.LogError($"{nameof(PlayerSpawner)} on '{name}' has no player prefab.", this);
                return null;
            }

            var character = Instantiate(playerPrefab, PositionFor(index), Quaternion.identity);
            character.name = viewer ? "Player" : $"Player {index + 1}";

            // Which map this character is in, told rather than worked out. Everything that must not
            // cross a map — the noise bus, the power, the darkness — reads it from here.
            var presence = character.GetComponent<MapPresence>();
            if (presence != null)
                presence.Enter(_zone);
            else
                Debug.LogError($"'{playerPrefab.name}' has no {nameof(MapPresence)}, so it will " +
                               "neither hear anything nor be heard.", character);

            // Before the rig, because switching maps disables and re-enables the input components
            // and the rig decides whether they should be on at all.
            if (index < actionMaps.Length)
                foreach (var user in character.GetComponentsInChildren<IUsesActionMap>(true))
                    user.UseActionMap(actionMaps[index]);

            var rig = character.GetComponent<PlayerRig>();
            if (rig != null)
                rig.Apply(viewer, controlled);
            else
                Debug.LogError($"'{playerPrefab.name}' has no {nameof(PlayerRig)}.", character);

            return character;
        }

        /// <summary>
        /// Where character <paramref name="index"/> starts.
        ///
        /// The points come from the map, not from here: this object outlives every map and cannot
        /// hold a reference into one. A named entry wins when there is one, so a game can open on
        /// the players walking in through a particular door.
        /// </summary>
        Vector3 PositionFor(int index)
        {
            if (!string.IsNullOrEmpty(startingEntry))
            {
                var entry = MapEntry.Find(startingEntry, _zone);
                if (entry != null)
                    return entry.PointFor(index);

                Debug.LogWarning($"Map '{startingMap}' has no entry called '{startingEntry}'. " +
                                 "Using its spawn points instead.", this);
            }

            var points = _zone != null ? _zone.GetComponentsInChildren<SpawnPoint>(true) : null;
            if (points == null || points.Length == 0)
            {
                Debug.LogWarning($"Map '{startingMap}' has no {nameof(SpawnPoint)} in it, so " +
                                 "everybody starts on its origin, most likely inside a wall.", this);
                return _zone != null ? _zone.transform.position : transform.position;
            }

            return points[Mathf.Clamp(index, 0, points.Length - 1)].transform.position;
        }

    }
}
