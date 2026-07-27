using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Where the players appear when they arrive from another map.
    ///
    /// Named rather than referenced, because the door that sends them here lives in a different
    /// scene and no reference can cross that gap. A house with a front and a back door has two of
    /// these, and coming in the back has to put you at the back.
    /// </summary>
    public class MapEntry : MonoBehaviour
    {
        [Tooltip("What a MapDoor asks for by name. Unique within this scene. 'Front', 'Cellar'.")]
        [SerializeField] string id = "Default";

        [Tooltip("How far apart arrivals are placed, in world units. Two characters landing on the " +
                 "same spot would shove each other through whatever is next to them.")]
        [SerializeField] float spread = 0.6f;

        /// <summary>The name a door uses to ask for this entry.</summary>
        public string Id => id;

        /// <summary>Where the given arrival stands. Index 0 is on the mark, the rest beside it.</summary>
        public Vector3 PointFor(int index)
        {
            if (index <= 0)
                return transform.position;

            // Alternating sides, so a third arrival does not walk further and further into a wall.
            var step = (index + 1) / 2 * spread;
            var side = index % 2 == 1 ? 1f : -1f;
            return transform.position + Vector3.right * (step * side);
        }

        /// <summary>Finds an entry by name in the loaded scene, or null.</summary>
        public static MapEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (var entry in FindObjectsByType<MapEntry>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (entry.id == id)
                    return entry;

            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.7f);
        }
    }
}
