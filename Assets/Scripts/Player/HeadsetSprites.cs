using System.Collections.Generic;
using UnityEngine;

namespace Ashburn.Player
{
    /// <summary>
    /// Shows on the character's body whether the headset is on their head.
    ///
    /// The ring of hearing used to be the only sign of it, and the ring is drawn for the person
    /// wearing it and nobody else. That made taking the headset off a private act: the partner
    /// standing in the same room could not tell, and a headset is the one piece of equipment the
    /// story hangs on. Now it is on the body, where both players can see it.
    ///
    /// The two sprite sheets are drawn the same way — same grid, same order, same frame per
    /// direction — and differ only in the head. So this swaps the sprite the animator just wrote
    /// for its twin, and the animation itself never has to know. The alternative, a second set of
    /// clips and a controller to switch between, doubles the clip assets per character and restarts
    /// the walk cycle every time somebody presses the key.
    /// </summary>
    public class HeadsetSprites : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The switch to follow. Left empty it is looked for on this object.")]
        [SerializeField] HearingRingToggle headset;

        [Tooltip("The character sprite the animator drives.")]
        [SerializeField] SpriteRenderer body;

        [Header("Frames")]
        [Tooltip("The frames the animation clips use — the headset on. Same order as Bare.")]
        [SerializeField] Sprite[] worn;

        [Tooltip("The same frames with the headset off, in the same order as Worn.")]
        [SerializeField] Sprite[] bare;

        readonly Dictionary<Sprite, Sprite> _bareFor = new();

        bool _on = true;

        void Awake()
        {
            if (headset == null)
                headset = GetComponent<HearingRingToggle>();

            if (headset == null)
                Debug.LogError($"{nameof(HeadsetSprites)} on '{name}' has no " +
                               $"{nameof(HearingRingToggle)} to follow.", this);

            if (body == null)
                Debug.LogError($"{nameof(HeadsetSprites)} on '{name}' has no body sprite.", this);

            if (worn.Length != bare.Length)
            {
                // A mismatch would silently leave the tail of the longer sheet unswapped, which
                // looks like the headset flickering back on when the character turns.
                Debug.LogError($"{nameof(HeadsetSprites)} on '{name}' has {worn.Length} worn frames " +
                               $"and {bare.Length} bare ones. They pair up by position, so the two " +
                               "lists must be the same length and in the same order.", this);
                return;
            }

            for (var i = 0; i < worn.Length; i++)
                if (worn[i] != null && bare[i] != null)
                    _bareFor[worn[i]] = bare[i];
        }

        void OnEnable()
        {
            if (headset == null)
                return;

            headset.Switched += Apply;

            // Read the current state rather than waiting for the next press. A character that
            // spawns, or comes back from a map change, would otherwise show whatever the last
            // animation frame left behind.
            Apply(headset.IsOn);
        }

        void OnDisable()
        {
            if (headset != null)
                headset.Switched -= Apply;
        }

        void Apply(bool on) => _on = on;

        /// <summary>
        /// After the animator has written this frame's sprite, and before anything draws it.
        ///
        /// Every frame rather than only on the press, because the animator writes the worn sprite
        /// again on each one and would put the headset straight back on the character's head.
        /// </summary>
        void LateUpdate()
        {
            if (_on || body == null || body.sprite == null)
                return;

            if (_bareFor.TryGetValue(body.sprite, out var without))
                body.sprite = without;
        }
    }
}
