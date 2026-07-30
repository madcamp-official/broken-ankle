using Ashburn.Interaction;
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

        string UsedFlag => "interact:" + eventId;

        public string Prompt => prompt;

        public bool CanInteract(GameObject interactor)
        {
            if (DialogueManager.IsPlaying)
                return false;

            if (singleUse && WorldState.Has(UsedFlag))
                return false;

            return string.IsNullOrEmpty(requiredFlag) || WorldState.Has(requiredFlag);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
                return;

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);
            if (!manager.TryPlay(eventId, lockInput, emitNoise: false, noiseRange: 0f,
                                 transform.position, map, raiseFlagOnComplete))
                return;

            if (singleUse)
                WorldState.Raise(UsedFlag);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.45f);
        }
    }
}
