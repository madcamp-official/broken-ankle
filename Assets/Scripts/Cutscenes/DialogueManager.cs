using System.Collections;
using System;
using Ashburn.Core;
using Ashburn.Noise;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Stopgap dialogue UI and flow controller.
    ///
    /// It intentionally draws with IMGUI like the existing menu/prompt screens, so story beats can
    /// be tested before a final Canvas, TMP font, portraits, and animation polish exist.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
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
        public static bool IsPlaying => Current != null && Current._playing;

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

        public static DialogueManager Ensure()
        {
            if (Current != null)
                return Current;

            var go = new GameObject("DialogueManager");
            return go.AddComponent<DialogueManager>();
        }

        public bool TryPlay(
            string eventId,
            bool lockInput,
            bool emitNoise,
            float noiseRange,
            Vector2 noisePosition,
            int map,
            string raiseFlagOnComplete,
            Func<bool> advanceSource = null)
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
                                           map, raiseFlagOnComplete, eventId, advanceSource));
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
            Func<bool> advanceSource)
        {
            _playing = true;
            _lockInput = lockInput;
            _advanceSource = advanceSource;
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
                    yield return WaitForAdvance();

                _lineIndex++;
            }

            if (!string.IsNullOrEmpty(raiseFlagOnComplete))
                WorldState.Raise(raiseFlagOnComplete);

            StoryProgression.Complete(eventId);
            Finished?.Invoke(eventId);
            Finish();
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

            if (_lockInput)
            {
                SuspendPlayers(false);
                StoryBeat.End();
            }

            _lockInput = false;
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

            var keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard[advanceKey].wasPressedThisFrame ||
                 keyboard[alternateAdvanceKey].wasPressedThisFrame))
                return true;

            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
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
