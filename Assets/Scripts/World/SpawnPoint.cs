using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Where a character stands at the start of a game, marked in the map itself.
    ///
    /// A marker rather than a list on the spawner, because the spawner does not live in the map any
    /// more — it outlives every one of them, and a reference from it into a map would break the
    /// moment that map was unloaded. The map carries its own answer, and the spawner asks whichever
    /// map it is opening.
    ///
    /// Order is hierarchy order, which is the order the generator creates them in: the first is
    /// player one.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.8f);
        }
    }
}
