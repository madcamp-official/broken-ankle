using System.Collections;
using Ashburn.Net;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>Runs one or more dialogue beats shortly after a scene loads.</summary>
    public class AutoDialogueSequence : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] string[] dialogueIds;
        [SerializeField] float delay = 0.35f;
        [SerializeField] bool playOnce = true;

        string CompletedFlag => "auto-dialogue:" + (string.IsNullOrEmpty(id) ? name : id);

        IEnumerator Start()
        {
            if (playOnce && WorldState.Has(CompletedFlag))
                yield break;

            // The starting map is opened while the title screen is still asking which room to play
            // in, so that joining is instant once the code is typed. Without this the briefing ran
            // itself out behind that screen against a player who was still reading a room code, and
            // playOnce then marked it seen — the beat was gone before the game had begun.
            while (TitleScreen.IsUp)
                yield return null;

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);

            foreach (var dialogueId in dialogueIds)
            {
                if (string.IsNullOrEmpty(dialogueId))
                    continue;

                while (DialogueManager.IsPlaying)
                    yield return null;

                if (!manager.TryPlay(dialogueId, lockInput: true, emitNoise: false, noiseRange: 0f,
                                     noisePosition: transform.position, map: map,
                                     raiseFlagOnComplete: null))
                {
                    Debug.LogWarning($"Could not start automatic dialogue '{dialogueId}'.", this);
                    continue;
                }

                while (DialogueManager.IsPlaying)
                    yield return null;
            }

            if (playOnce)
                WorldState.Raise(CompletedFlag);
        }
    }
}
