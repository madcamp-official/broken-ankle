using Morrow.Noise;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Morrow.Player
{
    /// <summary>
    /// Decides what a spawned player is: the one at this keyboard, or somebody else's character
    /// seen from across the room.
    ///
    /// Everything that differs between the two is gathered here, because it is a long list and
    /// every item is a bug waiting to happen if it is set by hand per instance. Input, the camera,
    /// the flashlight, the darkness mask and the hearing ring all belong to the viewer alone; a
    /// partner needs none of them and must not have a second copy running.
    ///
    /// Nothing here knows about networking. Whatever spawns the character calls
    /// <see cref="Apply"/> with one bool, so the same prefab works for a hand-placed dummy today
    /// and a networked spawn later.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        [Header("Role")]
        [Tooltip("Whose eyes the screen belongs to. Exactly one character in a scene may be the viewer.")]
        [SerializeField] bool isViewer = true;

        [Tooltip("Whether input on this machine drives this character. A split-keyboard second " +
                 "player is controlled here without being the viewer; a networked partner is neither.")]
        [SerializeField] bool isControlled = true;

        [Header("Controlled only")]
        [Tooltip("Components that read input. Off for a character driven from somewhere else.")]
        [SerializeField] MonoBehaviour[] inputComponents;

        [Header("Viewer only")]
        [Tooltip("Objects only the viewer should see: their beam, the darkness, the hearing ring.")]
        [SerializeField] GameObject[] viewerOnlyObjects;

        [Tooltip("Components that belong to the point of view rather than to the character. The " +
                 "flashlight switch lives here: left enabled on a partner it would turn their beam " +
                 "and ring back on the moment it starts.")]
        [SerializeField] MonoBehaviour[] viewerOnlyComponents;

        [Header("Rendering")]
        [Tooltip("The character sprite. Its sorting layer decides whether the viewer's own beam lights it.")]
        [SerializeField] SpriteRenderer body;

        [Tooltip("Sorting layer for the viewer's own body, excluded from their flashlight.")]
        [SerializeField] string localBodyLayer = "Character";

        [Tooltip("Sorting layer for everyone else, so the viewer's beam can pick them out of the dark.")]
        [SerializeField] string remoteBodyLayer = "Default";

        [Header("Noise")]
        [Tooltip("Footsteps. The viewer's own are not drawn back to them; a partner's read as Ally.")]
        [SerializeField] FootstepNoise footsteps;

        /// <summary>True when the screen shows this character's point of view.</summary>
        public bool IsViewer => isViewer;

        /// <summary>True when input on this machine moves this character.</summary>
        public bool IsControlled => isControlled;

        void Awake() => Apply(isViewer, isControlled);

        /// <summary>
        /// Sets the character's role. Safe to call before or after spawn, so a networked spawner
        /// can call it the moment ownership is known.
        /// </summary>
        public void Apply(bool viewer, bool controlled)
        {
            isViewer = viewer;
            isControlled = controlled;

            foreach (var component in inputComponents)
                if (component != null)
                    component.enabled = controlled;

            foreach (var component in viewerOnlyComponents)
                if (component != null)
                    component.enabled = viewer;

            foreach (var go in viewerOnlyObjects)
                if (go != null)
                    go.SetActive(viewer);

            if (body != null)
            {
                var layer = viewer ? localBodyLayer : remoteBodyLayer;
                if (SortingLayer.NameToID(layer) != 0 || layer == "Default")
                    body.sortingLayerName = layer;
            }

            // Only the viewer's own footsteps are hidden from the ring. Everyone else's, including
            // a second player sharing this keyboard, should show up as a partner.
            if (footsteps != null)
                footsteps.Kind = viewer ? NoiseKind.Self : NoiseKind.Ally;

            // Only the viewer carries the tag the camera and the hearing ring look for. Two tagged
            // players would leave the camera bound to whichever it happened to find first.
            gameObject.tag = viewer ? "Player" : "Untagged";
        }
    }
}
