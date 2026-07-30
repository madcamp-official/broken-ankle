using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashburn.World
{
    /// <summary>
    /// Keeps exactly the maps somebody is standing in loaded, and no others.
    ///
    /// The alternative — every map in one scene — is fine at two maps and stops being fine well
    /// before ten: one enormous scene file that two people cannot edit without colliding in git,
    /// and every map's art resident in memory whether or not anyone is near it. Here a map is its
    /// own scene, loaded when somebody arrives and dropped when the last of them leaves.
    ///
    /// Counting rather than tracking who is where: two players in the same house is one map held
    /// twice, and a map is only worth unloading once both have gone. The count also covers the
    /// moment mid-travel when a player has claimed the map ahead but not yet let go of the one
    /// behind, which is the window an unload would otherwise land in.
    /// </summary>
    public static class MapLoader
    {
        static readonly Dictionary<string, int> _claims = new();

        /// <summary>Maps whose load has been started and has not finished.</summary>
        static readonly HashSet<string> _loading = new();

        /// <summary>Maps currently held, with how many players are holding each.</summary>
        public static IReadOnlyDictionary<string, int> Claims => _claims;

        /// <summary>Whether this map is in memory right now.</summary>
        public static bool IsLoaded(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && SceneManager.GetSceneByName(sceneName).isLoaded;

        /// <summary>
        /// Takes a hold on a map, loading it if this is the first. Yields until it is up and its
        /// <see cref="MapZone"/> has moved it to its slot.
        /// </summary>
        public static IEnumerator Acquire(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"{nameof(MapLoader)} was asked for a map with no name.");
                yield break;
            }

            _claims.TryGetValue(sceneName, out var held);
            _claims[sceneName] = held + 1;

            if (IsLoaded(sceneName))
                yield break;

            // Somebody else asked for this map and it is still on its way. IsLoaded is false for
            // the whole of that, so without this the second caller starts a second load of the same
            // scene: two copies in memory, two MapZones, two slots claimed, and every lookup by
            // name answering with whichever copy it found first. Two players walking through the
            // same door together is all it takes.
            if (_loading.Contains(sceneName))
            {
                while (_loading.Contains(sceneName))
                    yield return null;

                yield break;
            }

            _loading.Add(sceneName);

            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"Could not load the map '{sceneName}'. Is it in " +
                               "File > Build Profiles > Scene List?");
                _loading.Remove(sceneName);
                Release(sceneName);
                yield break;
            }

            while (!load.isDone)
                yield return null;

            // A frame for the arriving scene's Awake and Start: the zone claims its slot and moves
            // the map, and anything placed before that would be standing at the origin.
            yield return null;

            // After the frame, so anybody who was waiting wakes up to a map that has already
            // claimed its slot rather than to one still sitting on the origin.
            _loading.Remove(sceneName);

            if (MapZone.Find(sceneName) == null)
                Debug.LogError($"The map '{sceneName}' loaded but has no {nameof(MapZone)} on a " +
                               "root object, so it is sitting on top of whatever else is loaded.");
        }

        /// <summary>Lets go of a map, unloading it once nobody is left in it.</summary>
        public static void Release(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !_claims.TryGetValue(sceneName, out var held))
                return;

            held--;
            if (held > 0)
            {
                _claims[sceneName] = held;
                return;
            }

            _claims.Remove(sceneName);

            if (!IsLoaded(sceneName))
                return;

            // Unloading the last loaded scene would leave the game with nothing, and the systems
            // scene is not one of ours to begin with, so this only ever touches map scenes.
            var operation = SceneManager.UnloadSceneAsync(sceneName);
            if (operation == null)
                return;

            operation.completed += _ => Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// Drops every hold without unloading anything, for a run that is starting over.
        ///
        /// The scenes themselves are not this method's business: a restart loads the systems scene
        /// on its own, which takes every additively loaded map with it. What has to go is the
        /// count, because it is a static and a second attempt would otherwise begin believing the
        /// maps of the first are still up.
        /// </summary>
        public static void ForgetClaims()
        {
            _claims.Clear();
            _loading.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => ForgetClaims();
    }
}
