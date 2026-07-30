using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Raises one flag once several others are all up.
    ///
    /// The shape the second half of the game keeps asking for: the elevator goes down when Grant
    /// has fixed it <em>and</em> Nathan has read what is down there, and the hangar wakes when the
    /// power is back <em>and</em> the warden spec has been found. Both are two people finishing
    /// separate jobs in separate rooms, in whichever order they happen to.
    ///
    /// A flag rather than a reference to the door, so the thing being waited on does not have to
    /// exist in the same scene, or exist yet. <see cref="MapDoor"/>'s Required Flag and
    /// <c>WorldFlagGate</c> both read the result without knowing this is here.
    /// </summary>
    public class WorldFlagAll : MonoBehaviour
    {
        [Tooltip("Every one of these has to be set.")]
        [SerializeField] string[] required;

        [Tooltip("Raised once they are, and only once.")]
        [SerializeField] string raise;

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;

            // The requirements can already all be met when this map loads: the pair did both jobs
            // an hour ago, or the flags arrived from the room together in one packet before
            // anything in this scene existed to hear them.
            Check();
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string flag) => Check();

        void Check()
        {
            if (string.IsNullOrEmpty(raise) || WorldState.Has(raise) || required == null ||
                required.Length == 0)
            {
                return;
            }

            foreach (var flag in required)
                if (!WorldState.Has(flag))
                    return;

            WorldState.Raise(raise);
        }
    }
}
