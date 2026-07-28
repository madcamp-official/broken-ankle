using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Which map a character is standing in.
    ///
    /// Position cannot answer this. Maps sit far enough apart that distance happens to keep them
    /// separate today, but that is a consequence of the slot spacing rather than a rule, and it
    /// fails quietly the day a map grows or the spacing is tuned. Everything that must not reach
    /// across a map — the noise bus above all — asks here instead, and gets an answer that has
    /// nothing to do with coordinates.
    ///
    /// Set when the character is spawned and again on every arrival, both of which know exactly
    /// which map they are putting the character into. No triggers, nothing to walk out of.
    /// </summary>
    public class MapPresence : MonoBehaviour
    {
        /// <summary>The map this character is in, or null before it has been placed.</summary>
        public MapZone Zone { get; private set; }

        /// <summary>The scene name of that map, or null.</summary>
        public string MapName => Zone != null ? Zone.Id : null;

        /// <summary>
        /// The id noise is tagged with. <see cref="MapZone.Unzoned"/> until the character has been
        /// placed, which nothing should ever hear.
        /// </summary>
        public int MapId => Zone != null ? Zone.MapId : MapZone.Unzoned;

        /// <summary>Records that this character is now in <paramref name="zone"/>.</summary>
        public void Enter(MapZone zone)
        {
            if (zone == null)
                Debug.LogWarning($"'{name}' was put into a map that has no {nameof(MapZone)}. It " +
                                 "will neither hear nor be heard.", this);

            Zone = zone;
        }
    }
}
