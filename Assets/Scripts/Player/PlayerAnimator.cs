using UnityEngine;

namespace Ashburn.Player
{
    /// <summary>
    /// Feeds the Animator from <see cref="PlayerController"/> so the character faces the way it
    /// walks. The controller owns the movement decision; this component only reports it.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : MonoBehaviour
    {
        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int IsMoving = Animator.StringToHash("IsMoving");

        PlayerController _controller;
        Animator _animator;

        void Awake()
        {
            _controller = GetComponent<PlayerController>();

            // The Animator lives on the pixel-snapped visual child, not here, because rounding the
            // body's own transform would stall slow movement.
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
                Debug.LogError($"{nameof(PlayerAnimator)} on '{name}' found no Animator in its children.", this);
        }

        void Update()
        {
            // FacingDirection is already snapped to a cardinal and is never zero, which is what
            // keeps the blend tree on a single direction instead of drifting between two.
            var facing = _controller.FacingDirection;
            _animator.SetFloat(MoveX, facing.x);
            _animator.SetFloat(MoveY, facing.y);
            _animator.SetBool(IsMoving, _controller.IsMoving);
        }
    }
}
