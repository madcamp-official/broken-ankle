using System.Collections.Generic;
using Ashburn.Noise;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashburn.Player
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
                 "flashlight and hearing-ring switches live here: left enabled on a partner they " +
                 "would turn that partner's beam and ring back on the moment they start.")]
        [SerializeField] MonoBehaviour[] viewerOnlyComponents;

        [Header("Rendering")]
        [Tooltip("The character sprite. Its sorting layer decides whether the viewer's own beam lights it.")]
        [SerializeField] SpriteRenderer body;

        [Tooltip("Sorting layer for the viewer's own body, excluded from their flashlight.")]
        [SerializeField] string localBodyLayer = "Character";

        [Tooltip("Sorting layer for everyone else. The same one as the viewer: a partner has to be " +
                 "drawn over the floor they are standing on before anything about light matters.")]
        [SerializeField] string remoteBodyLayer = "Character";

        [Header("Noise")]
        [Tooltip("Footsteps. The viewer's own are not drawn back to them; a partner's read as Ally.")]
        [SerializeField] FootstepNoise footsteps;

        /// <summary>True when the screen shows this character's point of view.</summary>
        public bool IsViewer => isViewer;

        /// <summary>True when input on this machine moves this character.</summary>
        public bool IsControlled => isControlled;

        /// <summary>
        /// Every character in the level, whoever is driving them.
        ///
        /// The darkness mask reads this. A partner carries light of their own, and it has to be let
        /// through the viewer's mask or the two of them are invisible to each other from three
        /// steps away — which is the same as not being in the same room at all.
        /// </summary>
        public static IReadOnlyList<PlayerRig> All => _all;

        static readonly List<PlayerRig> _all = new();

        Monster.Downed _downed;
        int _inputSuspendCount;

        /// <summary>True while one or more systems still own an input lock.</summary>
        public bool InputSuspended => _inputSuspendCount > 0;

        void Awake()
        {
            // Asked for once, so SuspendInput can tell a menu apart from a broken leg. See there.
            _downed = GetComponent<Monster.Downed>();

            Apply(isViewer, isControlled);
        }

        void OnEnable() => _all.Add(this);

        void OnDisable() => _all.Remove(this);

        // A static list outlives a play session when the editor skips its domain reload, and the
        // last run's characters would be carrying light through this run's darkness.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _all.Clear();

        /// <summary>
        /// Sets the character's role. Safe to call before or after spawn, so a networked spawner
        /// can call it the moment ownership is known.
        /// </summary>
        public void Apply(bool viewer, bool controlled)
        {
            isViewer = viewer;
            isControlled = controlled;

            ApplyInputState();

            foreach (var component in viewerOnlyComponents)
                if (component != null)
                    component.enabled = viewer;

            foreach (var go in viewerOnlyObjects)
                if (go != null)
                    go.SetActive(viewer);

            // A partner used to be moved to Default so the viewer's beam could light them, which is
            // the one thing Character is excluded from. It cost far more than it bought: Default is
            // the bottom of this project's stack, below Floor, Wall and Object, so every partner was
            // drawn underneath the floor they were standing on and could not be seen at all — no
            // amount of light reaches something the tilemap is painted over. Their own glow and the
            // room's lamps both include Character, so they are lit there anyway.
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

        /// <summary>
        /// Takes this character's hands off the keys while a menu is up, and gives them back.
        ///
        /// <see cref="Apply"/> would do the same thing, but only by being told the role again, and a
        /// caller that merely wants the menu open has no business deciding whether this character is
        /// the viewer. It belongs here because this is already the one place that knows which
        /// components read input.
        ///
        /// The body is stopped as well. Disabling the controller stops its FixedUpdate, and a
        /// Rigidbody2D with no drag keeps whatever velocity it had — the character would coast on
        /// across the room for as long as the menu was open, which is a horror game's worst way to
        /// find out somebody opened the settings.
        /// </summary>
        public void SuspendInput(bool suspended)
        {
            // A character this machine does not drive has no hands to take off the keys, and stopping
            // its body would be reaching into somebody else's: a partner's position arrives from the
            // wire every frame and is none of a local menu's business.
            if (!isControlled)
                return;

            if (suspended)
                _inputSuspendCount++;
            else if (_inputSuspendCount > 0)
                _inputSuspendCount--;

            // A player on the floor does not stand up because a cutscene ended. Downed switches off
            // the movement and the interactor — the same two components listed here — and it is the
            // only thing entitled to switch them back on. Without this a story beat that finished
            // while somebody was down handed their legs back and left the state saying otherwise:
            // they walked about with their partner being offered 구출하기 over a healthy character,
            // and their own camera watching somebody else because the game had them lying there.
            if (!suspended && _downed != null && _downed.IsDown)
            {
                ApplyInputState();
                return;
            }

            ApplyInputState();

            if (!InputSuspended)
                return;

            var body = GetComponent<Rigidbody2D>();
            if (body != null)
                body.linearVelocity = Vector2.zero;

            // Everything downstream of movement — the animator, the footsteps — reads these off the
            // controller, and left as they were the character would keep walking on the spot and
            // keep making the noise that goes with it.
            var controller = GetComponent<PlayerController>();
            if (controller != null)
                controller.Drive(Vector2.zero, MovementMode.Walk);
        }

        /// <summary>Reapplies input flags after another player state changed components.</summary>
        public void RefreshInputState() => ApplyInputState();

        void ApplyInputState()
        {
            var enabled = isControlled && !InputSuspended &&
                          (_downed == null || !_downed.IsDown);

            foreach (var component in inputComponents)
                if (component != null)
                    component.enabled = enabled;
        }
    }
}
