using System.Collections;
using System.Collections.Generic;
using Ashburn.Core;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Moves one character from one map to another.
    ///
    /// One, not both. This used to swap the whole scene, which took everybody with it whether or
    /// not they had touched the door — the partner standing three rooms away was teleported into a
    /// house they had not entered. Maps are loaded side by side now (see <see cref="MapLoader"/>),
    /// so travelling is a character walking out of one and into another while the other map, and
    /// whoever is still in it, carries on untouched.
    ///
    /// Which door you came through is handed over directly rather than through a static, because
    /// two players can now be travelling at the same time and a single pending value would have
    /// them arrive at each other's doors.
    /// </summary>
    public static class MapTravel
    {
        [Tooltip("Seconds to go black before the change, and to come back after it.")]
        const float FadeOutSeconds = 0.35f;
        const float FadeInSeconds = 0.5f;

        static readonly HashSet<GameObject> _travelling = new();
        static MonoBehaviour _runner;

        /// <summary>Whether this character is in the middle of a map change.</summary>
        public static bool IsTravelling(GameObject who) => who != null && _travelling.Contains(who);

        /// <summary>Whether anybody is.</summary>
        public static bool AnyTravelling => _travelling.Count > 0;

        /// <summary>
        /// Sends <paramref name="traveller"/> to the map called <paramref name="mapName"/>, arriving
        /// at the <see cref="MapEntry"/> named <paramref name="entryId"/>.
        /// </summary>
        public static void Go(GameObject traveller, string mapName, string entryId)
        {
            if (traveller == null)
            {
                Debug.LogError($"{nameof(MapTravel)} was asked to move nobody.");
                return;
            }

            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogError($"{nameof(MapTravel)} was asked for a map with no name.");
                return;
            }

            if (IsTravelling(traveller))
                return;

            Runner().StartCoroutine(Travel(traveller, mapName, entryId));
        }

        static IEnumerator Travel(GameObject traveller, string mapName, string entryId)
        {
            _travelling.Add(traveller);

            var presence = traveller.GetComponent<MapPresence>();
            if (presence == null)
            {
                Debug.LogError($"'{traveller.name}' has no {nameof(MapPresence)}, so there is no " +
                               "record of the map it is leaving.", traveller);
                _travelling.Remove(traveller);
                yield break;
            }

            var leaving = presence.MapName;

            // Only the person going anywhere sees the screen go dark. Fading for the partner left
            // behind would black out a player who is mid-step in a room they never left.
            var showFade = IsLocalViewer(traveller);
            var fade = showFade ? ScreenFade.Current : null;
            if (fade != null)
                yield return fade.To(1f, FadeOutSeconds);

            // Taken before the old map is let go, so a player crossing back and forth between two
            // maps never drops the count to zero and pays to load one they are about to stand in.
            yield return MapLoader.Acquire(mapName);

            var zone = MapZone.Find(mapName);
            if (zone == null)
            {
                Debug.LogError($"The map '{mapName}' is not loaded, so '{traveller.name}' has " +
                               "nowhere to arrive.", traveller);
                _travelling.Remove(traveller);

                if (fade != null)
                    yield return fade.To(0f, FadeInSeconds);

                yield break;
            }

            Place(traveller, zone, entryId);
            presence.Enter(zone);

            if (leaving != mapName)
                MapLoader.Release(leaving);

            // A frame with the character already standing in the new map, so the camera and the
            // visibility mask have both caught up before the screen comes back.
            yield return null;

            _travelling.Remove(traveller);

            if (fade != null)
                yield return fade.To(0f, FadeInSeconds);
        }

        /// <summary>Puts the character on the entry mark, beside anyone already standing there.</summary>
        static void Place(GameObject traveller, MapZone zone, string entryId)
        {
            var entry = MapEntry.Find(entryId, zone);
            Vector3 destination;

            if (entry != null)
            {
                // Everyone already in the map is standing somewhere; arriving on top of one of them
                // would shove whoever loses the overlap through the nearest wall.
                destination = entry.PointFor(CountIn(zone));
            }
            else
            {
                Debug.LogWarning($"Map '{zone.Id}' has no entry called '{entryId}'. Arriving at " +
                                 "the map's origin instead.", zone);
                destination = zone.transform.position;
            }

            // The transform alone is not enough for a character with physics: the body keeps its
            // own position and would drag the character straight back on the next fixed step.
            var body = traveller.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = destination;
                body.linearVelocity = Vector2.zero;
            }

            traveller.transform.position = destination;
        }

        static int CountIn(MapZone zone)
        {
            var count = 0;
            foreach (var presence in Object.FindObjectsByType<MapPresence>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (presence.Zone == zone)
                    count++;

            return count;
        }

        static bool IsLocalViewer(GameObject traveller)
        {
            var rig = traveller.GetComponent<Player.PlayerRig>();
            return rig == null || rig.IsViewer;
        }

        /// <summary>
        /// Something has to own the coroutine, and it cannot be the door: the door belongs to a map,
        /// and letting go of that map is one of the steps.
        /// </summary>
        static MonoBehaviour Runner()
        {
            if (_runner != null)
                return _runner;

            var host = new GameObject(nameof(MapTravel)) { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            _runner = host.AddComponent<MapTravelRunner>();
            return _runner;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _travelling.Clear();
            _runner = null;
        }
    }

    /// <summary>Nothing but a place for <see cref="MapTravel"/>'s coroutines to live.</summary>
    public class MapTravelRunner : MonoBehaviour
    {
    }
}
