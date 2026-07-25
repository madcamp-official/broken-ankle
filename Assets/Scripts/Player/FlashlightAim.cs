using UnityEngine;
using UnityEngine.InputSystem;

namespace Morrow.Player
{
    /// <summary>
    /// Points the flashlight, and decides what "forward" means for the beam.
    ///
    /// The three sources trade expressiveness against simplicity, and which one feels right is a
    /// question for playing rather than reasoning, so all three stay available and the field is
    /// switchable at runtime.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class FlashlightAim : MonoBehaviour
    {
        public enum AimSource
        {
            /// <summary>Follows the movement stick, so the beam always leads the walk.</summary>
            MoveDirection,

            /// <summary>Follows the mouse, letting the player look one way and walk another.</summary>
            Pointer,

            /// <summary>Follows the body's four-way facing. Matches the sprite exactly, but is coarse.</summary>
            Facing,
        }

        [Header("Aim")]
        [SerializeField] AimSource source = AimSource.MoveDirection;

        [Tooltip("Degrees the cone is rotated relative to the object's right. Light shapes open along +Y, so -90 lines them up.")]
        [SerializeField] float angleOffset = -90f;

        [Tooltip("Degrees per second the beam swings. Zero snaps instantly.")]
        [SerializeField] float turnSpeed = 900f;

        [Header("Input")]
        [Tooltip("Only needed for the Pointer source. Drag Assets/InputSystem_Actions here.")]
        [SerializeField] InputActionAsset inputActions;

        InputAction _aimAction;
        PlayerController _controller;
        Camera _camera;
        float _currentAngle;

        void Awake()
        {
            _controller = GetComponentInParent<PlayerController>();
            if (_controller == null)
                Debug.LogError($"{nameof(FlashlightAim)} on '{name}' found no PlayerController above it.", this);

            if (inputActions == null)
                return;

            _aimAction = inputActions.FindAction("Player/Aim", throwIfNotFound: false);
        }

        void OnEnable() => _aimAction?.Enable();

        void OnDisable() => _aimAction?.Disable();

        // LateUpdate so the camera has already been moved for this frame. Reading a stale camera
        // would convert the pointer against last frame's view and make the beam lag while walking.
        void LateUpdate()
        {
            var target = TargetAngle();
            _currentAngle = turnSpeed <= 0f
                ? target
                : Mathf.MoveTowardsAngle(_currentAngle, target, turnSpeed * Time.deltaTime);

            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngle + angleOffset);
        }

        float TargetAngle()
        {
            Vector2? direction;
            switch (source)
            {
                case AimSource.Pointer:
                    direction = PointerDirection();
                    break;
                case AimSource.Facing:
                    direction = _controller != null ? _controller.FacingDirection : (Vector2?)null;
                    break;
                default:
                    direction = MoveDirection();
                    break;
            }

            // Standing still leaves no direction to read, so hold the last one rather than
            // snapping the beam back to some default and lighting up the wrong wall.
            return direction.HasValue
                ? Mathf.Atan2(direction.Value.y, direction.Value.x) * Mathf.Rad2Deg
                : _currentAngle;
        }

        Vector2? MoveDirection()
        {
            if (_controller == null)
                return null;

            var input = _controller.MoveInput;
            if (input.sqrMagnitude > 0.01f)
                return input;

            // Idle: keep pointing where the body faces, which is where the walk left off.
            return _controller.FacingDirection;
        }

        Vector2? PointerDirection()
        {
            // A pointer that never reported a position reads as (0,0), which would aim at the
            // screen's bottom-left corner instead of telling us there is no mouse.
            if (_aimAction == null || !_aimAction.enabled || _aimAction.activeControl == null)
                return null;

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return null;
            }

            var screenPoint = _aimAction.ReadValue<Vector2>();
            var world = _camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));
            var direction = (Vector2)world - (Vector2)transform.position;

            // Right on top of the pivot there is no meaningful direction, so hold the last angle.
            return direction.sqrMagnitude < 1e-6f ? (Vector2?)null : direction;
        }
    }
}
