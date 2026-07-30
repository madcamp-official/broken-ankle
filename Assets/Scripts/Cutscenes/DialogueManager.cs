using System.Collections;
using System.Collections.Generic;
using System;
using Ashburn.Core;
using Ashburn.Noise;
using Ashburn.Player;
using Ashburn.World;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Stopgap dialogue UI and flow controller.
    ///
    /// It intentionally draws with IMGUI like the existing menu/prompt screens, so story beats can
    /// be tested before a final Canvas, TMP font, portraits, and animation polish exist.
    /// </summary>
    public class DialogueManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        const byte SharedRequestEventCode = 91;
        const string SharedEventKey = "shared-dialogue:event";
        const string SharedSerialKey = "shared-dialogue:serial";
        const string SharedAdvanceKey = "shared-dialogue:advance";
        const string SharedDoneKey = "shared-dialogue:done";
        const string SharedLockKey = "shared-dialogue:lock";
        const string SharedNoiseKey = "shared-dialogue:noise";
        const string SharedNoiseRangeKey = "shared-dialogue:noiseRange";
        const string SharedNoiseXKey = "shared-dialogue:noiseX";
        const string SharedNoiseYKey = "shared-dialogue:noiseY";
        const string SharedMapKey = "shared-dialogue:map";
        const string SharedRaiseKey = "shared-dialogue:raise";

        [Header("Input")]
        [SerializeField] Key advanceKey = Key.Space;
        [SerializeField] Key alternateAdvanceKey = Key.Enter;

        [Header("Text")]
        [SerializeField] int speakerFontSize = 13;
        [SerializeField] int textFontSize = 14;
        [SerializeField] float lettersPerSecond = 34f;

        [Header("Look")]
        [SerializeField] int guiDepth = -800;
        [SerializeField] Color panelColour = new(0.03f, 0.03f, 0.04f, 0.92f);
        [SerializeField] Color lineColour = new(0.67f, 0.55f, 0.38f, 0.95f);
        [SerializeField] Color speakerColour = new(0.95f, 0.9f, 0.8f);
        [SerializeField] Color textColour = new(0.96f, 0.96f, 0.98f);
        [SerializeField] Vector2 panelSize = new(540f, 92f);
        [SerializeField] float bottomMargin = 18f;

        public static DialogueManager Current { get; private set; }
        public static bool IsPlaying =>
            Current != null && (Current._playing || Current._waitingForSharedStart);

        public static event Action<string> Finished;

        DialogueLine[] _lines;
        DialogueLine _line;
        Coroutine _routine;
        GUIStyle _speakerStyle;
        GUIStyle _textStyle;
        int _lineIndex;
        int _visibleCharacters;
        bool _playing;
        bool _revealing;
        bool _lockInput;
        bool _advanceAfterReveal;
        Func<bool> _advanceSource;
        string _playingEventId;

        bool _sharedNetworkPlaying;
        bool _sharedStartPending;
        bool _waitingForSharedStart;
        int _sharedSerial = -1;
        int _sharedAdvanceCounter;
        int _sharedPendingAdvancePulses;
        string _pendingSharedEventId;
        bool _pendingSharedLockInput;
        bool _pendingSharedEmitNoise;
        float _pendingSharedNoiseRange;
        Vector2 _pendingSharedNoisePosition;
        int _pendingSharedMap;
        string _pendingSharedRaiseFlag;
        string _requestedSharedEventId;
        string _activeSharedEventId;
        SharedRequest _outgoingSharedRequest;
        readonly Queue<SharedRequest> _sharedRequests = new();
        readonly HashSet<string> _queuedSharedEvents = new(StringComparer.Ordinal);

        sealed class SharedRequest
        {
            public string EventId;
            public bool LockInput;
            public bool EmitNoise;
            public float NoiseRange;
            public Vector2 NoisePosition;
            public int Map;
            public string RaiseFlag;
        }

        void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        void Update()
        {
            TryPublishNextSharedDialogue();
            TryStartPendingSharedDialogue();

            if (_sharedNetworkPlaying && PhotonNetwork.InRoom && LocalAdvancePressed())
                PublishSharedAdvance();
        }

        public override void OnJoinedRoom()
        {
            AdoptSharedRoomState();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changedProperties)
        {
            AdoptSharedRoomState();
        }

        public override void OnLeftRoom()
        {
            _sharedStartPending = false;
            _sharedNetworkPlaying = false;
            _sharedPendingAdvancePulses = 0;
            _waitingForSharedStart = false;
            _requestedSharedEventId = null;
            _activeSharedEventId = null;
            _outgoingSharedRequest = null;
            _sharedRequests.Clear();
            _queuedSharedEvents.Clear();

            // A room disappearing halfway through a line should leave a playable solo dialogue,
            // not one waiting forever for the next network pulse.
            if (_playing && _advanceSource == NetworkAdvancePressed)
                _advanceSource = null;
        }

        public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
        {
            if (_waitingForSharedStart && !string.IsNullOrEmpty(_requestedSharedEventId))
                ResendSharedRequest();
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != SharedRequestEventCode ||
                !PhotonNetwork.IsMasterClient ||
                photonEvent.CustomData is not object[] data ||
                !TryReadRequest(data, out var request))
            {
                return;
            }

            EnqueueSharedRequest(request);
            TryPublishNextSharedDialogue();
        }

        public static DialogueManager Ensure()
        {
            if (Current != null)
                return Current;

            var go = new GameObject("DialogueManager");
            return go.AddComponent<DialogueManager>();
        }

        /// <summary>
        /// Closes a local copy after the authoritative network flow has completed this event.
        /// This is a loading-lag fallback; under normal timing both copies finish from the same
        /// advance counter before the done property arrives.
        /// </summary>
        public bool FinishNetworkPlayback(string eventId)
        {
            if (!_playing || _playingEventId != eventId)
                return false;

            if (_routine != null)
                StopCoroutine(_routine);

            Finish();
            return true;
        }

        public bool TryPlay(
            string eventId,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete,
            Func<bool> advanceSource = null,
            float autoAdvanceSeconds = 0f)
        {
            if (ShouldSynchronize(eventId, advanceSource))
            {
                return RequestSharedDialogue(
                    eventId,
                    lockInput,
                    emitNoise,
                    noiseRange,
                    noisePosition,
                    map,
                    raiseFlagOnComplete);
            }

            return TryPlayLocal(
                eventId,
                lockInput,
                emitNoise,
                noiseRange,
                noisePosition,
                map,
                raiseFlagOnComplete,
                advanceSource,
                autoAdvanceSeconds);
        }

        bool TryPlayLocal(
            string eventId,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete,
            Func<bool> advanceSource,
            float autoAdvanceSeconds)
        {
            if (_playing)
                return false;

            if (!StoryProgression.CanPlay(eventId))
                return false;

            if (!DialogueCatalog.TryGet(eventId, out var lines) || lines.Length == 0)
            {
                Debug.LogWarning($"No dialogue lines found for event id '{eventId}'.", this);
                return false;
            }

            _routine = StartCoroutine(Play(lines, lockInput, emitNoise, noiseRange, noisePosition,
                                           map, raiseFlagOnComplete, eventId, advanceSource,
                                           autoAdvanceSeconds));
            return true;
        }

        static bool ShouldSynchronize(string eventId, Func<bool> advanceSource)
        {
            if (!PhotonNetwork.InRoom || advanceSource != null || string.IsNullOrEmpty(eventId))
                return false;

            // This line auto-advances inside StorySequenceTrigger on both machines. Every other
            // custom company beat supplies its own advance source and was already excluded above.
            return eventId != "corp_escape_001";
        }

        bool RequestSharedDialogue(
            string eventId,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete)
        {
            if (PhotonNetwork.CurrentRoom == null)
                return false;

            if (!StoryProgression.CanPlay(eventId))
                return false;

            if (!DialogueCatalog.TryGet(eventId, out var lines) || lines.Length == 0)
            {
                Debug.LogWarning($"No dialogue lines found for event id '{eventId}'.", this);
                return false;
            }

            var request = new SharedRequest
            {
                EventId = eventId,
                LockInput = lockInput,
                EmitNoise = emitNoise,
                NoiseRange = noiseRange,
                NoisePosition = noisePosition,
                Map = map,
                RaiseFlag = raiseFlagOnComplete,
            };

            if (PhotonNetwork.IsMasterClient)
            {
                EnqueueSharedRequest(request);
                TryPublishNextSharedDialogue();
                return true;
            }

            _outgoingSharedRequest = request;
            _requestedSharedEventId = eventId;
            _waitingForSharedStart = SendSharedRequest(request);
            if (!_waitingForSharedStart)
            {
                _outgoingSharedRequest = null;
                _requestedSharedEventId = null;
            }
            return _waitingForSharedStart;
        }

        void EnqueueSharedRequest(SharedRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.EventId) ||
                _queuedSharedEvents.Contains(request.EventId))
            {
                return;
            }

            _queuedSharedEvents.Add(request.EventId);
            _sharedRequests.Enqueue(request);
        }

        void TryPublishNextSharedDialogue()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient ||
                PhotonNetwork.CurrentRoom == null || _playing || _sharedStartPending ||
                _sharedNetworkPlaying || _sharedRequests.Count == 0)
            {
                return;
            }

            while (_sharedRequests.Count > 0)
            {
                var request = _sharedRequests.Peek();

                // The request and its prerequisite WorldState flags are separate reliable
                // messages. Keep it queued until the flags catch up instead of silently dropping
                // the conversation on the faster packet.
                if (!StoryProgression.CanPlay(request.EventId))
                    return;

                _sharedRequests.Dequeue();
                if (!DialogueCatalog.TryGet(request.EventId, out var lines) || lines.Length == 0)
                {
                    _queuedSharedEvents.Remove(request.EventId);
                    continue;
                }

                var room = PhotonNetwork.CurrentRoom;
                var roomSerial = ReadInt(room.CustomProperties, SharedSerialKey, -1);
                var serial = Mathf.Max(roomSerial, _sharedSerial) + 1;
                _activeSharedEventId = request.EventId;

                room.SetCustomProperties(new Hashtable
                {
                    { SharedEventKey, request.EventId },
                    { SharedSerialKey, serial },
                    { SharedAdvanceKey, 0 },
                    { SharedDoneKey, serial - 1 },
                    { SharedLockKey, request.LockInput },
                    { SharedNoiseKey, request.EmitNoise },
                    { SharedNoiseRangeKey, request.NoiseRange },
                    { SharedNoiseXKey, request.NoisePosition.x },
                    { SharedNoiseYKey, request.NoisePosition.y },
                    { SharedMapKey, request.Map },
                    { SharedRaiseKey, request.RaiseFlag ?? string.Empty },
                });

                QueueSharedDialogue(
                    serial,
                    request.EventId,
                    request.LockInput,
                    request.EmitNoise,
                    request.NoiseRange,
                    request.NoisePosition,
                    request.Map,
                    request.RaiseFlag);
                TryStartPendingSharedDialogue();
                return;
            }
        }

        bool SendSharedRequest(SharedRequest request)
        {
            var data = new object[]
            {
                request.EventId,
                request.LockInput,
                request.EmitNoise,
                request.NoiseRange,
                request.NoisePosition.x,
                request.NoisePosition.y,
                request.Map,
                request.RaiseFlag ?? string.Empty,
            };

            return PhotonNetwork.RaiseEvent(
                SharedRequestEventCode,
                data,
                new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                SendOptions.SendReliable);
        }

        void ResendSharedRequest()
        {
            if (_outgoingSharedRequest == null ||
                !DialogueCatalog.TryGet(_outgoingSharedRequest.EventId, out var lines) ||
                lines.Length == 0)
            {
                _waitingForSharedStart = false;
                _requestedSharedEventId = null;
                _outgoingSharedRequest = null;
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                EnqueueSharedRequest(_outgoingSharedRequest);
                TryPublishNextSharedDialogue();
            }
            else
            {
                _waitingForSharedStart = SendSharedRequest(_outgoingSharedRequest);
            }
        }

        static bool TryReadRequest(object[] data, out SharedRequest request)
        {
            request = null;
            if (data.Length < 8 ||
                data[0] is not string eventId ||
                data[1] is not bool lockInput ||
                data[2] is not bool emitNoise)
            {
                return false;
            }

            request = new SharedRequest
            {
                EventId = eventId,
                LockInput = lockInput,
                EmitNoise = emitNoise,
                NoiseRange = Convert.ToSingle(data[3]),
                NoisePosition = new Vector2(
                    Convert.ToSingle(data[4]),
                    Convert.ToSingle(data[5])),
                Map = Convert.ToInt32(data[6]),
                RaiseFlag = data[7] as string,
            };
            return true;
        }

        void AdoptSharedRoomState()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return;

            var properties = PhotonNetwork.CurrentRoom.CustomProperties;
            var serial = ReadInt(properties, SharedSerialKey, -1);
            if (serial < 0)
                return;

            var done = ReadInt(properties, SharedDoneKey, -1);
            if (serial > _sharedSerial && done < serial)
            {
                var eventId = ReadString(properties, SharedEventKey);
                if (!string.IsNullOrEmpty(eventId))
                {
                    QueueSharedDialogue(
                        serial,
                        eventId,
                        ReadBool(properties, SharedLockKey, true),
                        ReadBool(properties, SharedNoiseKey, false),
                        ReadFloat(properties, SharedNoiseRangeKey),
                        new Vector2(
                            ReadFloat(properties, SharedNoiseXKey),
                            ReadFloat(properties, SharedNoiseYKey)),
                        ReadInt(properties, SharedMapKey, MapZone.Unzoned),
                        ReadString(properties, SharedRaiseKey));
                }
            }

            if (serial != _sharedSerial)
                return;

            if (done >= serial)
            {
                _sharedStartPending = false;
                FinishNetworkPlayback(ReadString(properties, SharedEventKey));
            }

            var advance = ReadInt(properties, SharedAdvanceKey, 0);
            if (advance <= _sharedAdvanceCounter)
                return;

            _sharedPendingAdvancePulses += advance - _sharedAdvanceCounter;
            _sharedAdvanceCounter = advance;
        }

        void QueueSharedDialogue(
            int serial,
            string eventId,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete)
        {
            if (serial < _sharedSerial ||
                (serial == _sharedSerial && (_sharedStartPending || _sharedNetworkPlaying)))
            {
                return;
            }

            _sharedSerial = serial;
            _sharedAdvanceCounter = 0;
            _sharedPendingAdvancePulses = 0;
            _pendingSharedEventId = eventId;
            _pendingSharedLockInput = lockInput;
            _pendingSharedEmitNoise = emitNoise;
            _pendingSharedNoiseRange = noiseRange;
            _pendingSharedNoisePosition = noisePosition;
            _pendingSharedMap = map;
            _pendingSharedRaiseFlag = raiseFlagOnComplete;
            _sharedStartPending = true;
        }

        void TryStartPendingSharedDialogue()
        {
            if (!_sharedStartPending || _playing)
                return;

            // WorldState and the room dialogue packet are separate Photon properties. Retrying
            // here lets the slower one arrive without losing a one-shot conversation.
            if (!StoryProgression.CanPlay(_pendingSharedEventId))
                return;

            var emitHere = _pendingSharedEmitNoise &&
                           (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient);

            if (!TryPlayLocal(
                    _pendingSharedEventId,
                    _pendingSharedLockInput,
                    emitHere,
                    _pendingSharedNoiseRange,
                    _pendingSharedNoisePosition,
                    _pendingSharedMap,
                    _pendingSharedRaiseFlag,
                    NetworkAdvancePressed,
                    autoAdvanceSeconds: 0f))
            {
                return;
            }

            _sharedStartPending = false;
            _sharedNetworkPlaying = true;
            if (_pendingSharedEventId == _requestedSharedEventId)
            {
                _waitingForSharedStart = false;
                _requestedSharedEventId = null;
                _outgoingSharedRequest = null;
            }
        }

        void PublishSharedAdvance()
        {
            if (PhotonNetwork.CurrentRoom == null)
                return;

            var expected = _sharedAdvanceCounter;
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { SharedAdvanceKey, expected + 1 } },
                new Hashtable { { SharedAdvanceKey, expected } });
        }

        bool NetworkAdvancePressed()
        {
            if (_sharedPendingAdvancePulses <= 0)
                return false;

            _sharedPendingAdvancePulses--;
            return true;
        }

        IEnumerator Play(
            DialogueLine[] lines,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete,
            string eventId,
            Func<bool> advanceSource,
            float autoAdvanceSeconds)
        {
            _playing = true;
            _lockInput = lockInput;
            _advanceSource = advanceSource;
            _playingEventId = eventId;
            _lines = lines;
            _lineIndex = 0;

            // Counted as a beat only when it locks input. A line that plays while the players can
            // still walk is not a moment they are unable to defend themselves in, and making the
            // monster harmless through it would cover most of the game.
            if (_lockInput)
            {
                StoryBeat.Begin();
                SuspendPlayers(true);
            }

            while (_lineIndex < _lines.Length)
            {
                _line = _lines[_lineIndex];
                _advanceAfterReveal = false;

                if (emitNoise && noiseRange > 0f)
                    NoiseBus.Emit(noisePosition, noiseRange, NoiseKind.Self, map);

                yield return RevealLine(_line.Text);
                if (!_advanceAfterReveal)
                    yield return autoAdvanceSeconds > 0f
                        ? WaitForAutoAdvance(autoAdvanceSeconds)
                        : WaitForAdvance();

                _lineIndex++;
            }

            if (!string.IsNullOrEmpty(raiseFlagOnComplete))
                WorldState.Raise(raiseFlagOnComplete);

            StoryProgression.Complete(eventId);
            var completedSharedDialogue = _sharedNetworkPlaying;
            Finished?.Invoke(eventId);
            Finish();

            if (completedSharedDialogue)
                CompleteSharedDialogue();
        }

        IEnumerator RevealLine(string text)
        {
            _visibleCharacters = 0;
            _revealing = true;

            var total = string.IsNullOrEmpty(text) ? 0 : text.Length;
            var elapsed = 0f;

            while (_visibleCharacters < total)
            {
                if (AdvancePressed())
                {
                    _visibleCharacters = total;
                    _advanceAfterReveal = _advanceSource != null;
                    break;
                }

                elapsed += Time.unscaledDeltaTime * lettersPerSecond;
                _visibleCharacters = Mathf.Clamp(Mathf.FloorToInt(elapsed), 0, total);
                yield return null;
            }

            _visibleCharacters = total;
            _revealing = false;
        }

        /// <summary>
        /// Holds a finished line for a moment and then moves on by itself.
        ///
        /// For beats that carry the scene rather than wait on it. The company escape runs the
        /// dialogue alongside the automatic run for the door, and the sequence cannot travel until
        /// the last line is done — so a conversation nobody thought to press space through left the
        /// two of them standing in the lobby with no way out. A line that plays while the players
        /// are being carried somewhere is not a line they were asked to acknowledge.
        ///
        /// A press still skips ahead, so this only ever makes the beat faster.
        /// </summary>
        IEnumerator WaitForAutoAdvance(float seconds)
        {
            var remaining = seconds;

            while (remaining > 0f)
            {
                if (AdvancePressed())
                    yield break;

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator WaitForAdvance()
        {
            if (_advanceSource != null)
            {
                while (!AdvancePressed())
                    yield return null;

                yield break;
            }

            while (AdvancePressed())
                yield return null;

            while (!AdvancePressed())
                yield return null;
        }

        void Finish()
        {
            _routine = null;
            _playing = false;
            _revealing = false;
            _lines = null;
            _visibleCharacters = 0;
            _advanceAfterReveal = false;
            _advanceSource = null;
            _playingEventId = null;
            _sharedNetworkPlaying = false;

            if (_lockInput)
            {
                SuspendPlayers(false);
                StoryBeat.End();
            }

            _lockInput = false;
        }

        void CompleteSharedDialogue()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient ||
                PhotonNetwork.CurrentRoom == null)
            {
                return;
            }

            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { SharedDoneKey, _sharedSerial } });

            if (!string.IsNullOrEmpty(_activeSharedEventId))
                _queuedSharedEvents.Remove(_activeSharedEventId);

            _activeSharedEventId = null;
        }

        void SuspendPlayers(bool suspended)
        {
            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                rig.SuspendInput(suspended);
        }

        bool AdvancePressed()
        {
            if (_advanceSource != null)
                return _advanceSource();

            return LocalAdvancePressed();
        }

        bool LocalAdvancePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard[advanceKey].wasPressedThisFrame ||
                 keyboard[alternateAdvanceKey].wasPressedThisFrame))
                return true;

            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        static int ReadInt(Hashtable properties, string key, int fallback)
        {
            if (properties == null || !properties.TryGetValue(key, out var value))
                return fallback;

            return value switch
            {
                int number => number,
                byte number => number,
                short number => number,
                _ => fallback,
            };
        }

        static float ReadFloat(Hashtable properties, string key)
        {
            if (properties == null || !properties.TryGetValue(key, out var value))
                return 0f;

            return value switch
            {
                float number => number,
                double number => (float)number,
                int number => number,
                _ => 0f,
            };
        }

        static bool ReadBool(Hashtable properties, string key, bool fallback)
        {
            return properties != null &&
                   properties.TryGetValue(key, out var value) &&
                   value is bool flag
                ? flag
                : fallback;
        }

        static string ReadString(Hashtable properties, string key)
        {
            return properties != null &&
                   properties.TryGetValue(key, out var value)
                ? value as string
                : null;
        }

        void OnGUI()
        {
            if (!_playing || _lines == null)
                return;

            GUI.depth = guiDepth;
            EnsureStyles();

            // Measured in the game's own 640x360 pixels. See Imgui.Scaled.
            using var screen = Imgui.Scaled();

            if (_line.Speaker == "센틸 안내방송")
            {
                DrawBroadcast(screen.Area);
                return;
            }

            DrawDialogueBox(screen.Area);
        }

        void DrawDialogueBox(Rect viewport)
        {
            var width = Mathf.Min(panelSize.x, viewport.width - 32f);
            var height = Mathf.Min(panelSize.y, viewport.height * 0.42f);
            var panel = new Rect(
                viewport.x + (viewport.width - width) * 0.5f,
                viewport.yMax - bottomMargin - height,
                width,
                height);

            Imgui.Fill(panel, panelColour);
            Imgui.Fill(new Rect(panel.x, panel.y, panel.width, 2f), lineColour);

            const float pad = 14f;
            var speakerRect = new Rect(panel.x + pad, panel.y + 8f, panel.width - pad * 2f, 18f);
            GUI.Label(speakerRect, _line.Speaker, _speakerStyle);

            var visible = _line.Text;
            if (_revealing && _visibleCharacters < visible.Length)
                visible = visible.Substring(0, _visibleCharacters);

            var textRect = new Rect(panel.x + pad, panel.y + 32f, panel.width - pad * 2f,
                                    panel.height - 42f);
            GUI.Label(textRect, visible, _textStyle);
        }

        void DrawBroadcast(Rect viewport)
        {
            Imgui.Fill(viewport, new Color(0.006f, 0.008f, 0.007f, 1f));

            var safe = new Rect(
                viewport.x + viewport.width * 0.08f,
                viewport.y + viewport.height * 0.12f,
                viewport.width * 0.84f,
                viewport.height * 0.76f);

            DrawCrtLines(viewport);
            DrawVignette(viewport);

            Imgui.Fill(new Rect(safe.x, safe.y, safe.width, 2f),
                       new Color(0.34f, 0.52f, 0.38f, 0.7f));
            Imgui.Fill(new Rect(safe.x, safe.yMax - 2f, safe.width, 2f),
                       new Color(0.34f, 0.52f, 0.38f, 0.45f));

            GUI.Label(new Rect(safe.x, safe.y + 14f, safe.width, 28f),
                      "SENTIL FIELD BRIEFING", _speakerStyle);

            var visible = _line.Text;
            if (_revealing && _visibleCharacters < visible.Length)
                visible = visible.Substring(0, _visibleCharacters);

            var body = new Rect(safe.x, safe.y + safe.height * 0.34f, safe.width,
                                safe.height * 0.34f);
            GUI.Label(body, visible, _textStyle);

            var footer = new Rect(safe.x, safe.yMax - 32f, safe.width, 18f);
            GUI.Label(footer, "SPACE / ENTER  //  SIGNAL STABLE", _speakerStyle);
        }

        void DrawCrtLines(Rect viewport)
        {
            var line = new Color(0.18f, 0.31f, 0.21f, 0.08f);
            for (var y = viewport.y; y < viewport.yMax; y += 6f)
                Imgui.Fill(new Rect(viewport.x, y, viewport.width, 1f), line);
        }

        void DrawVignette(Rect viewport)
        {
            var edge = new Color(0f, 0f, 0f, 0.55f);
            Imgui.Fill(new Rect(viewport.x, viewport.y, viewport.width, viewport.height * 0.08f), edge);
            Imgui.Fill(new Rect(viewport.x, viewport.yMax - viewport.height * 0.08f,
                                viewport.width, viewport.height * 0.08f), edge);
            Imgui.Fill(new Rect(viewport.x, viewport.y, viewport.width * 0.055f, viewport.height), edge);
            Imgui.Fill(new Rect(viewport.xMax - viewport.width * 0.055f, viewport.y,
                                viewport.width * 0.055f, viewport.height), edge);
        }

        void EnsureStyles()
        {
            if (_speakerStyle != null)
                return;

            _speakerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = speakerFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
            };
            _speakerStyle.normal.textColor = speakerColour;

            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = textFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            _textStyle.normal.textColor = textColour;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Current = null;
            Finished = null;
        }
    }
}
