using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashburn.Player
{
    /// <summary>
    /// Lets the beam spill over the character's own body, but only when it is pointed toward the
    /// camera.
    ///
    /// In this view down is nearer, so a beam aimed south passes between the viewer and the
    /// character and should wash across them. Aimed north it travels away behind their back, and
    /// lighting the body there reads as the character glowing from the inside — which is exactly
    /// what the sorting-layer split was added to stop.
    ///
    /// Switching the light's target layers is what makes it directional: the body layer is added
    /// while aiming down and dropped the rest of the time.
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    [DefaultExecutionOrder(920)]
    public class FlashlightBodyWash : MonoBehaviour
    {
        [Tooltip("The sorting layer the characters render on.")]
        [SerializeField] string bodyLayer = "Character";

        [Tooltip("Sorting layers the beam always lights — the level itself.")]
        [SerializeField] string worldLayer = "Default";

        [Tooltip("Beam must point at least this far down before it washes over the body. " +
                 "Zero would flicker on and off while aiming straight across.")]
        [SerializeField, Range(0f, 1f)] float engageBelow = 0.15f;

        [Tooltip("And must come back up past this before it stops. The gap is what stops flicker.")]
        [SerializeField, Range(0f, 1f)] float releaseAbove = 0.05f;

        Light2D _light;
        int[] _worldOnly;
        int[] _worldAndBody;
        bool _washing;

        void Awake()
        {
            _light = GetComponent<Light2D>();

            var world = SortingLayer.NameToID(worldLayer);
            var body = SortingLayer.NameToID(bodyLayer);
            _worldOnly = new[] { world };
            _worldAndBody = new[] { world, body };

            Apply(false);
        }

        // Runs after the aim (900) so the direction read here is this frame's, not last frame's.
        void LateUpdate()
        {
            // The cone opens along local +Y, so that is the beam direction.
            var down = -transform.up.y;

            if (!_washing && down > engageBelow)
                Apply(true);
            else if (_washing && down < releaseAbove)
                Apply(false);
        }

        void Apply(bool washing)
        {
            _washing = washing;
            _light.targetSortingLayers = washing ? _worldAndBody : _worldOnly;
        }
    }
}
