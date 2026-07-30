using System;
using System.Collections.Generic;
using Ashburn.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Interaction
{
    /// <summary>
    /// Watches the space just in front of the player, keeps track of the best
    /// <see cref="IInteractable"/> there, and runs it when the Interact button is pressed.
    ///
    /// Content objects never talk to this component. They implement IInteractable and are found
    /// automatically, which is what keeps level building and systems work on separate files.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour, Ashburn.Player.IUsesActionMap
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Interact action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Which action map to read. A second local character uses its own map.")]
        [SerializeField] string actionMap = "Player";

        [Header("Search")]
        [Tooltip("How far in front of the player the search circle sits, in world units.")]
        [SerializeField] float reach = 0.5f;

        [Tooltip("Radius of the search circle. Larger is more forgiving to aim at.")]
        [SerializeField] float radius = 0.45f;

        [Tooltip("Which layers hold interactable objects. Leave as Everything until the team adds a dedicated layer.")]
        [SerializeField] LayerMask searchLayers = ~0;

        [Tooltip("Draw the search circle in the Scene view.")]
        [SerializeField] bool showGizmo = true;

        readonly List<Collider2D> _hits = new();
        ContactFilter2D _filter;
        InputAction _interactAction;
        InputActionAsset _ownedActions;
        PlayerController _controller;
        IInteractable _current;

        /// <summary>
        /// The interactable that would run right now, or null. UI reads
        /// <see cref="IInteractable.Prompt"/> off this to show the on-screen hint.
        /// </summary>
        public IInteractable CurrentTarget => _current;

        /// <summary>
        /// The action this character interacts with, or null before Awake has found it.
        ///
        /// Exposed so the on-screen hint can name the key the player is actually meant to press, and
        /// read how far a hold has got, rather than being told a key by hand and lying about it once
        /// the bindings move. It is this character's private copy of the asset — see Awake — so the
        /// key it names is the one this character responds to even in the split-keyboard test.
        /// </summary>
        public InputAction InteractAction => _interactAction;

        /// <summary>Fires when the target changes, including to null when the player walks away.</summary>
        public event Action<IInteractable> TargetChanged;

        /// <summary>
        /// How far the current hold has got, 0..1, or zero when nothing is being held.
        ///
        /// Read by the on-screen prompt so the bar fills. A press-and-go interaction never sets it,
        /// so nothing has to be configured to say which kind a target is.
        /// </summary>
        public float HoldProgress { get; private set; }

        IHoldInteractable _holding;
        float _holdStartedAt;

        void Awake()
        {
            _controller = GetComponent<PlayerController>();

            _filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = searchLayers,
                // Interactables are ordinary solid props, so trigger colliders are included on
                // purpose: a breaker box can have a non-blocking trigger as its use zone.
                useTriggers = true,
            };

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(PlayerInteractor)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            // Private copy per character. Actions belong to the asset, so the partner switching to
            // its own map disables Player/Interact on the way past and takes this character's use
            // key with it — silently, since nothing here is wrong afterwards.
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

            _interactAction = inputActions.FindAction($"{actionMap}/Interact", throwIfNotFound: false);
            if (_interactAction == null)
                Debug.LogError($"Could not find the '{actionMap}/Interact' action in the assigned asset.", this);
        }

        void OnEnable()
        {
            if (_interactAction == null)
                return;

            _interactAction.started += OnInteractStarted;
            _interactAction.performed += OnInteractPerformed;
            _interactAction.canceled += OnInteractCanceled;
            _interactAction.Enable();
        }

        void OnDisable()
        {
            if (_interactAction != null)
            {
                _interactAction.started -= OnInteractStarted;
                _interactAction.performed -= OnInteractPerformed;
                _interactAction.canceled -= OnInteractCanceled;
                _interactAction.Disable();
            }

            CancelHold();
            SetTarget(null);
        }

        void Update()
        {
            SetTarget(FindBestTarget());
            TickHold();
        }

        void OnInteractStarted(InputAction.CallbackContext _)
        {
            // Only a target that asks to be held starts a hold. Everything else runs on performed,
            // exactly as it did before holds existed.
            if (_current is IHoldInteractable hold && hold.HoldSeconds > 0f &&
                hold.CanInteract(gameObject))
            {
                _holding = hold;
                _holdStartedAt = Time.time;
                HoldProgress = 0f;
            }
        }

        void OnInteractPerformed(InputAction.CallbackContext _)
        {
            // A hold is finished by the clock in TickHold, not by the button. Without this the
            // press that begins the hold would also complete the interaction immediately, because
            // an action with no interaction on it performs the moment it goes down.
            if (_holding != null)
                return;

            // Re-check rather than trusting the cached target: a door can lock itself in the same
            // frame the player presses the button, and running it anyway would desync the state.
            if (_current != null && _current.CanInteract(gameObject))
                _current.Interact(gameObject);
        }

        void OnInteractCanceled(InputAction.CallbackContext _) => CancelHold();

        /// <summary>
        /// Counts out a hold, and gives up on it the moment it stops making sense.
        ///
        /// Walking away, or the target ceasing to want help — a partner picked up by somebody else,
        /// a door unlocked from the other side — has to end the hold rather than let it complete on
        /// something that is no longer there.
        /// </summary>
        void TickHold()
        {
            if (_holding == null)
                return;

            if (!ReferenceEquals(_holding, _current) || !_holding.CanInteract(gameObject))
            {
                CancelHold();
                return;
            }

            HoldProgress = Mathf.Clamp01((Time.time - _holdStartedAt) / _holding.HoldSeconds);
            if (HoldProgress < 1f)
                return;

            var finished = _holding;
            CancelHold();
            finished.Interact(gameObject);
        }

        void CancelHold()
        {
            _holding = null;
            HoldProgress = 0f;
        }

        IInteractable FindBestTarget()
        {
            IInteractable best = null;
            var bestDistance = float.MaxValue;
            var bestPriority = int.MinValue;

            FindBestTargetAt(SearchOrigin(), ref best, ref bestDistance, ref bestPriority);

            // Floor triggers such as room transitions sit under the player instead of in front
            // of them. Checking both positions keeps prop interactions directional while making
            // "stand on the tile and press E" reliable regardless of facing.
            FindBestTargetAt(transform.position, ref best, ref bestDistance, ref bestPriority);
            return best;
        }

        void FindBestTargetAt(
            Vector2 origin,
            ref IInteractable best,
            ref float bestDistance,
            ref int bestPriority)
        {
            Physics2D.OverlapCircle(origin, radius, _filter, _hits);

            foreach (var hit in _hits)
            {
                // A collider on the player itself is never a valid target, and neither is one
                // whose interactable lives on a parent we happen to be a child of.
                if (hit.transform.IsChildOf(transform))
                    continue;

                if (hit.GetComponentInParent<IInteractable>() is not { } candidate)
                    continue;

                if (!candidate.CanInteract(gameObject))
                    continue;

                // A floor transition can overlap scenery interactions in compact door and stair
                // layouts. Once it is unlocked, entering/leaving the room is the intended action.
                var priority = candidate is SceneTransition ? 1 : 0;
                var distance = (hit.ClosestPoint(origin) - origin).sqrMagnitude;
                if (priority < bestPriority ||
                    (priority == bestPriority && distance >= bestDistance))
                    continue;

                bestPriority = priority;
                bestDistance = distance;
                best = candidate;
            }
        }

        Vector2 SearchOrigin()
        {
            var facing = _controller != null ? _controller.FacingDirection : Vector2.down;
            return (Vector2)transform.position + facing * reach;
        }

        void SetTarget(IInteractable target)
        {
            if (ReferenceEquals(target, _current))
                return;

            _current = target;
            TargetChanged?.Invoke(target);
        }

        void OnDrawGizmosSelected()
        {
            if (!showGizmo)
                return;

            Gizmos.color = _current != null ? Color.green : new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(SearchOrigin(), radius);
        }
    }
}
