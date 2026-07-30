using System;
using System.Collections;
using System.Collections.Generic;
using Ashburn.Player;
using Ashburn.World;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Finishes a story sequence after its source map unloads.
    /// </summary>
    public sealed class StoryTransitionRunner : MonoBehaviourPunCallbacks
    {
        static StoryTransitionRunner _instance;

        readonly List<PlayerRig> _participants = new();
        readonly List<PlayerRig> _controlled = new();

        string _sequenceId;
        string _completedFlag;
        string _dialogueId;
        string _targetMap;
        string _targetEntry;
        string _advanceKey;
        int _advanceCounter;
        int _pendingAdvancePulses;
        bool _running;

        /// <summary>
        /// Whether the tail of a beat is playing, and the one place that tells
        /// <see cref="StoryBeat"/> so. Same reasoning as StorySequenceTrigger's.
        /// </summary>
        bool Running
        {
            get => _running;
            set
            {
                if (_running == value)
                    return;

                _running = value;

                if (value)
                    StoryBeat.Begin();
                else
                    StoryBeat.End();
            }
        }

        public static bool Begin(
            string sequenceId,
            string completedFlag,
            IEnumerable<PlayerRig> participants,
            string targetMap,
            string targetEntry,
            string dialogueId)
        {
            var runner = Ensure();
            if (runner._running || string.IsNullOrEmpty(targetMap))
                return false;

            runner.Prepare(
                sequenceId,
                completedFlag,
                participants,
                targetMap,
                targetEntry,
                dialogueId);
            runner.StartCoroutine(runner.Run());
            return true;
        }

        static StoryTransitionRunner Ensure()
        {
            if (_instance != null)
                return _instance;

            var host = new GameObject(nameof(StoryTransitionRunner))
            {
                hideFlags = HideFlags.HideInHierarchy,
            };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<StoryTransitionRunner>();
            return _instance;
        }

        void Prepare(
            string sequenceId,
            string completedFlag,
            IEnumerable<PlayerRig> participants,
            string targetMap,
            string targetEntry,
            string dialogueId)
        {
            _sequenceId = sequenceId;
            _completedFlag = completedFlag;
            _targetMap = targetMap;
            _targetEntry = targetEntry;
            _dialogueId = dialogueId;
            _advanceKey = "cutscene:" + sequenceId + ":arrivalAdvance";
            _advanceCounter = 0;
            _pendingAdvancePulses = 0;
            _participants.Clear();
            _controlled.Clear();

            foreach (var rig in participants)
            {
                if (rig == null)
                    continue;

                _participants.Add(rig);
                if (rig.IsControlled)
                    _controlled.Add(rig);
            }
        }

        void Update()
        {
            if (!_running || !PhotonNetwork.InRoom ||
                !DialogueManager.IsPlaying || !LocalAdvancePressed())
            {
                return;
            }

            var expected = _advanceCounter;
            var next = expected + 1;

            PhotonNetwork.CurrentRoom?.SetCustomProperties(
                new Hashtable { { _advanceKey, next } },
                new Hashtable { { _advanceKey, expected } });
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!_running || changed == null ||
                !changed.TryGetValue(_advanceKey, out var value) ||
                !TryReadInt(value, out var counter) ||
                counter <= _advanceCounter)
            {
                return;
            }

            _pendingAdvancePulses += counter - _advanceCounter;
            _advanceCounter = counter;
        }

        IEnumerator Run()
        {
            Running = true;

            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.CurrentRoom?.SetCustomProperties(
                        new Hashtable { { _advanceKey, 0 } });
                }

                foreach (var rig in _controlled)
                {
                    if (rig != null)
                        MapTravel.Go(rig.gameObject, _targetMap, _targetEntry);
                }

                while (!AllControlledArrived())
                    yield return null;

                while (DialogueManager.IsPlaying)
                    yield return null;

                if (!string.IsNullOrEmpty(_dialogueId))
                {
                    var manager = DialogueManager.Ensure();
                    var position = _controlled.Count > 0 && _controlled[0] != null
                        ? _controlled[0].transform.position
                        : Vector3.zero;
                    var map = _controlled.Count > 0 && _controlled[0] != null
                        ? MapZone.IdOf(_controlled[0])
                        : MapZone.Unzoned;

                    if (!manager.TryPlay(
                            _dialogueId,
                            lockInput: false,
                            emitNoise: false,
                            noiseRange: 0f,
                            noisePosition: position,
                            map: map,
                            raiseFlagOnComplete: null,
                            advanceSource: PhotonNetwork.InRoom
                                ? NetworkAdvancePressed
                                : (Func<bool>)null))
                    {
                        Debug.LogWarning(
                            $"Could not start post-transition dialogue '{_dialogueId}'.", this);
                    }

                    while (DialogueManager.IsPlaying)
                        yield return null;
                }

                if (!string.IsNullOrEmpty(_completedFlag))
                    WorldState.Raise(_completedFlag);
            }
            finally
            {
                foreach (var rig in _controlled)
                    if (rig != null)
                        rig.SuspendInput(false);

                if (RoomCamera.Current != null)
                    RoomCamera.Current.EndGroupFrame();

                _participants.Clear();
                _controlled.Clear();
                Running = false;
            }
        }

        bool AllControlledArrived()
        {
            if (_controlled.Count == 0)
                return true;

            foreach (var rig in _controlled)
            {
                if (rig == null || MapTravel.IsTravelling(rig.gameObject))
                    return false;

                var presence = rig.GetComponent<MapPresence>();
                if (presence == null || presence.MapName != _targetMap)
                    return false;
            }

            return true;
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
    }
}
