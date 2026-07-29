using Ashburn.Interaction;
using Ashburn.Noise;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// A door inside a map that will not open until the pair are carrying the right thing.
    ///
    /// <see cref="MapDoor"/> is the other kind, and they are not the same job: that one carries a
    /// character into another scene, this one is a wall that stops being a wall. The greybox cuts
    /// room-to-room openings as holes with nothing in them, which is why the hospital's locked rooms
    /// and the police station's evidence store had no way of being locked at all.
    ///
    /// What it wants is named, not referenced — a keycard found in one scene cannot be pointed at by
    /// a door in another. <see cref="WorldState"/> holds both ends of that name.
    ///
    /// Opening is permanent and shared. It is world state rather than an event, so the partner two
    /// rooms away finds it open when they arrive rather than walking into a door that is only open
    /// on somebody else's screen.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LockedDoor : MonoBehaviour, IInteractable
    {
        [Header("Identity")]
        [Tooltip("Unique within the whole game — it is what remembers this door is open. " +
                 "'Hospital_Records', 'Police_Evidence'.")]
        [SerializeField] string id;

        [Header("What opens it")]
        [Tooltip("Name of the key the pair must be carrying. Empty means it opens for anyone, " +
                 "which is a door that is only stiff.")]
        [SerializeField] string requiredKey;

        [Header("Prompts")]
        [SerializeField] string prompt = "연다";

        [Tooltip("Shown while it will not open. Say what is missing — a door that only says " +
                 "'잠김' sends the player back over ground they have already searched.")]
        [SerializeField] string lockedPrompt = "잠겨 있다";

        [Header("What opening does")]
        [Tooltip("Turned off once it is open: the door leaf and whatever else stands in the way. " +
                 "This object's own collider is switched off as well and needs no entry here.")]
        [SerializeField] GameObject[] hideWhenOpen;

        [Tooltip("How far the sound of it carries. A door is not a quiet way through a wall.")]
        [SerializeField] float noiseRange = 9f;

        Collider2D _blocker;

        /// <summary>Whether it has been opened, now or by whoever got here first.</summary>
        public bool IsOpen => WorldState.Has(Flag);

        string Flag => "door:" + (string.IsNullOrEmpty(id) ? name : id);

        public string Prompt => WorldState.HasKey(requiredKey) || string.IsNullOrEmpty(requiredKey)
            ? prompt
            : lockedPrompt;

        void Awake()
        {
            _blocker = GetComponent<Collider2D>();

            if (string.IsNullOrEmpty(id))
                Debug.LogWarning($"{nameof(LockedDoor)} on '{name}' has no id, so it is remembered " +
                                 "by its object name. Two doors named the same would open together.",
                                 this);
        }

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;

            // Already open when this map loads, because the pair opened it an hour ago or because
            // the other machine opened it while this one was elsewhere.
            if (IsOpen)
                Open(silent: true);
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string flag)
        {
            if (flag == Flag)
                Open(silent: false);
        }

        // The prompt still shows while locked, so the player learns the door is a door and that it
        // is the lock stopping them rather than the level having no way on.
        public bool CanInteract(GameObject interactor) => !IsOpen;

        public void Interact(GameObject interactor)
        {
            if (IsOpen)
                return;

            // Heard whether or not it opens. Rattling a locked door is a noise somebody made.
            NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self, MapZone.IdOf(this));

            if (!string.IsNullOrEmpty(requiredKey) && !WorldState.HasKey(requiredKey))
                return;

            // Raising the flag is the whole of it. This door hears its own flag back through
            // OnFlagSet, and so does the copy of it on the other machine.
            WorldState.Raise(Flag);
        }

        void Open(bool silent)
        {
            if (_blocker != null)
                _blocker.enabled = false;

            foreach (var go in hideWhenOpen)
                if (go != null)
                    go.SetActive(false);

            if (!silent)
                NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self, MapZone.IdOf(this));
        }

        void OnDrawGizmos()
        {
            Gizmos.color = string.IsNullOrEmpty(requiredKey)
                ? new Color(0.5f, 1f, 0.6f, 0.9f)
                : new Color(1f, 0.75f, 0.3f, 0.9f);

            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.9f);
        }
    }
}
