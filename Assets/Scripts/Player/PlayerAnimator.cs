using Ashburn.Monster;
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

        [Tooltip("Sprite shown while this character has been caught and is waiting for rescue.")]
        [SerializeField] Sprite downedSprite;

        PlayerController _controller;
        Downed _downed;
        Animator _animator;
        SpriteRenderer _renderer;
        Sprite _standingSprite;
        bool _animatorWasEnabled;
        bool _showingDowned;

        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _downed = GetComponent<Downed>();

            // The Animator lives on the pixel-snapped visual child, not here, because rounding the
            // body's own transform would stall slow movement.
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogError($"{nameof(PlayerAnimator)} on '{name}' found no Animator in its children.", this);
                return;
            }

            _renderer = _animator.GetComponent<SpriteRenderer>();
            if (_renderer == null)
                _renderer = GetComponentInChildren<SpriteRenderer>();

            if (_renderer == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerAnimator)} on '{name}' found no SpriteRenderer for its downed visual.",
                    this);
                return;
            }

            _standingSprite = _renderer.sprite;
        }

        void OnEnable()
        {
            if (_downed == null)
                return;

            _downed.DownChanged += SetDownedVisual;
            SetDownedVisual(_downed.IsDown);
        }

        void OnDisable()
        {
            if (_downed != null)
                _downed.DownChanged -= SetDownedVisual;
        }

        void Update()
        {
            if (_showingDowned || _animator == null)
                return;

            // FacingDirection is already snapped to a cardinal and is never zero, which is what
            // keeps the blend tree on a single direction instead of drifting between two.
            var facing = _controller.FacingDirection;
            _animator.SetFloat(MoveX, facing.x);
            _animator.SetFloat(MoveY, facing.y);
            _animator.SetBool(IsMoving, _controller.IsMoving);
        }

        void SetDownedVisual(bool down)
        {
            if (_animator == null || _renderer == null || down == _showingDowned)
                return;

            if (down)
            {
                _animatorWasEnabled = _animator.enabled;
                _animator.enabled = false;

                if (downedSprite != null)
                    _renderer.sprite = downedSprite;
                else
                    Debug.LogError($"{nameof(PlayerAnimator)} on '{name}' has no downed sprite.", this);

                _showingDowned = true;
                return;
            }

            if (_standingSprite != null)
                _renderer.sprite = _standingSprite;

            _animator.enabled = _animatorWasEnabled;
            _showingDowned = false;
        }
    }
}
