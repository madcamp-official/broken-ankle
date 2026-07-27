using System;
using Ashburn.Interaction;
using UnityEngine;

namespace Ashburn.Monster
{
    /// <summary>
    /// Sits on a player and handles being caught: control is taken away, the body is dragged along
    /// behind whatever grabbed it, and the only way out is a partner reaching them in time.
    ///
    /// The rescue reuses <see cref="IInteractable"/>, so a partner frees someone with the same key
    /// they use on a door. Nothing new to learn at the worst possible moment.
    /// </summary>
    public class Captive : MonoBehaviour, IInteractable
    {
        [Tooltip("Components switched off while held — movement, interaction, the flashlight.")]
        [SerializeField] MonoBehaviour[] disableWhileHeld;

        [Tooltip("Objects hidden while held, such as the beam and the hearing ring.")]
        [SerializeField] GameObject[] hideWhileHeld;

        [Tooltip("Shown to a partner standing close enough to help.")]
        [SerializeField] string rescuePrompt = "구출하기";

        /// <summary>True while this player is being dragged.</summary>
        public bool IsHeld => _captor != null;

        /// <summary>Raised when grabbed and when freed.</summary>
        public event Action<bool> HeldChanged;

        /// <summary>Raised once the captor reaches its destination and nobody arrived in time.</summary>
        public event Action Lost;

        Transform _captor;
        Rigidbody2D _body;
        RigidbodyType2D _originalBodyType;

        void Awake() => _body = GetComponent<Rigidbody2D>();

        public string Prompt => rescuePrompt;

        // Only worth offering to somebody else, and only while there is something to undo.
        public bool CanInteract(GameObject interactor) => IsHeld && interactor != gameObject;

        public void Interact(GameObject interactor)
        {
            if (CanInteract(interactor))
                Release();
        }

        public void Seize(Transform captor)
        {
            if (IsHeld)
                return;

            _captor = captor;

            // Kinematic while held: the captor drives the position, and a dynamic body would fight
            // it and jitter against the walls it is being pulled past.
            if (_body != null)
            {
                _originalBodyType = _body.bodyType;
                _body.linearVelocity = Vector2.zero;
                _body.bodyType = RigidbodyType2D.Kinematic;
            }

            SetControlEnabled(false);
            HeldChanged?.Invoke(true);
        }

        public void Release()
        {
            if (!IsHeld)
                return;

            _captor = null;

            if (_body != null)
                _body.bodyType = _originalBodyType;

            SetControlEnabled(true);
            HeldChanged?.Invoke(false);
        }

        /// <summary>Called by the captor once it has hauled this player all the way home.</summary>
        public void MarkLost()
        {
            if (!IsHeld)
                return;

            Lost?.Invoke();

            // Whatever losing turns out to mean belongs to whoever handles the event. Until then,
            // hand control back rather than leave a player frozen with nothing holding them.
            Release();
        }

        void SetControlEnabled(bool enabled)
        {
            foreach (var component in disableWhileHeld)
                if (component != null)
                    component.enabled = enabled;

            foreach (var go in hideWhileHeld)
                if (go != null)
                    go.SetActive(enabled);
        }
    }
}
