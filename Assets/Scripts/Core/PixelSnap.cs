using UnityEngine;

namespace Morrow.Core
{
    /// <summary>
    /// Rounds this object's position onto the pixel grid every frame, so the sprite it carries
    /// always lands on a whole pixel instead of somewhere between two.
    ///
    /// Put this on a purely visual child, never on the object that owns the Rigidbody2D. Rounding
    /// the body's own transform feeds the rounded value back into physics, and any movement slower
    /// than half a pixel per frame then rounds away to nothing and the character sticks in place.
    /// Keeping physics on the parent and the rounding on the child lets the body accumulate motion
    /// at full precision while only the picture snaps.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PixelSnap : MonoBehaviour
    {
        [Tooltip("Must match the art's pixels per unit. At 32 the grid is 1/32 of a world unit.")]
        [SerializeField] int pixelsPerUnit = 32;

        [Tooltip("Off restores continuous positioning without removing the component, which makes this easy to A/B.")]
        [SerializeField] bool snap = true;

        Vector3 _restingLocalPosition;

        void Awake()
        {
            // Whatever offset the artist gave this child in the prefab is the pose we snap from.
            _restingLocalPosition = transform.localPosition;

            if (GetComponent<Rigidbody2D>() != null)
                Debug.LogWarning(
                    $"{nameof(PixelSnap)} on '{name}' shares a GameObject with a Rigidbody2D. " +
                    "Slow movement will stall; move it to a visual-only child.", this);
        }

        void LateUpdate()
        {
            if (!snap || pixelsPerUnit <= 0)
                return;

            // Undo the previous frame's correction first. Reading our own already-offset position
            // and rounding that again compounds the offset every frame until the sprite is left
            // behind entirely, which is exactly what happens without this line.
            transform.localPosition = _restingLocalPosition;

            // LateUpdate so this lands after movement and animation but before rendering.
            // The parent keeps its exact position; only this child is quantised.
            var p = transform.position;
            transform.position = new Vector3(
                Mathf.Round(p.x * pixelsPerUnit) / pixelsPerUnit,
                Mathf.Round(p.y * pixelsPerUnit) / pixelsPerUnit,
                p.z);
        }
    }
}
