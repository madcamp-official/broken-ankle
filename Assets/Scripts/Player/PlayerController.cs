using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Player
{
    /// <summary>
    /// Top-down eight-way movement for the player character.
    ///
    /// Movement is written to the Rigidbody2D rather than the Transform so that the physics
    /// engine resolves wall collisions for us. Moving the Transform directly would push the
    /// player straight through level geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour, IUsesActionMap
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Move action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Which action map to read. A second local character uses its own map so two " +
                 "players at one keyboard do not fight over the same keys.")]
        [SerializeField] string actionMap = "Player";

        [Header("Movement")]
        [Tooltip("World units per second while walking. One unit is one 32x32 tile at PPU 32.")]
        [SerializeField] float moveSpeed = 2.9f;

        [Tooltip("Multiplies walk speed while sprinting. Fast, but the loudest way to travel.")]
        [SerializeField] float runMultiplier = 1.7f;

        [Tooltip("Multiplies walk speed while crouching. Slow, and nearly silent.")]
        [SerializeField] float crouchMultiplier = 0.45f;

        [Tooltip("Seconds to reach full speed. Zero is instant, which feels twitchy for a horror pace.")]
        [SerializeField] float acceleration = 0.08f;

        Rigidbody2D _body;
        InputActionAsset _ownedActions;
        InputAction _moveAction;
        InputAction _sprintAction;
        InputAction _crouchAction;
        Vector2 _moveInput;

        /// <summary>
        /// Last direction the player actually moved in, snapped to one of the four cardinals and
        /// never zero. Animation and <see cref="Ashburn.Interaction.PlayerInteractor"/> both need a
        /// facing even while the player stands still, which is why this survives a released stick.
        /// </summary>
        public Vector2 FacingDirection { get; private set; } = Vector2.down;

        /// <summary>True while the player is actually moving, for footstep noise and animation.</summary>
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        /// <summary>
        /// The movement stick or key combination as read this frame, before it is snapped to a
        /// cardinal. Zero while standing still. Anything that should point exactly where the player
        /// is walking — the flashlight, for one — wants this rather than
        /// <see cref="FacingDirection"/>, which only ever holds four values.
        /// </summary>
        public Vector2 MoveInput => _moveInput;

        /// <summary>
        /// How the player is travelling. Crouching trades speed for quiet and sprinting does the
        /// reverse, which is the whole tension of moving through a room something else is listening
        /// to. Reported even while standing still, so a crouching player stays crouched.
        /// </summary>
        public MovementMode Mode { get; private set; } = MovementMode.Walk;

        /// <summary>
        /// Reports what somebody else's character is doing, for a copy of it driven from the network
        /// rather than from this keyboard.
        ///
        /// Everything downstream of movement — the animator, the footsteps, the interactor's reach —
        /// reads the three values above and does not care where they came from. So a partner is
        /// simply this component with its Update switched off and these values written in, rather
        /// than a second set of look-alike fields for everything to check.
        ///
        /// Movement itself is not applied here. The position arrives already decided, because the
        /// machine that owns the character has already collided with its walls.
        /// </summary>
        public void Drive(Vector2 moveInput, MovementMode mode)
        {
            _moveInput = moveInput;
            Mode = mode;

            if (IsMoving)
                FacingDirection = SnapToCardinal(_moveInput);
        }

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

            // An InputAction belongs to the asset, not to the component reading it, so enabling or
            // disabling one is felt by every character pointed at the same asset. Two local players
            // share this prefab: the second one starts on the prefab's default map, enables
            // Player/Move, and then switches to Player2 — and the switch disables Player/Move on
            // its way out, from under the first player, who is already running and never finds out.
            // A private copy per character keeps one character's bookkeeping off another's.
            inputActions = _ownedActions = Instantiate(inputActions);

            Resolve();
        }

        void OnDestroy()
        {
            if (_ownedActions != null)
                Destroy(_ownedActions);
        }

        /// <summary>Points this character at a different action map, even after it has started.</summary>
        public void UseActionMap(string map)
        {
            if (map == actionMap)
                return;

            var wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
                OnDisable();

            actionMap = map;
            Resolve();

            if (wasEnabled)
                OnEnable();
        }

        void Resolve()
        {
            if (inputActions == null)
                return;

            _moveAction = inputActions.FindAction($"{actionMap}/Move", throwIfNotFound: false);
            if (_moveAction == null)
                Debug.LogError($"Could not find the '{actionMap}/Move' action in the assigned asset.", this);

            _sprintAction = inputActions.FindAction($"{actionMap}/Sprint", throwIfNotFound: false);
            _crouchAction = inputActions.FindAction($"{actionMap}/Crouch", throwIfNotFound: false);
        }

        // Each component enables only the actions it reads. Enabling the whole Player map here
        // would mean disabling this component also silences interaction, which is not our call.
        void OnEnable()
        {
            _moveAction?.Enable();
            _sprintAction?.Enable();
            _crouchAction?.Enable();
        }

        void OnDisable()
        {
            _moveAction?.Disable();
            _sprintAction?.Disable();
            _crouchAction?.Disable();
        }

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

            // Crouch wins over sprint. Holding both is a fumble, and the quiet option is the one
            // a player who pressed crouch was trying to get.
            if (_crouchAction != null && _crouchAction.IsPressed())
                Mode = MovementMode.Crouch;
            else if (_sprintAction != null && _sprintAction.IsPressed())
                Mode = MovementMode.Run;
            else
                Mode = MovementMode.Walk;
        }

        void FixedUpdate()
        {
            var speed = moveSpeed * SpeedMultiplier(Mode);
            var target = _moveInput * speed;

            if (acceleration <= 0f)
                _body.linearVelocity = target;
            else
                _body.linearVelocity = Vector2.MoveTowards(
                    _body.linearVelocity, target, speed / acceleration * Time.fixedDeltaTime);
        }

        float SpeedMultiplier(MovementMode mode)
        {
            switch (mode)
            {
                case MovementMode.Run: return runMultiplier;
                case MovementMode.Crouch: return crouchMultiplier;
                default: return 1f;
            }
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
