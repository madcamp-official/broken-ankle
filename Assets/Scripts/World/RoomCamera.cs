using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] float padding = 1.25f;

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

        [Tooltip("In a room too large to frame whole, stop the camera at the room's walls. Leave " +
                 "off when keeping the player readable matters more than hiding the outside map.")]
        [SerializeField] bool clampToRoom;

        [Tooltip("Fraction of the visible half-size the player may move from the camera centre " +
                 "before a large room starts scrolling. Keeps rooms stable while still revealing " +
                 "their far edges.")]
        [SerializeField, Range(0f, 0.9f)] float followDeadZone = 1f / 3f;

        [Tooltip("Aspect to assume before a camera exists to ask. Only used on the first frame.")]
        [SerializeField] float fallbackAspect = 16f / 9f;

        [Header("Cutscene framing")]
        [SerializeField] float groupMinimumSize = 4.5f;
        [SerializeField] float groupMaximumSize = 9f;
        [SerializeField] float groupPadding = 2f;
        [SerializeField] float groupSmoothSeconds = 0.16f;

        /// <summary>The room camera in the current scene, if there is one.</summary>
        public static RoomCamera Current { get; private set; }

        /// <summary>The room being shown, or null before the player has entered one.</summary>
        public RoomBounds Room { get; private set; }

        CinemachinePlayerBinder _binder;
        Transform _viewer;
        MapPresence _viewerPresence;
        float _restingSize;
        Coroutine _fade;
        Vector3 _targetPosition;
        float _targetSize;
        Vector3 _velocity;
        float _sizeVelocity;
        bool _framed;
        bool _fading;
        bool _groupFraming;
        readonly List<Transform> _groupTargets = new();

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

            // What the camera was authored at, kept for the maps that have no rooms to frame.
            _restingSize = cinemachineCamera.Lens.OrthographicSize;
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
            _targetPosition = InitialPositionFor(room, _targetSize);

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
            if (_groupFraming)
            {
                UpdateGroupFrame();
                return;
            }

            if (!_framed)
                return;

            // The room being framed can end up in a map the viewer has left. Travelling does not
            // walk out through a trigger, and the map arrived in may have no RoomBounds at all —
            // the village has none — so nothing calls Frame to correct it. Left alone the camera
            // stays in the building the player walked out of.
            if (HasLeftItsMap())
            {
                Release();
                return;
            }

            // While this drives the camera, Follow must stay empty. A body component with a target
            // moves the camera itself, after this has already placed it, so anything that sets
            // Follow behind our back does not fight for the camera — it simply wins.
            if (cinemachineCamera.Follow != null)
                cinemachineCamera.Follow = null;

            // Recomputed rather than set once: a room too large to show whole tracks the player
            // across it, and a room that fits resolves to its centre every time anyway.
            _targetPosition = PositionFor(Room, _targetSize, _targetPosition);

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
        Vector3 InitialPositionFor(RoomBounds room, float size)
        {
            if (room == null)
                return cinemachineCamera.transform.position;

            var area = room.Area;
            var z = cinemachineCamera.transform.position.z;
            var centre = new Vector3(area.center.x, area.center.y, z);

            return FitsWholeRoom(area) ? centre : PositionFor(room, size, centre);
        }

        /// <summary>Keeps every supplied participant visible until <see cref="EndGroupFrame"/>.</summary>
        public void BeginGroupFrame(IEnumerable<Transform> participants)
        {
            _groupTargets.Clear();
            if (participants != null)
                foreach (var participant in participants)
                    if (participant != null && !_groupTargets.Contains(participant))
                        _groupTargets.Add(participant);

            if (_groupTargets.Count == 0)
                return;

            _groupFraming = true;

            if (_binder != null)
                _binder.enabled = false;

            cinemachineCamera.Follow = null;
            UpdateGroupTarget();
            Snap();
        }

        /// <summary>Returns control to the current room frame or the ordinary player follower.</summary>
        public void EndGroupFrame()
        {
            if (!_groupFraming)
                return;

            _groupFraming = false;
            _groupTargets.Clear();
            _velocity = Vector3.zero;
            _sizeVelocity = 0f;

            if (Room != null)
            {
                _framed = true;
                _targetSize = Mathf.Min(SizeFor(Room.Area), maxSize);
                _targetPosition = InitialPositionFor(Room, _targetSize);
                Snap();
                return;
            }

            Release();
        }

        void UpdateGroupFrame()
        {
            for (var i = _groupTargets.Count - 1; i >= 0; i--)
                if (_groupTargets[i] == null || !_groupTargets[i].gameObject.activeInHierarchy)
                    _groupTargets.RemoveAt(i);

            if (_groupTargets.Count == 0)
            {
                EndGroupFrame();
                return;
            }

            UpdateGroupTarget();

            if (groupSmoothSeconds <= 0f)
            {
                Snap();
                return;
            }

            cinemachineCamera.transform.position = Vector3.SmoothDamp(
                cinemachineCamera.transform.position, _targetPosition, ref _velocity,
                groupSmoothSeconds);

            cinemachineCamera.Lens.OrthographicSize = Mathf.SmoothDamp(
                cinemachineCamera.Lens.OrthographicSize, _targetSize, ref _sizeVelocity,
                groupSmoothSeconds);
        }

        void UpdateGroupTarget()
        {
            var min = (Vector2)_groupTargets[0].position;
            var max = min;

            for (var i = 1; i < _groupTargets.Count; i++)
            {
                var point = (Vector2)_groupTargets[i].position;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var centre = (min + max) * 0.5f;
            var neededHeight = (max.y - min.y) * 0.5f + groupPadding;
            var neededWidth = ((max.x - min.x) * 0.5f + groupPadding) / Aspect();

            _targetSize = Mathf.Clamp(
                Mathf.Max(neededHeight, neededWidth), groupMinimumSize, groupMaximumSize);
            _targetPosition = new Vector3(
                centre.x, centre.y, cinemachineCamera.transform.position.z);
        }

        Vector3 PositionFor(RoomBounds room, float size, Vector3 current)
        {
            var z = cinemachineCamera.transform.position.z;

            if (room == null)
                return cinemachineCamera.transform.position;

            var area = room.Area;
            var halfHeight = size;
            var halfWidth = size * Aspect();

            var centre = new Vector3(area.center.x, area.center.y, z);

            var viewer = Viewer();
            if (viewer == null)
                return centre;

            // A room that fits stays composed as one shot. Once any axis makes the room too large
            // to frame, both axes track so the player remains readable near a nominally fitting
            // edge. A one-third dead zone keeps the route ahead visible.
            if (FitsWholeRoom(area))
                return centre;

            var x = Track(current.x, viewer.position.x, area.min.x, area.max.x, halfWidth);
            var y = Track(current.y, viewer.position.y, area.min.y, area.max.y, halfHeight);

            return new Vector3(x, y, z);
        }

        bool FitsWholeRoom(Bounds area)
        {
            return SizeFor(area) <= maxSize + 0.001f;
        }

        float Track(float current, float viewer, float min, float max, float half)
        {
            var deadHalf = half * followDeadZone;
            var target = current;

            if (viewer < current - deadHalf)
                target = viewer + deadHalf;
            else if (viewer > current + deadHalf)
                target = viewer - deadHalf;

            return clampToRoom ? ClampToRoom(target, min, max, half) : target;
        }

        float ClampToRoom(float value, float min, float max, float half)
        {
            // SizeFor adds padding when a whole room fits. Large rooms hit maxSize instead, so
            // position clamping must add the same margin or the outer wall is cut at the viewport.
            var minimum = min + half - padding;
            var maximum = max - half + padding;
            return minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : (min + max) * 0.5f;
        }

        float Aspect()
        {
            var aspect = Camera.main != null ? Camera.main.aspect : fallbackAspect;
            return aspect > 0f ? aspect : fallbackAspect;
        }

        /// <summary>
        /// Whether the room on screen belongs to a map the viewer is no longer standing in.
        ///
        /// Deliberately narrow. "The viewer is outside the room's box" would also be true of
        /// somebody standing in a doorway, which is between two rooms and inside neither, and
        /// releasing there would drop the framing every time anybody crossed one.
        /// </summary>
        bool HasLeftItsMap()
        {
            if (Room == null)
                return true;

            var presence = ViewerPresence();
            if (presence == null || presence.Zone == null)
                return false;

            return MapZone.Of(Room) != presence.Zone;
        }

        /// <summary>
        /// Hands the camera back to <see cref="CinemachinePlayerBinder"/>, which is where it starts
        /// and where it belongs whenever there is no room to show. Puts the lens back to what the
        /// camera was authored at, so an outdoor map is not framed at the size of the last cupboard
        /// the player stood in.
        /// </summary>
        void Release()
        {
            Room = null;
            _framed = false;

            if (_fade != null)
            {
                StopCoroutine(_fade);
                _fade = null;
            }

            _fading = false;
            cinemachineCamera.Lens.OrthographicSize = _restingSize;

            var viewer = Viewer();
            if (viewer != null)
                cinemachineCamera.Follow = viewer;

            if (_binder != null)
                _binder.enabled = true;
        }

        Transform Viewer()
        {
            if (_viewer != null)
                return _viewer;

            var tagged = GameObject.FindGameObjectWithTag("Player");
            _viewer = tagged != null ? tagged.transform : null;
            _viewerPresence = null;
            return _viewer;
        }

        MapPresence ViewerPresence()
        {
            if (_viewerPresence != null)
                return _viewerPresence;

            var viewer = Viewer();
            _viewerPresence = viewer != null ? viewer.GetComponentInParent<MapPresence>() : null;
            return _viewerPresence;
        }

        /// <summary>
        /// Orthographic size that fits the room. Half the height, unless the room is wider than the
        /// screen can show at that height, in which case width decides instead.
        /// </summary>
        float SizeFor(Bounds area)
        {
            var aspect = Aspect();

            var byHeight = area.size.y * 0.5f + padding;
            var byWidth = (area.size.x * 0.5f + padding) / aspect;
            return Mathf.Max(byHeight, byWidth);
        }
    }
}
