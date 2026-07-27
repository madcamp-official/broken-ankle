using System.Collections;
using Ashburn.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Frames one room at a time instead of following the player around.
    ///
    /// A room the camera holds still on is a room the player has to read: what is in it is decided
    /// when they walk in, not by where they happen to be standing. It also means a partner in the
    /// next room is off screen entirely, which is the point — Ashburn.MD is about two people who
    /// cannot see what the other one is looking at.
    ///
    /// It takes the camera over only once it has a room to show. Until then
    /// <see cref="CinemachinePlayerBinder"/> keeps following the player, so a level built without
    /// any <see cref="RoomBounds"/> still plays instead of staring at empty space.
    /// </summary>
    public class RoomCamera : MonoBehaviour
    {
        [Tooltip("The camera to drive. Found on this object when left empty.")]
        [SerializeField] CinemachineCamera cinemachineCamera;

        [Tooltip("World units of empty space kept around the room, so walls are not flush with " +
                 "the screen edge.")]
        [SerializeField] float padding = 0.75f;

        [Tooltip("How the view gets from one room to the next.\n\n" +
                 "Fade — black, move, back. The next room is simply there.\n" +
                 "Pan — slides across. Shows the player the wall between the two rooms.\n" +
                 "Cut — instant, no cover.")]
        [SerializeField] Transition transition = Transition.Fade;

        [Tooltip("Seconds to slide, for Pan. A slow pan reads as the building being large.")]
        [SerializeField] float panSeconds = 0.45f;

        [Tooltip("Seconds to go black, for Fade.")]
        [SerializeField] float fadeOutSeconds = 0.22f;

        [Tooltip("Seconds to come back, for Fade. Slower than going out: arriving somewhere should " +
                 "take longer than leaving.")]
        [SerializeField] float fadeInSeconds = 0.38f;

        /// <summary>How the view moves between rooms.</summary>
        public enum Transition
        {
            Fade,
            Pan,
            Cut,
        }

        [Tooltip("Largest orthographic size a room may be shown at. A long corridor framed whole " +
                 "would put the camera so far back the walls are a stripe across the middle of an " +
                 "otherwise empty screen; past this the view follows the player inside the room " +
                 "instead, and stops at its edges.")]
        [SerializeField] float maxSize = 6f;

        [Tooltip("Aspect to assume before a camera exists to ask. Only used on the first frame.")]
        [SerializeField] float fallbackAspect = 16f / 9f;

        /// <summary>The room camera in the current scene, if there is one.</summary>
        public static RoomCamera Current { get; private set; }

        /// <summary>The room being shown, or null before the player has entered one.</summary>
        public RoomBounds Room { get; private set; }

        CinemachinePlayerBinder _binder;
        Transform _viewer;
        Coroutine _fade;
        Vector3 _targetPosition;
        float _targetSize;
        Vector3 _velocity;
        float _sizeVelocity;
        bool _framed;
        bool _fading;

        void Awake()
        {
            Current = this;

            if (cinemachineCamera == null)
                cinemachineCamera = GetComponent<CinemachineCamera>();

            if (cinemachineCamera == null)
            {
                Debug.LogError($"{nameof(RoomCamera)} on '{name}' has no {nameof(CinemachineCamera)}.", this);
                enabled = false;
                return;
            }

            _binder = cinemachineCamera.GetComponent<CinemachinePlayerBinder>();
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>
        /// Shows the given room. Called by <see cref="RoomBounds"/> when the viewer walks in, but
        /// anything may call it — a scripted moment that pulls the view somewhere else.
        /// </summary>
        public void Frame(RoomBounds room)
        {
            if (room == null || room == Room)
                return;

            Room = room;

            // Follow and this cannot both drive the camera. Handing it over on the first room
            // rather than at startup is what keeps a level with no rooms playable.
            if (!_framed)
            {
                if (_binder != null)
                    _binder.enabled = false;

                cinemachineCamera.Follow = null;
            }

            _targetSize = Mathf.Min(SizeFor(room.Area), maxSize);
            _targetPosition = PositionFor(room, _targetSize);

            // The first room always cuts, with no cover. Fading in from black at startup would
            // hide the one moment the player has not asked for anything yet, and sliding in from
            // wherever the camera happened to start reads as a move they did not make.
            if (!_framed)
            {
                _framed = true;
                Snap();
                return;
            }

            if (transition == Transition.Cut)
            {
                Snap();
                return;
            }

            if (transition != Transition.Fade || ScreenFade.Current == null)
                return;

            // A second doorway crossed mid-fade replaces the first rather than queueing behind it,
            // or walking back and forth would leave the screen black for as long as it took.
            if (_fade != null)
                StopCoroutine(_fade);

            _fade = StartCoroutine(FadeThrough());
        }

        IEnumerator FadeThrough()
        {
            _fading = true;

            yield return ScreenFade.Current.To(1f, fadeOutSeconds);
            Snap();
            yield return ScreenFade.Current.To(0f, fadeInSeconds);

            _fading = false;
            _fade = null;
        }

        void Snap()
        {
            cinemachineCamera.transform.position = _targetPosition;
            cinemachineCamera.Lens.OrthographicSize = _targetSize;
        }

        void LateUpdate()
        {
            if (!_framed)
                return;

            // Recomputed rather than set once: a room too large to show whole tracks the player
            // across it, and a room that fits resolves to its centre every time anyway.
            _targetPosition = PositionFor(Room, _targetSize);

            // Mid-fade the move happens in one step behind black. Easing towards it as well would
            // mean the camera is still travelling when the screen comes back.
            if (_fading)
                return;

            if (transition != Transition.Pan || panSeconds <= 0f)
            {
                Snap();
                return;
            }

            cinemachineCamera.transform.position = Vector3.SmoothDamp(
                cinemachineCamera.transform.position, _targetPosition, ref _velocity, panSeconds);

            cinemachineCamera.Lens.OrthographicSize = Mathf.SmoothDamp(
                cinemachineCamera.Lens.OrthographicSize, _targetSize, ref _sizeVelocity, panSeconds);
        }

        /// <summary>
        /// Where to sit to show the room. Its centre when the whole room fits; otherwise over the
        /// player, pulled back so the view never leaves the room — which is what keeps a corridor
        /// feeling like a corridor rather than a window onto the walls around it.
        /// </summary>
        Vector3 PositionFor(RoomBounds room, float size)
        {
            var z = cinemachineCamera.transform.position.z;

            if (room == null)
                return cinemachineCamera.transform.position;

            var area = room.Area;
            var halfHeight = size;
            var halfWidth = size * (Camera.main != null ? Camera.main.aspect : fallbackAspect);

            var centre = new Vector3(area.center.x, area.center.y, z);

            var viewer = Viewer();
            if (viewer == null)
                return centre;

            // On an axis the room is smaller than the view, there is nothing to track along:
            // clamping would fight itself, so stay centred.
            var x = area.size.x <= halfWidth * 2f
                ? area.center.x
                : Mathf.Clamp(viewer.position.x, area.min.x + halfWidth, area.max.x - halfWidth);

            var y = area.size.y <= halfHeight * 2f
                ? area.center.y
                : Mathf.Clamp(viewer.position.y, area.min.y + halfHeight, area.max.y - halfHeight);

            return new Vector3(x, y, z);
        }

        Transform Viewer()
        {
            if (_viewer != null)
                return _viewer;

            var tagged = GameObject.FindGameObjectWithTag("Player");
            _viewer = tagged != null ? tagged.transform : null;
            return _viewer;
        }

        /// <summary>
        /// Orthographic size that fits the room. Half the height, unless the room is wider than the
        /// screen can show at that height, in which case width decides instead.
        /// </summary>
        float SizeFor(Bounds area)
        {
            var aspect = Camera.main != null ? Camera.main.aspect : fallbackAspect;
            if (aspect <= 0f)
                aspect = fallbackAspect;

            var byHeight = area.size.y * 0.5f + padding;
            var byWidth = (area.size.x * 0.5f + padding) / aspect;
            return Mathf.Max(byHeight, byWidth);
        }
    }
}
