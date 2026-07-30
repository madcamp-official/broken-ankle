using System;
using System.Collections;
using System.Collections.Generic;
using Ashburn.Interaction;
using Ashburn.Net;
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
        string ArrivalReadyPrefix => "arrival-dialogue:" + eventId + ":present:";

        bool _networkPlaying;
        bool _startSeen;
        bool _doneSeen;
        int _advanceCounter;
        int _pendingAdvancePulses;
        readonly List<PlayerRig> _arrivalLocks = new();
        readonly HashSet<int> _publishedArrivalSlots = new();

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

            // Lock on the arrival frame, before the delay or network wait can keep reading a held
            // movement key.
            HoldArrivedPlayers(zone);

            if (PhotonNetwork.InRoom)
                Adopt(PhotonNetwork.CurrentRoom?.CustomProperties);

            if (playOnce && WorldState.Has(CompletedFlag))
            {
                ReleaseArrivalLocks();
                yield break;
            }

            if (!string.IsNullOrEmpty(requiredFlag) && !WorldState.Has(requiredFlag))
            {
                ReleaseArrivalLocks();
                yield break;
            }

            if (!StoryProgression.CanPlay(eventId))
            {
                ReleaseArrivalLocks();
                yield break;
            }

            if (delay > 0f)
            {
                var remaining = delay;
                while (remaining > 0f)
                {
                    HoldArrivedPlayers(zone);
                    remaining -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (PhotonNetwork.InRoom)
            {
                PublishControlledArrivals(zone);

                if (_doneSeen)
                {
                    ReleaseArrivalLocks();
                    yield break;
                }

                // A map loads independently on each machine. The host can arrive while the other
                // client is still on its loading frame, so publishing immediately would let the
                // whole conversation finish before that client even owns this component.
                while (!AllRoomPlayersPresent() && !_doneSeen)
                {
                    HoldArrivedPlayers(zone);
                    PublishControlledArrivals(zone);
                    yield return null;
                }

                if (PhotonNetwork.IsMasterClient && !_startSeen)
                    PublishStart();

                while (!_startSeen && !_doneSeen)
                {
                    HoldArrivedPlayers(zone);
                    yield return null;
                }

                if (_doneSeen)
                {
                    ReleaseArrivalLocks();
                    yield break;
                }
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
                ReleaseArrivalLocks();
                Debug.LogWarning($"Could not start arrival dialogue '{eventId}'.", this);
                yield break;
            }

            // DialogueManager acquired its own lock synchronously. Release the arrival lock only
            // after that handoff, leaving no frame in which the held key can leak through.
            ReleaseArrivalLocks();

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

        void HoldArrivedPlayers(MapZone zone)
        {
            foreach (var rig in PlayerRig.All)
            {
                if (rig == null || !rig.IsControlled || _arrivalLocks.Contains(rig))
                    continue;

                var presence = rig.GetComponent<MapPresence>();
                if (presence == null || presence.Zone != zone)
                    continue;

                rig.SuspendInput(true);
                _arrivalLocks.Add(rig);
            }
        }

        void ReleaseArrivalLocks()
        {
            foreach (var rig in _arrivalLocks)
                if (rig != null)
                    rig.SuspendInput(false);

            _arrivalLocks.Clear();
        }

        public override void OnDisable()
        {
            ReleaseArrivalLocks();
            base.OnDisable();
        }

        void PublishControlledArrivals(MapZone zone)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return;

            var arrivals = new Hashtable();
            foreach (var rig in PlayerRig.All)
            {
                if (rig == null || !rig.IsControlled)
                    continue;

                var presence = rig.GetComponent<MapPresence>();
                var slot = SlotOf(rig);
                if (presence != null && presence.Zone == zone &&
                    _publishedArrivalSlots.Add(slot))
                {
                    arrivals[ArrivalReadyKey(slot)] = true;
                }
            }

            if (arrivals.Count > 0)
                PhotonNetwork.CurrentRoom.SetCustomProperties(arrivals);
        }

        bool AllRoomPlayersPresent()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return true;

            var room = PhotonNetwork.CurrentRoom;
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (!player.CustomProperties.TryGetValue(NetworkGame.SlotKey, out var value) ||
                    !TryReadInt(value, out var slot) ||
                    !room.CustomProperties.TryGetValue(ArrivalReadyKey(slot), out var arrived) ||
                    arrived is not bool ready || !ready)
                {
                    return false;
                }
            }

            return true;
        }

        string ArrivalReadyKey(int slot) => ArrivalReadyPrefix + slot;

        static int SlotOf(PlayerRig rig)
        {
            var inventory = rig != null ? rig.GetComponent<Inventory>() : null;
            return inventory != null ? inventory.Slot : 0;
        }
    }
}
