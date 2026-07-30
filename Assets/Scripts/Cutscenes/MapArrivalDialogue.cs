using System.Collections;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>Plays a dialogue when a locally controlled player arrives in this map.</summary>
    [RequireComponent(typeof(MapZone))]
    public class MapArrivalDialogue : MonoBehaviour
    {
        [SerializeField] string eventId;
        [SerializeField] bool playOnce = true;
        [SerializeField] bool lockInput = true;
        [SerializeField] float delay = 0.15f;
        [SerializeField] string requiredFlag;
        [SerializeField] string raiseFlagOnComplete;

        string CompletedFlag => "dialogue:" + eventId;

        IEnumerator Start()
        {
            if (string.IsNullOrEmpty(eventId))
            {
                Debug.LogWarning($"{nameof(MapArrivalDialogue)} on '{name}' has no event id.", this);
                yield break;
            }

            var zone = GetComponent<MapZone>();

            while (!HasControlledPlayer(zone))
                yield return null;

            if (playOnce && WorldState.Has(CompletedFlag))
                yield break;

            if (!string.IsNullOrEmpty(requiredFlag) && !WorldState.Has(requiredFlag))
                yield break;

            if (!StoryProgression.CanPlay(eventId))
                yield break;

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            while (DialogueManager.IsPlaying)
                yield return null;

            var manager = DialogueManager.Ensure();
            if (!manager.TryPlay(eventId, lockInput, emitNoise: false, noiseRange: 0f,
                                 noisePosition: transform.position, map: zone.MapId,
                                 raiseFlagOnComplete: raiseFlagOnComplete))
            {
                Debug.LogWarning($"Could not start arrival dialogue '{eventId}'.", this);
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
                if (presence != null && presence.Zone == zone)
                    return true;
            }

            return false;
        }
    }
}
