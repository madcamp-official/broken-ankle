using System;
using System.Collections.Generic;
using Ashburn.Interaction;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

namespace Ashburn.Monster
{
    /// <summary>
    /// Sits on a player and handles being hurt: the legs go, control is taken away, and the only
    /// way back up is a partner standing over them long enough to help.
    ///
    /// Going down is not the end, it is a countdown somebody else can still beat. The help is a
    /// hold rather than a press on purpose — it costs the rescuer real seconds standing still in
    /// the open, which is what makes going down frightening for both of them rather than an
    /// inconvenience for one.
    ///
    /// The state belongs to the machine that owns the character. Everyone else asks — see
    /// <see cref="RequestDown"/> — which keeps two clients from disagreeing about who is standing.
    /// </summary>
    public class Downed : MonoBehaviourPun, IHoldInteractable
    {
        [FormerlySerializedAs("disableWhileHeld")]
        [Tooltip("Components switched off while down — movement, interaction, the flashlight.")]
        [SerializeField] MonoBehaviour[] disableWhileDown;

        [FormerlySerializedAs("hideWhileHeld")]
        [Tooltip("Objects hidden while down, such as the beam and the hearing ring.")]
        [SerializeField] GameObject[] hideWhileDown;

        [FormerlySerializedAs("rescuePrompt")]
        [Tooltip("Shown to a partner standing close enough to help.")]
        [SerializeField] string helpPrompt = "일으켜 세우기";

        [Tooltip("Seconds the partner must hold the key. Long enough to be a real cost, short " +
                 "enough to be worth trying with the monster still in the room.")]
        [SerializeField] float helpSeconds = 2.5f;

        /// <summary>True while this player is on the floor and cannot move.</summary>
        public bool IsDown { get; private set; }

        /// <summary>Raised when this player goes down and when they are helped back up.</summary>
        public event Action<bool> DownChanged;

        /// <summary>Every player currently in the level, standing or not.</summary>
        public static IReadOnlyList<Downed> All => _all;

        /// <summary>
        /// Whether anybody is still on their feet.
        ///
        /// False with nobody in the level at all, so a caller has to check <see cref="All"/> is not
        /// empty first: an empty level is a game that has not started, not one that has been lost.
        /// </summary>
        public static bool AnyStanding
        {
            get
            {
                foreach (var player in _all)
                    if (player != null && !player.IsDown)
                        return true;

                return false;
            }
        }

        static readonly List<Downed> _all = new();

        Rigidbody2D _body;

        void Awake() => _body = GetComponent<Rigidbody2D>();

        void OnEnable() => _all.Add(this);

        void OnDisable() => _all.Remove(this);

        // A static list outlives a play session when the editor skips its domain reload, and the
        // last run's players would be counted among this run's casualties.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _all.Clear();

        #region Interaction

        public string Prompt => helpPrompt;

        /// <summary>Seconds of held key this asks for. Read by <see cref="PlayerInteractor"/>.</summary>
        public float HoldSeconds => helpSeconds;

        // Only worth offering to somebody else, and only while there is something to undo. Nobody
        // picks themselves up: that is the whole mechanic.
        public bool CanInteract(GameObject interactor) => IsDown && interactor != gameObject;

        public void Interact(GameObject interactor)
        {
            if (CanInteract(interactor))
                RequestUp();
        }

        #endregion

        #region Authority

        /// <summary>
        /// Whether this machine is the one allowed to change this character's state.
        ///
        /// Offline — and in the split-keyboard test — nothing was created through PUN and every
        /// character is this machine's to decide about.
        /// </summary>
        bool Mine => photonView == null || photonView.InstantiationId == 0 || photonView.IsMine;

        /// <summary>
        /// Puts this player down, asking their own machine to do it when it is not this one.
        ///
        /// Called by the monster, which runs on the host. The host cannot simply write the state:
        /// the owner's next packet would overwrite it and the player would stand straight back up.
        /// </summary>
        public void RequestDown()
        {
            if (Mine)
                GoDown();
            else
                photonView.RPC(nameof(RpcDown), photonView.Owner);
        }

        /// <summary>Helps this player up, asking their own machine when it is not this one.</summary>
        public void RequestUp()
        {
            if (Mine)
                StandUp();
            else
                photonView.RPC(nameof(RpcUp), photonView.Owner);
        }

        [PunRPC]
        void RpcDown() => GoDown();

        [PunRPC]
        void RpcUp() => StandUp();

        #endregion

        #region State

        /// <summary>
        /// Applies the state without asking who owns it.
        ///
        /// Public for the partner's copy, which is driven from the wire rather than by anything
        /// that happened on this machine — <c>PlayerSync</c> is the only other caller.
        /// </summary>
        public void Apply(bool down)
        {
            if (down)
                GoDown();
            else
                StandUp();
        }

        void GoDown()
        {
            if (IsDown)
                return;

            IsDown = true;

            // Whatever they were doing stops here. Without it the body keeps the velocity it had —
            // there is no drag on it — and a player who was running goes on sliding across the
            // floor on legs they are no longer using.
            if (_body != null)
                _body.linearVelocity = Vector2.zero;

            SetControlEnabled(false);
            DownChanged?.Invoke(true);
        }

        void StandUp()
        {
            if (!IsDown)
                return;

            IsDown = false;
            SetControlEnabled(true);
            DownChanged?.Invoke(false);
        }

        /// <summary>
        /// Takes this character's hands away, and gives them back.
        ///
        /// Only ever on a character this machine drives. A partner's copy is already switched off
        /// by <see cref="Player.PlayerRig"/> — their beam and their switches belong to their own
        /// screen — and standing them back up here would turn on components the rig deliberately
        /// left off, putting somebody else's flashlight in this player's hands.
        ///
        /// Nothing is lost by skipping it: a partner's copy moves and animates from the wire, and
        /// their machine has already stopped sending anything to move to.
        /// </summary>
        void SetControlEnabled(bool enabled)
        {
            if (!Mine)
                return;

            foreach (var component in disableWhileDown)
                if (component != null)
                    component.enabled = enabled;

            foreach (var go in hideWhileDown)
                if (go != null)
                    go.SetActive(enabled);
        }

        #endregion
    }
}
