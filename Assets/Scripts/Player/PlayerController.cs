using UnityEngine;
using UnityEngine.InputSystem;

namespace Morrow.Player
{
    /// <summary>
    /// Top-down eight-way movement for the player character.
    ///
    /// Movement is written to the Rigidbody2D rather than the Transform so that the physics
    /// engine resolves wall collisions for us. Moving the Transform directly would push the
    /// player straight through level geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Player/Move action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Movement")]
        [Tooltip("World units per second. One unit is one 32x32 tile at PPU 32.")]
        [SerializeField] float moveSpeed = 3.5f;

        [Tooltip("Seconds to reach full speed. Zero is instant, which feels twitchy for a horror pace.")]
        [SerializeField] float acceleration = 0.08f;

        Rigidbody2D _body;
        InputAction _moveAction;
        Vector2 _moveInput;

        /// <summary>
        /// Last direction the player actually moved in, snapped to one of the four cardinals and
        /// never zero. Animation and <see cref="Morrow.Interaction.PlayerInteractor"/> both need a
        /// facing even while the player stands still, which is why this survives a released stick.
        /// </summary>
        public Vector2 FacingDirection { get; private set; } = Vector2.down;

        /// <summary>True while the player is actually moving, for footstep noise and animation.</summary>
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            // A top-down character has no gravity and must never be spun by a collision, or it
            // would drift away from the pixel grid and end up rendering between pixels.
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(PlayerController)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            _moveAction = inputActions.FindAction("Player/Move", throwIfNotFound: false);
            if (_moveAction == null)
                Debug.LogError("Could not find the 'Player/Move' action in the assigned asset.", this);
        }

        // Each component enables only the actions it reads. Enabling the whole Player map here
        // would mean disabling this component also silences interaction, which is not our call.
        void OnEnable() => _moveAction?.Enable();

        void OnDisable() => _moveAction?.Disable();

        void Update()
        {
            _moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            // Keyboard input is already unit length on the diagonals, but a gamepad stick pushed
            // to a corner reads longer than one, so clamp instead of normalising: a half-tilted
            // stick should still walk slowly.
            if (_moveInput.sqrMagnitude > 1f)
                _moveInput.Normalize();

            if (IsMoving)
                FacingDirection = SnapToCardinal(_moveInput);
        }

        void FixedUpdate()
        {
            var target = _moveInput * moveSpeed;

            if (acceleration <= 0f)
                _body.linearVelocity = target;
            else
                _body.linearVelocity = Vector2.MoveTowards(
                    _body.linearVelocity, target, moveSpeed / acceleration * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Collapses a free direction onto up/down/left/right. Ties on a perfect diagonal go to
        /// the horizontal, because side-facing sprites read better than back-facing ones.
        /// </summary>
        static Vector2 SnapToCardinal(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x > 0f ? Vector2.right : Vector2.left;

            return direction.y > 0f ? Vector2.up : Vector2.down;
        }
    }
}
