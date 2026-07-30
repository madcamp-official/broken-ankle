using System.Collections;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Plays a beat the moment the world becomes a certain way, wherever the players are standing.
    ///
    /// The third way a dialogue starts, next to walking into a <see cref="DialogueTrigger"/> and
    /// arriving in a map. Both of those are a place the players have to go, and the beats this is
    /// for have no such place: the hangar wakes up because Grant finished the power in one room
    /// while Nathan finished a document in another, and neither of them is walking anywhere when
    /// it happens. A trigger volume for that would mean the lights come on only if somebody
    /// remembers to cross a line on the floor, and a pair who had already crossed it would never
    /// see the moment at all.
    ///
    /// Keyed on a <see cref="WorldState"/> flag, so it fires for whoever is in the map — including
    /// the player who did none of the work, and including a machine that heard the flag arrive
    /// from the room rather than raising it.
    /// </summary>
    public class FlaggedDialogue : MonoBehaviour
    {
        [Header("When")]
        [Tooltip("The flag that starts it. Usually the output of a WorldFlagAll.")]
        [SerializeField] string flag;

        [Tooltip("Held off until a player this machine controls is in this map, so the beat is not " +
                 "spent on an empty room the other one is working in.")]
        [SerializeField] bool requirePlayerPresent = true;

        [SerializeField] float delay = 0.35f;

        [Header("Dialogue")]
        [SerializeField] string eventId;
        [SerializeField] bool playOnce = true;
        [SerializeField] bool lockInput = true;

        [Header("Noise")]
        [SerializeField] bool emitNoise;
        [SerializeField] float noiseRange = 10f;

        [Header("World state")]
        [SerializeField] string raiseFlagOnComplete;

        bool _started;

        string CompletedFlag => "dialogue:" + eventId;

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;

            // Already true when this map loads. The pair set it off before coming back here, or the
            // whole world state arrived from the room in one packet a moment ago.
            if (WorldState.Has(flag))
                Begin();
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string changed)
        {
            if (changed == flag)
                Begin();
        }

        void Begin()
        {
            if (_started || string.IsNullOrEmpty(eventId))
                return;

            if (playOnce && WorldState.Has(CompletedFlag))
                return;

            if (!StoryProgression.CanPlay(eventId))
                return;

            _started = true;
            StartCoroutine(Play());
        }

        IEnumerator Play()
        {
            var zone = MapZone.Of(this);

            if (requirePlayerPresent)
                while (!HasControlledPlayer(zone))
                    yield return null;

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            while (DialogueManager.IsPlaying)
                yield return null;

            // Checked again rather than trusting Begin's answer: this may have waited several
            // seconds for somebody to walk in, and the partner's copy of the beat could have
            // finished and crossed the wire in that time.
            if (playOnce && WorldState.Has(CompletedFlag))
            {
                _started = false;
                yield break;
            }

            var manager = DialogueManager.Ensure();
            if (!manager.TryPlay(eventId, lockInput, emitNoise, noiseRange, transform.position,
                                 MapZone.IdOf(this), raiseFlagOnComplete))
            {
                _started = false;
                Debug.LogWarning($"Could not start flagged dialogue '{eventId}'.", this);
                yield break;
            }

            if (playOnce)
                WorldState.Raise(CompletedFlag);
        }

        static bool HasControlledPlayer(MapZone zone)
        {
            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!rig.IsControlled)
                    continue;

                var presence = rig.GetComponent<MapPresence>();
                if (zone == null || (presence != null && presence.Zone == zone))
                    return true;
            }

            return false;
        }
    }
}
