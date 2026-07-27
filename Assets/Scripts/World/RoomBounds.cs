using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Marks one room, so the camera knows what to frame when the player walks into it.
    ///
    /// Only the viewer moves the camera. A second character sharing the keyboard is in the level
    /// but not behind the eyes, and letting it drag the view into another room would take the
    /// screen away from the person holding it. <see cref="Player.PlayerRig"/> already puts the
    /// Player tag on exactly one character, which is the test used here.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class RoomBounds : MonoBehaviour
    {
        [Tooltip("Tag of the character whose view this follows.")]
        [SerializeField] string viewerTag = "Player";

        BoxCollider2D _area;

        /// <summary>The room in world space, as the camera should frame it.</summary>
        public Bounds Area => _area != null ? _area.bounds : new Bounds(transform.position, Vector3.one);

        void Awake()
        {
            _area = GetComponent<BoxCollider2D>();
            _area.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(viewerTag))
                return;

            if (RoomCamera.Current != null)
                RoomCamera.Current.Frame(this);
        }

        void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null)
                return;

            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
        }
    }
}
