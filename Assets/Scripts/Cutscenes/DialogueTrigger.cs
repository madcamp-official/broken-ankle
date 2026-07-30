using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>Starts a story beat when a player enters a trigger volume.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DialogueTrigger : MonoBehaviour
    {
        [Header("Dialogue")]
        [SerializeField] string eventId;
        [SerializeField] bool playOnce = true;
        [SerializeField] bool lockInput = true;

        [Header("Noise")]
        [Tooltip("Dialogue can be heard by wardens. Use this on monster tutorial beats.")]
        [SerializeField] bool emitNoise;
        [SerializeField] float noiseRange = 10f;

        [Header("World state")]
        [Tooltip("Optional flag required before this can play.")]
        [SerializeField] string requiredFlag;

        [Tooltip("Optional flag raised when the dialogue finishes.")]
        [SerializeField] string raiseFlagOnComplete;

        string CompletedFlag => "dialogue:" + eventId;

        void Reset()
        {
            var trigger = GetComponent<Collider2D>();
            trigger.isTrigger = true;
        }

        void Awake()
        {
            var trigger = GetComponent<Collider2D>();
            if (trigger != null)
                trigger.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.GetComponentInParent<Player.PlayerRig>())
                return;

            Play();
        }

        public void Play()
        {
            if (string.IsNullOrEmpty(eventId))
            {
                Debug.LogWarning($"{nameof(DialogueTrigger)} on '{name}' has no event id.", this);
                return;
            }

            if (playOnce && WorldState.Has(CompletedFlag))
                return;

            if (!string.IsNullOrEmpty(requiredFlag) && !WorldState.Has(requiredFlag))
                return;

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);
            if (!manager.TryPlay(eventId, lockInput, emitNoise, noiseRange, transform.position, map,
                                 raiseFlagOnComplete))
                return;

            if (playOnce)
                WorldState.Raise(CompletedFlag);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.65f);
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}
