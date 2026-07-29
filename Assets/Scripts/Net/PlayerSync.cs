using Ashburn.Player;
using Photon.Pun;
using UnityEngine;

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
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class PlayerSync : MonoBehaviourPun, IPunObservable
    {
        [Header("Parts")]
        [SerializeField] PlayerController controller;
        [SerializeField] FlashlightToggle flashlight;
        [SerializeField] HearingRingToggle headset;

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

        Vector2 _netPosition;
        Vector2 _netMoveInput;
        MovementMode _netMode = MovementMode.Walk;
        float _netBeamAngle;
        bool _netFlashlight = true;
        bool _netHeadset = true;
        bool _heard;

        Vector2 _velocity;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            if (controller == null)
                controller = GetComponent<PlayerController>();

            if (flashlight == null)
                flashlight = GetComponent<FlashlightToggle>();

            if (headset == null)
                headset = GetComponent<HearingRingToggle>();

            if (beam == null && flashlight != null)
                beam = flashlight.transform;

            _netPosition = transform.position;
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(_body != null ? _body.position : (Vector2)transform.position);
                stream.SendNext(controller != null ? controller.MoveInput : Vector2.zero);
                stream.SendNext((byte)(controller != null ? controller.Mode : MovementMode.Walk));
                stream.SendNext(beam != null ? beam.eulerAngles.z : 0f);
                stream.SendNext(flashlight != null && flashlight.IsOn);
                stream.SendNext(headset != null && headset.IsOn);
                return;
            }

            _netPosition = (Vector2)stream.ReceiveNext();
            _netMoveInput = (Vector2)stream.ReceiveNext();
            _netMode = (MovementMode)(byte)stream.ReceiveNext();
            _netBeamAngle = (float)stream.ReceiveNext();
            _netFlashlight = (bool)stream.ReceiveNext();
            _netHeadset = (bool)stream.ReceiveNext();

            // The first packet is where this character actually is, not somewhere to ease towards
            // from the spawn point it was built at.
            if (!_heard)
            {
                _heard = true;
                Place(_netPosition);
            }
        }

        void Update()
        {
            if (photonView.IsMine || !_heard)
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
        }

        void Place(Vector2 position)
        {
            if (_body != null)
                _body.position = position;

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }
    }
}
