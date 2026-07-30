using Ashburn.Monster;
using Ashburn.Player;
using Ashburn.World;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashburn.Net
{
    /// <summary>
    /// Carries one character across the wire.
    ///
    /// Only what the other machine cannot work out for itself. The facing and every animator
    /// parameter are left out on purpose: they are a pure function of the movement input, so the
    /// receiving side recomputes them and the packet stays half the size — see Multiplayer.MD.
    ///
    /// Movement is owner-authoritative. The position arrives already decided, because the machine
    /// holding the keyboard has already run it into the walls; this end only has to catch up to it
    /// smoothly. That is the right trade for a slow horror game, where a partner sliding half a step
    /// is invisible and a partner lagging behind their own input is not.
    ///
    /// The position is measured from the map's own origin rather than from the world's, and the map
    /// is named alongside it. World coordinates were tried and could not survive the first door:
    /// maps are moved to a slot claimed in load order, two players who reach the same house by
    /// different routes number that house differently, and their partner is then a thousand units
    /// from where they are standing with nothing reporting a problem.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class PlayerSync : MonoBehaviourPun, IPunObservable
    {
        [Header("Parts")]
        [SerializeField] PlayerController controller;
        [SerializeField] FlashlightToggle flashlight;
        [SerializeField] HearingRingToggle headset;

        [Tooltip("Whether this player is on the floor. Left empty the character's own is used.")]
        [SerializeField] Downed downed;

        [Tooltip("The beam's transform, whose angle is sent. Left empty the flashlight's own object " +
                 "is used.")]
        [SerializeField] Transform beam;

        [Header("Smoothing")]
        [Tooltip("Seconds to close the gap to the position last heard about. Long enough to hide " +
                 "the gaps between packets, short enough that a partner is where they look.")]
        [SerializeField] float catchUpSeconds = 0.08f;

        [Tooltip("Distance past which the character is moved rather than eased — an arrival from " +
                 "another map, or a packet lost long enough that easing would cross a wall.")]
        [SerializeField] float teleportOver = 4f;

        Rigidbody2D _body;
        MapPresence _presence;

        // Hidden while the partner is in a map this machine has not opened. The lights are listed
        // separately because a Light2D is not a Renderer, and a beam left burning in a room its
        // owner walked out of is the most visible thing in a dark game.
        Renderer[] _renderers;
        Light2D[] _lights;
        bool _visible = true;
        bool _warnedMissingMap;

        /// <summary>Set by Resolve: they named a map and this machine does not have it open.</summary>
        bool _elsewhere;

        int _netMap;
        Vector2 _netLocal;
        Vector2 _netPosition;
        Vector2 _netMoveInput;
        MovementMode _netMode = MovementMode.Walk;
        float _netBeamAngle;
        bool _netFlashlight = true;
        bool _netHeadset = true;
        bool _netDowned;
        bool _heard;

        Vector2 _velocity;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _presence = GetComponent<MapPresence>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _lights = GetComponentsInChildren<Light2D>(true);

            if (controller == null)
                controller = GetComponent<PlayerController>();

            if (flashlight == null)
                flashlight = GetComponent<FlashlightToggle>();

            if (headset == null)
                headset = GetComponent<HearingRingToggle>();

            if (downed == null)
                downed = GetComponent<Downed>();

            if (beam == null && flashlight != null)
                beam = flashlight.transform;

            _netPosition = transform.position;
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                // Named, and measured from that map's origin. Both halves are needed: the number
                // says which world this position belongs to, and without it the receiver cannot
                // know whether it even has that world open.
                var zone = _presence != null ? _presence.Zone : null;
                var here = _body != null ? _body.position : (Vector2)transform.position;

                stream.SendNext(zone != null ? zone.SharedId : 0);
                stream.SendNext(zone != null ? here - zone.Origin : here);
                stream.SendNext(controller != null ? controller.MoveInput : Vector2.zero);
                stream.SendNext((byte)(controller != null ? controller.Mode : MovementMode.Walk));
                stream.SendNext(beam != null ? beam.eulerAngles.z : 0f);
                stream.SendNext(flashlight != null && flashlight.IsOn);
                stream.SendNext(headset != null && headset.IsOn);
                stream.SendNext(downed != null && downed.IsDown);
                return;
            }

            _netMap = (int)stream.ReceiveNext();
            _netLocal = (Vector2)stream.ReceiveNext();
            _netMoveInput = (Vector2)stream.ReceiveNext();
            _netMode = (MovementMode)(byte)stream.ReceiveNext();
            _netBeamAngle = (float)stream.ReceiveNext();
            _netFlashlight = (bool)stream.ReceiveNext();
            _netHeadset = (bool)stream.ReceiveNext();
            _netDowned = (bool)stream.ReceiveNext();

            // The first packet is where this character actually is, not somewhere to ease towards
            // from the spawn point it was built at.
            if (!_heard)
            {
                _heard = true;
                if (Resolve())
                    Place(_netPosition);
            }
        }

        /// <summary>
        /// Turns the map-local position just heard into a position in this machine's world, and
        /// records which map the partner is in.
        ///
        /// False when that map is not open here, which is the ordinary state of affairs while one
        /// player is in the house and the other is out on the street. There is no sensible place to
        /// put somebody who is not in any world this machine has loaded, so they are hidden instead
        /// of being drawn at a coordinate that means nothing.
        /// </summary>
        bool Resolve()
        {
            _elsewhere = false;

            // Nothing has said which map they are in yet — a packet sent before their machine had
            // placed them. There is nowhere to move them to, but that is not a reason to hide
            // somebody, and the next packet almost certainly says where they are.
            if (_netMap == 0)
                return false;

            var zone = MapZone.FindShared(_netMap);
            if (zone == null)
            {
                // Named a map, and it is not one this machine has open. Now they really are
                // somewhere else.
                _elsewhere = true;

                if (!_warnedMissingMap)
                {
                    _warnedMissingMap = true;
                    Debug.Log($"'{name}' is in a map this machine has not opened (id {_netMap}), " +
                              "so they are hidden until it is.", this);
                }

                return false;
            }

            _warnedMissingMap = false;

            _netPosition = _netLocal + zone.Origin;

            // Kept current rather than set once at spawn. The noise bus asks a character which map
            // it is in, and a partner who walked out of the starting map would otherwise go on
            // being heard in it — and go on not being heard where they actually are.
            if (_presence != null && _presence.Zone != zone)
                _presence.Enter(zone);

            return true;
        }

        void Update()
        {
            if (photonView.IsMine || !_heard)
                return;

            // Their map may have been opened or closed on this machine since the last packet: a
            // partner becomes placeable the moment this player follows them through the door.
            var placed = Resolve();

            // Hidden only for being somewhere this machine has not loaded. Not knowing yet where
            // somebody is is not grounds for making them disappear — that turns one late packet
            // into a partner who is never drawn again.
            SetVisible(!_elsewhere);

            if (!placed)
                return;

            var here = _body != null ? _body.position : (Vector2)transform.position;

            if (Vector2.Distance(here, _netPosition) > teleportOver)
                Place(_netPosition);
            else
                Place(Vector2.SmoothDamp(here, _netPosition, ref _velocity, catchUpSeconds));

            // Handed to the controller rather than to the animator, so everything that reads a
            // character's movement — the animator, the footsteps, the interactor — sees a partner
            // exactly as it sees the local player.
            if (controller != null)
                controller.Drive(_netMoveInput, _netMode);

            if (beam != null)
                beam.rotation = Quaternion.Euler(0f, 0f, _netBeamAngle);

            // Applied every frame rather than on change: these switches are off on a partner, so
            // nothing else is going to put them right, and setting a bool to what it already is
            // costs nothing.
            if (flashlight != null && flashlight.IsOn != _netFlashlight)
                flashlight.Apply(_netFlashlight);

            if (headset != null && headset.IsOn != _netHeadset)
                headset.Apply(_netHeadset);

            // Applied rather than requested: this is the owner's own account of themselves, and
            // asking them to change it would send their state back to them.
            if (downed != null && downed.IsDown != _netDowned)
                downed.Apply(_netDowned);
        }

        /// <summary>
        /// Draws or hides the partner, without switching the character off.
        ///
        /// Only the renderers. Disabling the object would take its <see cref="PlayerSync"/> with it
        /// and there would be nothing left listening for the packet that says they came back.
        /// </summary>
        void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;

            if (_renderers != null)
                foreach (var renderer in _renderers)
                    if (renderer != null)
                        renderer.enabled = visible;

            if (_lights != null)
                foreach (var light in _lights)
                    if (light != null)
                        light.enabled = visible;
        }

        void Place(Vector2 position)
        {
            if (_body != null)
                _body.position = position;

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }
    }
}
