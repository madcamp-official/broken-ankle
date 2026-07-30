using System;
using Ashburn.Audio;
using Ashburn.Interaction;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>Starts a dialogue beat from an interactable prop such as a document or terminal.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DialogueInteractable : MonoBehaviour, IInteractable
    {
        [Header("Prompt")]
        [SerializeField] string prompt = "조사한다";

        [Header("Dialogue")]
        [SerializeField] string eventId;
        [SerializeField] bool singleUse = true;
        [SerializeField] bool lockInput = true;

        [Header("World state")]
        [SerializeField] string requiredFlag;
        [SerializeField] string raiseFlagOnComplete;

        [Header("Who may use it")]
        [Tooltip("-1 for either of them, 0 for Nathan, 1 for Grant. Records and sound gear are " +
                 "Nathan's, plant is Grant's — see PlayerRole.")]
        [SerializeField] int requiredSlot = PlayerRole.Anyone;

        // Whoever is standing here, for a prompt that can name the right person. See the same
        // field on SearchableCache.Spot for why this is read from CanInteract.
        GameObject _asking;

        string UsedFlag => "interact:" + eventId;

        public string Prompt => PlayerRole.Matches(_asking, requiredSlot)
            ? prompt
            : PlayerRole.Refusal(requiredSlot);

        public bool CanInteract(GameObject interactor)
        {
            _asking = interactor;

            if (DialogueManager.IsPlaying)
                return false;

            if (singleUse && WorldState.Has(UsedFlag))
                return false;

            if (!string.IsNullOrEmpty(requiredFlag) && !WorldState.Has(requiredFlag))
                return false;

            // Deliberately not the role check. The wrong one of the two still targets this so the
            // prompt can tell them whose job it is; Interact is where the refusal bites.
            return StoryProgression.CanPlay(eventId);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor) || !PlayerRole.Matches(interactor, requiredSlot))
                return;

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);
            if (!manager.TryPlay(eventId, lockInput, emitNoise: false, noiseRange: 0f,
                                 transform.position, map, raiseFlagOnComplete))
                return;

            if (UsesPaper(eventId))
                GameAudio.PlayPaper(transform.position, map);

            if (singleUse)
                WorldState.Raise(UsedFlag);
        }

        static bool UsesPaper(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            return id.Contains("record", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("doc", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("files", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("calls", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("floor_plan", StringComparison.OrdinalIgnoreCase);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.45f);
        }
    }
}
