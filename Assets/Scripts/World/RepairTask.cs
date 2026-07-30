using Ashburn.Cutscenes;
using Ashburn.Interaction;
using Ashburn.Noise;
using Ashburn.Player;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// A piece of plant one of the two has to stand at and work on: the elevator's motor, the
    /// hangar's main line.
    ///
    /// Grant's half of the job, and the reason the second floor and the hangar put the pair at
    /// opposite ends of a building. <c>docs/씬별_연출_체크리스트.md</c> 6절 asks for two people
    /// doing different things at once, and a repair that finishes in one tap is not something the
    /// other one has time to be somewhere else during.
    ///
    /// An <see cref="IHoldInteractable"/> rather than a minigame in its own screen. The hold is
    /// already counted by <see cref="PlayerInteractor"/> and already drawn as a filling bar, and it
    /// is the version of "this takes a while" that leaves the player looking at the room they are
    /// standing in — which is the room a warden may be walking into.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RepairTask : MonoBehaviour, IHoldInteractable
    {
        [Header("Prompts")]
        [SerializeField] string prompt = "수리한다";

        [Tooltip("Shown once it is done.")]
        [SerializeField] string finishedPrompt = "수리를 마쳤다";

        [Header("Who does it")]
        [Tooltip("-1 for either of them, 0 for Nathan, 1 for Grant. Plant is Grant's.")]
        [SerializeField] int requiredSlot = PlayerRole.Grant;

        [Header("How long")]
        [Tooltip("Seconds the key is held. Long enough that the partner gets somewhere else in it, " +
                 "short enough that being caught mid-repair is a mistake rather than a sentence.")]
        [SerializeField] float holdSeconds = 3.5f;

        [Header("What it does")]
        [Tooltip("Raised when the repair finishes. Doors and story gates read this.")]
        [SerializeField] string raiseFlagOnComplete;

        [Tooltip("Optional beat played once it is done.")]
        [SerializeField] string dialogueId;

        [Header("Noise")]
        [Tooltip("How far the work carries. Turning a building back on is loud.")]
        [SerializeField] float noiseRange = 14f;

        [Tooltip("Emitted every second of the hold as well as at the end, so being heard is a " +
                 "risk taken across the whole repair rather than a surprise at the last frame.")]
        [SerializeField] bool noisyWhileWorking = true;

        float _nextWorkNoise;

        /// <summary>Whether it has been done, by either of them, now or earlier.</summary>
        public bool IsDone => !string.IsNullOrEmpty(raiseFlagOnComplete) &&
                              WorldState.Has(raiseFlagOnComplete);

        /// <summary>
        /// How long this takes, or zero for somebody it is not the job of.
        ///
        /// Zero rather than a refusal, because a hold that will never be allowed to finish is worse
        /// than no hold at all: <see cref="PlayerInteractor"/> only begins one when this is above
        /// zero, so the wrong one of the two presses the key, nothing happens, and no bar fills to
        /// promise them otherwise. The prompt has already told them whose job it is.
        /// </summary>
        public float HoldSeconds =>
            PlayerRole.Matches(_asking, requiredSlot) ? holdSeconds : 0f;

        // Whoever is standing here, for a prompt that can name the right person. See the same
        // field on SearchableCache.Spot for why this is read from CanInteract.
        GameObject _asking;

        public string Prompt
        {
            get
            {
                if (IsDone)
                    return finishedPrompt;

                return PlayerRole.Matches(_asking, requiredSlot)
                    ? prompt
                    : PlayerRole.Refusal(requiredSlot);
            }
        }

        void Awake()
        {
            if (string.IsNullOrEmpty(raiseFlagOnComplete))
                Debug.LogWarning($"{nameof(RepairTask)} on '{name}' finishes nothing: it raises " +
                                 "no flag, so no door can be waiting on it.", this);
        }

        // Still targetable while it is the other one's job, so the wrong player is told whose it is
        // rather than finding a machine that gives no prompt at all. PlayerInteractor refuses to
        // target anything this says no to, and a silent panel reads as scenery. Interact is where
        // the role bites; HoldSeconds keeps the bar from filling on a promise it will not keep.
        public bool CanInteract(GameObject interactor)
        {
            _asking = interactor;
            return !IsDone;
        }

        void Update()
        {
            if (!noisyWhileWorking || IsDone || Time.time < _nextWorkNoise)
                return;

            // Only while somebody is actually holding this one down. HoldProgress belongs to the
            // interactor, so a second machine's player working on their own panel does not make
            // this one clatter.
            if (!IsBeingWorked())
                return;

            _nextWorkNoise = Time.time + 1f;
            NoiseBus.Emit(transform.position, noiseRange * 0.6f, NoiseKind.Self, MapZone.IdOf(this));
        }

        bool IsBeingWorked()
        {
            foreach (var interactor in FindObjectsByType<PlayerInteractor>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (interactor.HoldProgress > 0f && ReferenceEquals(interactor.CurrentTarget, this))
                    return true;
            }

            return false;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor) || !PlayerRole.Matches(interactor, requiredSlot))
                return;

            NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self, MapZone.IdOf(this));
            WorldState.Raise(raiseFlagOnComplete);

            if (string.IsNullOrEmpty(dialogueId))
                return;

            DialogueManager.Ensure().TryPlay(dialogueId, lockInput: true, emitNoise: false,
                                             noiseRange: 0f, transform.position,
                                             MapZone.IdOf(this), raiseFlagOnComplete: null);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = requiredSlot == PlayerRole.Grant
                ? new Color(0.4f, 0.85f, 1f, 0.9f)
                : new Color(1f, 0.85f, 0.4f, 0.9f);

            Gizmos.DrawWireSphere(transform.position, 0.45f);
        }
    }
}
