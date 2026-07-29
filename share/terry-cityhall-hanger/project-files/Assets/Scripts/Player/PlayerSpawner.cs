using UnityEngine;
using Ashburn.Interaction;

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

        [Tooltip("One per player, in order. Running out of points reuses the last one.")]
        [SerializeField] Transform[] spawnPoints;

        [Tooltip("Action map per player, in order. The second entry is the split-keyboard player.")]
        [SerializeField] string[] actionMaps = { "Player", "Player2" };

        [Header("Offline")]
        [Tooltip("Characters to create when nothing else does it. Two runs the local co-op test; " +
                 "zero waits for a network spawn.")]
        [SerializeField] int offlinePlayers = 2;

        [Tooltip("Which of them the screen follows.")]
        [SerializeField] int offlineViewerIndex;

        void Start()
        {
            // Says so rather than doing nothing quietly. An empty level used to look identical
            // whether this was set to zero or the spawner had never run at all.
            if (offlinePlayers <= 0)
            {
                Debug.LogWarning($"{nameof(PlayerSpawner)} on '{name}' has Offline Players set to " +
                                 $"{offlinePlayers}, so it creates nobody. Set it to 2 for the " +
                                 "local two-player test.", this);
                return;
            }

            var requestedPoint = ResolveRequestedSpawnPoint();
            for (var i = 0; i < offlinePlayers; i++)
                Spawn(i, viewer: i == offlineViewerIndex, controlled: true, overridePoint: requestedPoint);
        }

        /// <summary>
        /// Creates one character and tells it what it is.
        ///
        /// Networking calls this once per player with <paramref name="viewer"/> and
        /// <paramref name="controlled"/> both set from ownership.
        /// </summary>
        public GameObject Spawn(int index, bool viewer, bool controlled)
            => Spawn(index, viewer, controlled, null);

        GameObject Spawn(int index, bool viewer, bool controlled, Transform overridePoint)
        {
            if (playerPrefab == null)
            {
                Debug.LogError($"{nameof(PlayerSpawner)} on '{name}' has no player prefab.", this);
                return null;
            }

            var point = overridePoint != null ? overridePoint : PointFor(index);
            var character = Instantiate(playerPrefab, point.position, Quaternion.identity);
            character.name = viewer ? "Player" : $"Player {index + 1}";

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

        Transform ResolveRequestedSpawnPoint()
        {
            if (!SceneSpawnRequest.TryConsume(out var spawnName))
                return null;

            var spawnObject = GameObject.Find(spawnName);
            if (spawnObject != null)
                return spawnObject.transform;

            Debug.LogWarning($"Requested spawn point '{spawnName}' was not found in scene '{gameObject.scene.name}'.", this);
            return null;
        }

        Transform PointFor(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                return transform;

            return spawnPoints[Mathf.Clamp(index, 0, spawnPoints.Length - 1)];
        }

        void OnDrawGizmos()
        {
            if (spawnPoints == null)
                return;

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            for (var i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null)
                    continue;

                Gizmos.DrawWireSphere(spawnPoints[i].position, 0.4f);
                Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[i].position + Vector3.up * 0.8f);
            }
        }
    }
}
