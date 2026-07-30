using System.Collections;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Runs a simple authored beat: dialogue, optional waypoint movement, then optional dialogue.
    /// Used for the first company escape before a full Timeline pass exists.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class StorySequenceTrigger : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] string openingDialogueId;
        [SerializeField] CutsceneWaypointMover mover;
        [SerializeField] string closingDialogueId;
        [SerializeField] bool emitOpeningNoise = true;
        [SerializeField] float openingNoiseRange = 11f;

        bool _running;

        string CompletedFlag => "sequence:" + (string.IsNullOrEmpty(id) ? name : id);

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

            if (_running || WorldState.Has(CompletedFlag))
                return;

            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            _running = true;
            WorldState.Raise(CompletedFlag);

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);

            if (!string.IsNullOrEmpty(openingDialogueId))
            {
                manager.TryPlay(openingDialogueId, lockInput: true, emitNoise: emitOpeningNoise,
                                noiseRange: openingNoiseRange, noisePosition: transform.position,
                                map: map, raiseFlagOnComplete: null);
                while (DialogueManager.IsPlaying)
                    yield return null;
            }

            if (mover != null)
                yield return mover.PlayForAllControlledPlayersRoutine();

            if (!string.IsNullOrEmpty(closingDialogueId))
            {
                manager.TryPlay(closingDialogueId, lockInput: true, emitNoise: false,
                                noiseRange: 0f, noisePosition: transform.position,
                                map: map, raiseFlagOnComplete: null);
                while (DialogueManager.IsPlaying)
                    yield return null;
            }

            _running = false;
        }
    }
}
