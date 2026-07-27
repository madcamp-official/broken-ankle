using System.Collections;
using Ashburn.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashburn.World
{
    /// <summary>
    /// Moves the players from one map to another.
    ///
    /// A map is a scene: the street outside is one, the house you walk into is a second. Keeping
    /// them apart means an interior can be as detailed as it likes without the outside paying for
    /// it, and it is the seam the game already has — every scene builds its own level, spawns its
    /// own characters and lights itself.
    ///
    /// Which door you came through cannot be handed over as a reference, since the object holding
    /// it is destroyed by the load. It travels as a name in <see cref="PendingEntry"/>, which the
    /// arriving scene's <see cref="Player.PlayerSpawner"/> reads once and clears.
    ///
    /// Survives the load itself, because something has to hold the screen black across it.
    /// </summary>
    public class MapTravel : MonoBehaviour
    {
        [Tooltip("Seconds to go black before the load.")]
        [SerializeField] float fadeOutSeconds = 0.35f;

        [Tooltip("Seconds to come back once the new map is up.")]
        [SerializeField] float fadeInSeconds = 0.5f;

        /// <summary>
        /// The entry the arriving scene should put the players at, or null for its own spawn
        /// points. Read once by the spawner and cleared, so a later reload starts normally.
        /// </summary>
        public static string PendingEntry { get; private set; }

        /// <summary>True while a map change is under way. Doors ignore use during one.</summary>
        public static bool IsTravelling { get; private set; }

        static MapTravel _instance;

        /// <summary>
        /// Goes to <paramref name="sceneName"/> and arrives at the entry called
        /// <paramref name="entryId"/>.
        /// </summary>
        public static void Go(string sceneName, string entryId)
        {
            if (IsTravelling)
                return;

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"{nameof(MapTravel)} was asked for a map with no name.");
                return;
            }

            Require().StartCoroutine(Require().Travel(sceneName, entryId));
        }

        /// <summary>Takes the pending entry and forgets it. Returns null when arriving normally.</summary>
        public static string TakeEntry()
        {
            var entry = PendingEntry;
            PendingEntry = null;
            return entry;
        }

        IEnumerator Travel(string sceneName, string entryId)
        {
            IsTravelling = true;

            var fade = ScreenFade.Current;
            if (fade != null)
                yield return fade.To(1f, fadeOutSeconds);

            PendingEntry = entryId;

            // Held black across the load rather than faded per scene, so the seam between the two
            // maps is never on screen.
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (load == null)
            {
                Debug.LogError($"Could not load '{sceneName}'. Is it in File > Build Profiles > Scene List?");
                PendingEntry = null;
                IsTravelling = false;

                if (fade != null)
                    yield return fade.To(0f, fadeInSeconds);

                yield break;
            }

            while (!load.isDone)
                yield return null;

            // A frame for the arriving scene's Start methods to run, so the characters exist and
            // are standing in the right place before anyone can see them.
            yield return null;

            IsTravelling = false;

            // Re-read: the fade that survived the load is the one to bring back, and it may not be
            // the object we started with.
            fade = ScreenFade.Current;
            if (fade != null)
                yield return fade.To(0f, fadeInSeconds);
        }

        static MapTravel Require()
        {
            if (_instance != null)
                return _instance;

            var existing = FindFirstObjectByType<MapTravel>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _instance = existing;
            }
            else
            {
                var host = new GameObject(nameof(MapTravel));
                _instance = host.AddComponent<MapTravel>();
            }

            // Must outlive the scene it started in, or the coroutine holding the screen black dies
            // halfway through and the player watches the new map assemble itself.
            _instance.transform.SetParent(null);
            DontDestroyOnLoad(_instance.gameObject);
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _instance = null;
            PendingEntry = null;
            IsTravelling = false;
        }
    }
}
