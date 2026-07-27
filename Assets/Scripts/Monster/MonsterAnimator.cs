using UnityEngine;

namespace Ashburn.Monster
{
    /// <summary>
    /// Drives the monster's Animator from how it is actually moving.
    ///
    /// The direction comes from velocity rather than from the AI's destination, because what the
    /// player needs to read is which way the thing is facing right now — including while it is
    /// sliding along a wall in a direction it never chose.
    ///
    /// Playback speed follows the pace as well. The same clip carries a slow patrol and a charge,
    /// and a mech whose legs match its speed is the cheapest way to make a chase read as urgent.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MonsterAnimator : MonoBehaviour
    {
        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int IsMoving = Animator.StringToHash("IsMoving");

        [Tooltip("Speed the clips were timed for. Moving faster than this plays them faster.")]
        [SerializeField] float referenceSpeed = 1.6f;

        [SerializeField] float minPlaybackSpeed = 0.6f;
        [SerializeField] float maxPlaybackSpeed = 2.2f;

        [Tooltip("Below this the monster counts as standing still.")]
        [SerializeField] float movingThreshold = 0.15f;

        Rigidbody2D _body;
        Animator _animator;
        Vector2 _facing = Vector2.down;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            // The Animator sits on the pixel-snapped visual child, not here.
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
                Debug.LogError($"{nameof(MonsterAnimator)} on '{name}' found no Animator in its children.", this);
        }

        void Update()
        {
            if (_animator == null)
                return;

            var velocity = _body.linearVelocity;
            var speed = velocity.magnitude;
            var moving = speed > movingThreshold;

            // Hold the last facing while stopped so a halted monster keeps staring the way it came.
            if (moving)
                _facing = SnapToCardinal(velocity);

            _animator.SetFloat(MoveX, _facing.x);
            _animator.SetFloat(MoveY, _facing.y);
            _animator.SetBool(IsMoving, moving);
            _animator.speed = moving
                ? Mathf.Clamp(speed / referenceSpeed, minPlaybackSpeed, maxPlaybackSpeed)
                : 1f;
        }

        /// <summary>
        /// Four facings only, matching the sheet. Ties go to the horizontal, where the sprite
        /// reads best.
        /// </summary>
        static Vector2 SnapToCardinal(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x > 0f ? Vector2.right : Vector2.left;

            return direction.y > 0f ? Vector2.up : Vector2.down;
        }
    }
}
