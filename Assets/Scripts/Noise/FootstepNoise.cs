using Ashburn.World;
using Ashburn.Player;
using UnityEngine;

namespace Ashburn.Noise
{
    /// <summary>
    /// Turns walking into sound. Every so many steps the character announces itself on the
    /// <see cref="NoiseBus"/>, and how far that carries depends entirely on how they chose to move.
    ///
    /// Crouching is not silent, only quiet: a player who crouches everywhere should still be
    /// findable if something is close enough, or crouching becomes a free pass.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class FootstepNoise : MonoBehaviour
    {
        [Tooltip("Whose footsteps these are. The player's own are not drawn back to them.")]
        [SerializeField] NoiseKind kind = NoiseKind.Self;

        /// <summary>
        /// Settable because the same prefab is both the player and their partner, and which one it
        /// is only becomes known when the character is spawned.
        /// </summary>
        public NoiseKind Kind
        {
            get => kind;
            set => kind = value;
        }

        [Header("How far each step carries, in world units")]
        [SerializeField] float crouchRange = 2.5f;
        [SerializeField] float walkRange = 7f;
        [SerializeField] float runRange = 13f;

        [Header("Seconds between steps")]
        [SerializeField] float crouchInterval = 0.75f;
        [SerializeField] float walkInterval = 0.45f;
        [SerializeField] float runInterval = 0.28f;

        PlayerController _controller;
        float _nextStepTime;

        void Awake() => _controller = GetComponent<PlayerController>();

        void Update()
        {
            if (!_controller.IsMoving)
            {
                // Standing still, the next step should land promptly rather than waiting out a
                // timer that ran down while the player was stopped.
                _nextStepTime = 0f;
                return;
            }

            if (Time.time < _nextStepTime)
                return;

            var mode = _controller.Mode;
            _nextStepTime = Time.time + IntervalFor(mode);
            NoiseBus.Emit(transform.position, RangeFor(mode), kind, MapZone.IdOf(this));
        }

        float RangeFor(MovementMode mode)
        {
            switch (mode)
            {
                case MovementMode.Run: return runRange;
                case MovementMode.Crouch: return crouchRange;
                default: return walkRange;
            }
        }

        float IntervalFor(MovementMode mode)
        {
            switch (mode)
            {
                case MovementMode.Run: return runInterval;
                case MovementMode.Crouch: return crouchInterval;
                default: return walkInterval;
            }
        }
    }
}
