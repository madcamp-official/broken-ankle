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
    /// <summary>
    /// Runs an authored story beat only after both players are staged in the same shot.
    ///
    /// In a network room the master client publishes the start and dialogue-advance counter as
    /// room properties. Both clients therefore run the same phase, while each machine moves only
    /// the character it owns and receives the other character through PlayerSync.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class StorySequenceTrigger : MonoBehaviourPunCallbacks
    {
        [Header("Story")]
        [SerializeField] string id;
        [SerializeField] string openingDialogueId;
        [SerializeField] CutsceneWaypointMover mover;
        [SerializeField] string movementDialogueId;

        [Tooltip("Seconds each line of the movement dialogue holds before moving on by itself. " +
                 "The players are being carried along a path while it plays and are not being " +
                 "asked to acknowledge it — and the beat cannot travel to the next map until the " +
                 "last line is done, so waiting on a keypress here strands them. Zero waits for " +
                 "a press like every other beat.")]
        [SerializeField] float movementAutoAdvanceSeconds = 1.8f;

        [SerializeField] string closingDialogueId;
        [SerializeField] bool emitOpeningNoise = true;
        [SerializeField] float openingNoiseRange = 11f;
        [SerializeField] string raiseFlagBeforeMovement;
        [SerializeField] string raiseFlagAfterMovement;

        [Header("Post-sequence travel")]
        [SerializeField] string transitionMap;
        [SerializeField] string transitionEntry;

        [Header("Two-player staging")]
        [SerializeField] bool requireBothPlayers = true;
        [Tooltip("World-space offset from this trigger per player slot.")]
        [SerializeField] Vector2[] stagingOffsets =
        {
            new(-0.65f, -0.4f),
            new(0.65f, -0.4f),
        };
        [Tooltip("Keeps the authored staging mark separate from a room-sized trigger volume.")]
        [SerializeField] Vector2 stagingCenterOffset;
        [SerializeField] float stagingSpeed = 3.8f;
        [SerializeField] float stagingArriveDistance = 0.06f;

        [Header("Solo development preview")]
        [SerializeField] bool allowSoloPreview = true;
        [SerializeField] Key soloPreviewKey = Key.F8;

        Collider2D _trigger;
        MapZone _zone;
        bool _running;
        bool _localPreview;
        bool _startPublished;
        bool _startHeard;
        int _advanceCounter;
        int _pendingAdvancePulses;
        bool _handedOff;
        List<PlayerRig> _lockedParticipants;

        // True while a dialogue is running itself off a clock. Presses must not be published then:
        // nothing is consuming them, and the pulses would survive into the closing dialogue and
        // flush it line by line the moment it opened.
        bool _selfAdvancing;

        /// <summary>
        /// Whether this beat is playing, and the one place that tells <see cref="StoryBeat"/> so.
        ///
        /// A property rather than the bare field because the two must not be able to disagree:
        /// every path that ends a beat — the finally, the interrupted cleanup, the handover — has
        /// to be counted, and one that was missed would leave the monster unable to touch anybody
        /// for the rest of the run.
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

        string SequenceId => string.IsNullOrEmpty(id) ? name : id;
        string CompletedFlag => "sequence:" + SequenceId;
        string StartKey => "cutscene:" + SequenceId + ":start";
        string AdvanceKey => "cutscene:" + SequenceId + ":advance";
        string ExitReadyKey(int slot) => "cutscene:" + SequenceId + ":exitReady:" + slot;

        void Reset()
        {
            var trigger = GetComponent<Collider2D>();
            trigger.isTrigger = true;
        }

        void Awake()
        {
            _trigger = GetComponent<Collider2D>();
            if (_trigger != null)
                _trigger.isTrigger = true;

            _zone = MapZone.Of(this);
        }

        IEnumerator Start()
        {
            // Room properties can arrive before an additive scene and its trigger exist.
            yield return null;
            TryAdoptNetworkStart();
        }

        void Update()
        {
            if (_running)
            {
                PublishAdvanceFromLocalPlayer();
                return;
            }

            if (WorldState.Has(CompletedFlag) || !StoryProgression.CanPlay(openingDialogueId))
                return;

            // The room has already said this beat began, and only now can this machine play it.
            //
            // The start crosses as a room property, which arrives once and is never repeated. What
            // gates the beat is a WorldState flag, which crosses as a different room property — so
            // the two race, and a client that heard the start first used to refuse it and never
            // hear about it again. That client stood through the whole scene with no dialogue while
            // their partner read it.
            if (_startHeard)
            {
                StartSequence(localPreview: false);
                return;
            }

            if (allowSoloPreview && SoloPreviewAllowed() &&
                SoloPreviewPressed() && HasControlledPlayerInside())
            {
                StartSequence(localPreview: true);
                return;
            }

            if (!ParticipantsReady())
                return;

            if (!PhotonNetwork.InRoom)
            {
                StartSequence(localPreview: false);
                return;
            }

            if (PhotonNetwork.IsMasterClient)
                PublishStart();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (changed == null)
                return;

            if (changed.TryGetValue(StartKey, out var started) && IsTrue(started))
            {
                // Remembered before it is acted on, so Update can try again if this machine is not
                // ready yet. See there.
                _startHeard = true;
                StartSequence(localPreview: false);
            }

            if (changed.TryGetValue(AdvanceKey, out var value) && TryReadInt(value, out var counter))
                AdoptAdvance(counter);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            Cleanup();
        }

        void PublishStart()
        {
            if (_startPublished || PhotonNetwork.CurrentRoom == null)
                return;

            _startPublished = true;
            _advanceCounter = 0;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { StartKey, true },
                { AdvanceKey, _advanceCounter },
            });

            // Do not wait for the room property to make a round trip to its author.
            StartSequence(localPreview: false);
        }

        void TryAdoptNetworkStart()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return;

            var properties = PhotonNetwork.CurrentRoom.CustomProperties;
            if (properties.TryGetValue(AdvanceKey, out var advance) &&
                TryReadInt(advance, out var counter))
            {
                // A client can finish loading after the other player has already advanced one or
                // more lines. Preserve those room-property pulses so it catches up instead of
                // leaving an old dialogue panel behind when the sequence ends.
                AdoptAdvance(counter);
            }

            if (properties.TryGetValue(StartKey, out var started) && IsTrue(started))
            {
                _startHeard = true;
                StartSequence(localPreview: false);
            }
        }

        void StartSequence(bool localPreview)
        {
            if (_running || WorldState.Has(CompletedFlag))
            {
                // Consumed even though it is refused. This is the author's own start echoing back
                // from the room while the beat is already under way — on the master the property
                // and the direct call both land here. Left standing, it outlives the run: Run's
                // finally clears _running before the runner raises the completed flag, and in that
                // gap Update replayed the whole beat into a map about to unload, whose destroyed
                // coroutine never gave the input lock back.
                _startHeard = false;
                return;
            }

            // Not ready is different from already done: the start and its gating WorldState flag
            // cross as separate room properties and race, so this start is kept for Update to
            // retry rather than dropped. See OnRoomPropertiesUpdate.
            if (!StoryProgression.CanPlay(openingDialogueId))
                return;

            _startHeard = false;

            _localPreview = localPreview;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Running = true;

            // Held here rather than at the call sites because a sequence is started from three
            // places, and one of them is a room property that arrives once and is never repeated.
            // Refusing it while the title screen was up would drop the beat for that client
            // entirely; waiting keeps it.
            while (Net.TitleScreen.IsUp)
                yield return null;

            var participants = ParticipantsInZone();
            SetControlledInput(participants, suspended: true);

            // Remembered for Cleanup. A destroyed coroutine never reaches its finally, and by the
            // time OnDisable runs the participants have usually left this zone, so looking them up
            // again would release nobody.
            _lockedParticipants = participants;

            var cameraTargets = new List<Transform>();
            foreach (var participant in participants)
                if (participant != null)
                    cameraTargets.Add(participant.transform);

            if (RoomCamera.Current != null)
                RoomCamera.Current.BeginGroupFrame(cameraTargets);

            try
            {
                yield return StageControlledPlayers(participants);

                var manager = DialogueManager.Ensure();
                var map = MapZone.IdOf(this);

                if (!string.IsNullOrEmpty(openingDialogueId))
                    yield return PlayAndWait(manager, openingDialogueId, emitOpeningNoise,
                                             openingNoiseRange, map);

                while (DialogueManager.IsPlaying)
                    yield return null;

                if (!string.IsNullOrEmpty(raiseFlagBeforeMovement))
                    WorldState.Raise(raiseFlagBeforeMovement);

                _selfAdvancing = movementAutoAdvanceSeconds > 0f;

                if (!string.IsNullOrEmpty(movementDialogueId))
                    manager.TryPlay(movementDialogueId, lockInput: false, emitNoise: true,
                                    noiseRange: openingNoiseRange,
                                    noisePosition: transform.position, map: map,
                                    raiseFlagOnComplete: null,
                                    // No advance source while it runs itself: the counter exists so
                                    // one player's press moves both screens on, and nobody is
                                    // pressing. Leaving it in would hand the clock a gate that only
                                    // opens on a keystroke and put the hang straight back.
                                    advanceSource: movementAutoAdvanceSeconds > 0f
                                        ? null
                                        : SynchronizedAdvanceSource(),
                                    autoAdvanceSeconds: movementAutoAdvanceSeconds);

                if (mover != null)
                    yield return mover.PlayForAllControlledPlayersRoutine();

                while (DialogueManager.IsPlaying)
                    yield return null;

                _selfAdvancing = false;

                yield return WaitForAllPlayersAtExit(participants);

                if (!string.IsNullOrEmpty(raiseFlagAfterMovement))
                    WorldState.Raise(raiseFlagAfterMovement);

                if (!string.IsNullOrEmpty(transitionMap))
                {
                    _handedOff = StoryTransitionRunner.Begin(
                        SequenceId,
                        CompletedFlag,
                        participants,
                        transitionMap,
                        transitionEntry,
                        closingDialogueId);

                    if (_handedOff)
                        yield break;
                }

                if (!string.IsNullOrEmpty(closingDialogueId))
                    yield return PlayAndWait(manager, closingDialogueId, false, 0f, map);

                WorldState.Raise(CompletedFlag);
            }
            finally
            {
                // This sequence releases the lock it acquired even when the transition runner has
                // taken over. StoryTransitionRunner acquires its own lock synchronously in Begin,
                // so control never leaks through and neither side has to guess which lock belongs
                // to the other.
                SetControlledInput(participants, suspended: false);
                _lockedParticipants = null;

                if (!_handedOff)
                {
                    if (RoomCamera.Current != null)
                        RoomCamera.Current.EndGroupFrame();
                }

                Running = false;
                _localPreview = false;
                _selfAdvancing = false;
            }
        }

        IEnumerator StageControlledPlayers(List<PlayerRig> participants)
        {
            var routines = new List<Coroutine>();

            foreach (var rig in participants)
            {
                if (rig == null || !rig.IsControlled)
                    continue;

                var point = StagingPointFor(rig);
                if (point.HasValue)
                    routines.Add(StartCoroutine(MoveTo(rig, point.Value)));
            }

            foreach (var routine in routines)
                yield return routine;
        }

        IEnumerator MoveTo(PlayerRig rig, Vector2 target)
        {
            var body = rig.GetComponent<Rigidbody2D>();
            var controller = rig.GetComponent<PlayerController>();

            while (rig != null &&
                   Vector2.Distance(rig.transform.position, target) > stagingArriveDistance)
            {
                var current = body != null ? body.position : (Vector2)rig.transform.position;
                var deltaTime = body != null ? Time.fixedDeltaTime : Time.deltaTime;
                var next = Vector2.MoveTowards(current, target, stagingSpeed * deltaTime);
                var delta = next - current;

                if (controller != null && delta.sqrMagnitude > 0.0001f)
                    controller.Drive(delta.normalized, MovementMode.Walk);

                if (body != null)
                {
                    body.MovePosition(next);
                    yield return new WaitForFixedUpdate();
                }
                else
                {
                    rig.transform.position = next;
                    yield return null;
                }
            }

            if (controller != null)
                controller.Drive(Vector2.zero, MovementMode.Walk);
        }

        Vector2? StagingPointFor(PlayerRig rig)
        {
            var inventory = rig != null ? rig.GetComponent<Inventory>() : null;
            var slot = inventory != null ? inventory.Slot : 0;

            return stagingOffsets != null && slot >= 0 && slot < stagingOffsets.Length
                ? (Vector2)transform.position + stagingCenterOffset + stagingOffsets[slot]
                : null;
        }

        IEnumerator PlayAndWait(
            DialogueManager manager,
            string dialogueId,
            bool emitNoise,
            float noiseRange,
            int map)
        {
            while (DialogueManager.IsPlaying)
                yield return null;

            if (!manager.TryPlay(dialogueId, lockInput: false, emitNoise: emitNoise,
                                 noiseRange: noiseRange, noisePosition: transform.position,
                                 map: map, raiseFlagOnComplete: null,
                                 advanceSource: SynchronizedAdvanceSource()))
            {
                Debug.LogWarning($"Could not start story dialogue '{dialogueId}'.", this);
                yield break;
            }

            while (DialogueManager.IsPlaying)
                yield return null;
        }

        Func<bool> SynchronizedAdvanceSource()
        {
            return PhotonNetwork.InRoom && !_localPreview ? NetworkAdvancePressed : null;
        }

        bool NetworkAdvancePressed()
        {
            if (_pendingAdvancePulses <= 0)
                return false;

            _pendingAdvancePulses--;
            return true;
        }

        void PublishAdvanceFromLocalPlayer()
        {
            if (_selfAdvancing || _localPreview || !PhotonNetwork.InRoom ||
                !DialogueManager.IsPlaying || !LocalAdvancePressed())
            {
                return;
            }

            var expected = _advanceCounter;
            var next = expected + 1;

            PhotonNetwork.CurrentRoom?.SetCustomProperties(
                new Hashtable { { AdvanceKey, next } },
                new Hashtable { { AdvanceKey, expected } });
        }

        void AdoptAdvance(int counter)
        {
            if (counter <= _advanceCounter)
                return;

            _pendingAdvancePulses += counter - _advanceCounter;
            _advanceCounter = counter;
        }

        bool ParticipantsReady()
        {
            var present = new bool[2];
            var count = 0;

            foreach (var rig in ParticipantsInZone())
            {
                if (_trigger == null || !_trigger.OverlapPoint(rig.transform.position))
                    continue;

                var inventory = rig.GetComponent<Inventory>();
                var slot = inventory != null ? inventory.Slot : count;
                if (slot >= 0 && slot < present.Length && !present[slot])
                {
                    present[slot] = true;
                    count++;
                }
            }

            return requireBothPlayers ? present[0] && present[1] : count > 0;
        }

        bool HasControlledPlayerInside()
        {
            foreach (var rig in ParticipantsInZone())
                if (rig.IsControlled &&
                    (_trigger == null || _trigger.OverlapPoint(rig.transform.position)))
                    return true;

            return false;
        }

        List<PlayerRig> ParticipantsInZone()
        {
            var participants = new List<PlayerRig>();

            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var presence = rig.GetComponent<MapPresence>();
                if (_zone != null && (presence == null || presence.Zone != _zone))
                    continue;

                participants.Add(rig);
            }

            participants.Sort((a, b) => SlotOf(a).CompareTo(SlotOf(b)));
            return participants;
        }

        static int SlotOf(PlayerRig rig)
        {
            var inventory = rig != null ? rig.GetComponent<Inventory>() : null;
            return inventory != null ? inventory.Slot : 0;
        }

        IEnumerator WaitForAllPlayersAtExit(List<PlayerRig> participants)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                yield break;

            var ready = new Hashtable();
            foreach (var rig in participants)
            {
                if (rig == null || !rig.IsControlled)
                    continue;

                ready[ExitReadyKey(SlotOf(rig))] = true;
            }

            if (ready.Count > 0)
                PhotonNetwork.CurrentRoom.SetCustomProperties(ready);

            while (!AllRoomPlayersAtExit())
                yield return null;
        }

        bool AllRoomPlayersAtExit()
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return true;

            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (!player.CustomProperties.TryGetValue(NetworkGame.SlotKey, out var value) ||
                    !TryReadInt(value, out var slot) ||
                    !room.CustomProperties.TryGetValue(ExitReadyKey(slot), out var ready) ||
                    !IsTrue(ready))
                {
                    return false;
                }
            }

            return true;
        }

        static void SetControlledInput(List<PlayerRig> participants, bool suspended)
        {
            foreach (var rig in participants)
                if (rig != null && rig.IsControlled)
                    rig.SuspendInput(suspended);
        }

        void Cleanup()
        {
            if (!_running)
                return;

            // Whatever else is true, an interrupted run gives its input lock back. This is the only
            // path a run killed by scene unload has — its coroutine's finally never executes — and
            // the stored list is used because the players have usually left this zone by now.
            if (_lockedParticipants != null)
            {
                SetControlledInput(_lockedParticipants, suspended: false);
                _lockedParticipants = null;
            }

            if (_handedOff)
            {
                Running = false;
                return;
            }

            if (RoomCamera.Current != null)
                RoomCamera.Current.EndGroupFrame();

            Running = false;
            _localPreview = false;
            _selfAdvancing = false;
        }

        bool SoloPreviewPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[soloPreviewKey].wasPressedThisFrame;
        }

        static bool SoloPreviewAllowed()
        {
            return !PhotonNetwork.InRoom ||
                   PhotonNetwork.CurrentRoom == null ||
                   PhotonNetwork.CurrentRoom.PlayerCount < 2;
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

        static bool IsTrue(object value) => value is bool flag && flag;

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

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.65f);
            Gizmos.DrawWireCube(transform.position, transform.localScale);

            if (stagingOffsets == null)
                return;

            Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.8f);
            foreach (var offset in stagingOffsets)
                Gizmos.DrawWireSphere((Vector2)transform.position + offset, 0.18f);
        }
    }
}
