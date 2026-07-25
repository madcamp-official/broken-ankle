using UnityEngine;
using UnityEngine.InputSystem;

namespace Morrow.Player
{
    /// <summary>
    /// Turns the flashlight toward the mouse, independently of which way the body is walking.
    ///
    /// Aiming apart from movement is the point: the player can back away down a corridor while
    /// keeping the beam on whatever is behind them. With a pointer the beam is continuous even
    /// though the character sprite only has four facings, and that reads fine because the light
    /// is not pixel art.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class FlashlightAim : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Player/Aim action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Aim")]
        [Tooltip("Degrees the cone is rotated relative to the object's right. Point lights open along +X.")]
        [SerializeField] float angleOffset;

        [Tooltip("Degrees per second the beam swings. Zero snaps instantly.")]
        [SerializeField] float turnSpeed = 900f;

        [Tooltip("Without a mouse — a gamepad, say — fall back to the direction the body faces.")]
        [SerializeField] bool fallBackToFacing = true;

        InputAction _aimAction;
        PlayerController _controller;
        Camera _camera;
        float _currentAngle;

        void Awake()
        {
            _controller = GetComponentInParent<PlayerController>();

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(FlashlightAim)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            _aimAction = inputActions.FindAction("Player/Aim", throwIfNotFound: false);
            if (_aimAction == null)
                Debug.LogError("Could not find the 'Player/Aim' action in the assigned asset.", this);
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
            var pointer = ReadPointerDirection();
            if (pointer.HasValue)
                return Mathf.Atan2(pointer.Value.y, pointer.Value.x) * Mathf.Rad2Deg;

            if (fallBackToFacing && _controller != null)
            {
                var facing = _controller.FacingDirection;
                return Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            }

            return _currentAngle;
        }

        Vector2? ReadPointerDirection()
        {
            if (_aimAction == null || !_aimAction.enabled)
                return null;

            // A pointer that never reported a position reads as (0,0), which would aim at the
            // screen's bottom-left corner instead of telling us there is no mouse.
            if (_aimAction.activeControl == null)
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
