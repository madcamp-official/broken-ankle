using System;
using System.Collections;
using Ashburn.Player;
using Ashburn.World;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Ashburn.Cutscenes
{
    /// <summary>Plays a dialogue when a locally controlled player arrives in this map.</summary>
    [RequireComponent(typeof(MapZone))]
    public class MapArrivalDialogue : MonoBehaviourPunCallbacks
    {
        [SerializeField] string eventId;
        [SerializeField] bool playOnce = true;
        [SerializeField] bool lockInput = true;
        [SerializeField] float delay = 0.15f;
        [SerializeField] string requiredFlag;
        [SerializeField] string raiseFlagOnComplete;

        string CompletedFlag => "dialogue:" + eventId;
        string StartKey => "arrival-dialogue:" + eventId + ":start";
        string AdvanceKey => "arrival-dialogue:" + eventId + ":advance";
        string DoneKey => "arrival-dialogue:" + eventId + ":done";

        bool _networkPlaying;
        bool _startSeen;
        bool _doneSeen;
        int _advanceCounter;
        int _pendingAdvancePulses;

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

            if (PhotonNetwork.InRoom)
                Adopt(PhotonNetwork.CurrentRoom?.CustomProperties);

            if (playOnce && WorldState.Has(CompletedFlag))
                yield break;

            if (!string.IsNullOrEmpty(requiredFlag) && !WorldState.Has(requiredFlag))
                yield break;

            if (!StoryProgression.CanPlay(eventId))
                yield break;

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (PhotonNetwork.InRoom)
            {
                if (_doneSeen)
                    yield break;

                // A map loads independently on each machine. The host can arrive while the other
                // client is still on its loading frame, so publishing immediately would let the
                // whole conversation finish before that client even owns this component.
                while (!AllRoomPlayersPresent(zone) && !_doneSeen)
                    yield return null;

                if (PhotonNetwork.IsMasterClient && !_startSeen)
                    PublishStart();

                while (!_startSeen && !_doneSeen)
                    yield return null;

                if (_doneSeen)
                    yield break;
            }

            while (DialogueManager.IsPlaying)
                yield return null;

            var manager = DialogueManager.Ensure();
            _networkPlaying = PhotonNetwork.InRoom;
            if (!manager.TryPlay(eventId, lockInput, emitNoise: false, noiseRange: 0f,
                                 noisePosition: transform.position, map: zone.MapId,
                                 raiseFlagOnComplete: raiseFlagOnComplete,
                                 advanceSource: _networkPlaying ? NetworkAdvancePressed : null))
            {
                _networkPlaying = false;
                Debug.LogWarning($"Could not start arrival dialogue '{eventId}'.", this);
                yield break;
            }

            while (DialogueManager.IsPlaying)
                yield return null;

            _networkPlaying = false;

            if (playOnce)
                WorldState.Raise(CompletedFlag);

            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
                PhotonNetwork.CurrentRoom?.SetCustomProperties(
                    new Hashtable { { DoneKey, true } });
        }

        void Update()
        {
            if (!_networkPlaying || !PhotonNetwork.InRoom || !LocalAdvancePressed())
                return;

            var expected = _advanceCounter;
            var next = expected + 1;

            // Compare-and-swap turns simultaneous presses into one shared pulse instead of two
            // clients overwriting the same room property with competing values.
            PhotonNetwork.CurrentRoom?.SetCustomProperties(
                new Hashtable { { AdvanceKey, next } },
                new Hashtable { { AdvanceKey, expected } });
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            Adopt(changed);
        }

        void PublishStart()
        {
            _startSeen = true;
            _advanceCounter = 0;
            PhotonNetwork.CurrentRoom?.SetCustomProperties(new Hashtable
            {
                { StartKey, true },
                { AdvanceKey, 0 },
                { DoneKey, false },
            });
        }

        void Adopt(Hashtable properties)
        {
            if (properties == null)
                return;

            if (properties.TryGetValue(StartKey, out var started) &&
                started is bool start && start)
            {
                _startSeen = true;
            }

            if (properties.TryGetValue(DoneKey, out var done) &&
                done is bool complete && complete)
            {
                _doneSeen = true;
            }

            if (!properties.TryGetValue(AdvanceKey, out var value) ||
                !TryReadInt(value, out var counter) || counter <= _advanceCounter)
            {
                return;
            }

            _pendingAdvancePulses += counter - _advanceCounter;
            _advanceCounter = counter;
        }

        bool NetworkAdvancePressed()
        {
            if (_pendingAdvancePulses <= 0)
                return false;

            _pendingAdvancePulses--;
            return true;
        }

        static bool LocalAdvancePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        static bool TryReadInt(object value, out int result)
        {
            switch (value)
            {
                case int number:
                    result = number;
                    return true;
                case byte number:
                    result = number;
                    return true;
                default:
                    result = 0;
                    return false;
            }
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

        static bool AllRoomPlayersPresent(MapZone zone)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return HasControlledPlayer(zone);

            var present = 0;
            foreach (var rig in PlayerRig.All)
            {
                if (rig == null)
                    continue;

                var presence = rig.GetComponent<MapPresence>();
                if (presence != null && presence.Zone == zone)
                    present++;
            }

            return present >= PhotonNetwork.CurrentRoom.PlayerCount;
        }
    }
}
