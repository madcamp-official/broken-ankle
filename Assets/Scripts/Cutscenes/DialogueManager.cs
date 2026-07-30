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
            string raiseFlagOnComplete)
        {
            if (_playing)
                return false;

            if (!DialogueCatalog.TryGet(eventId, out var lines) || lines.Length == 0)
            {
                Debug.LogWarning($"No dialogue lines found for event id '{eventId}'.", this);
                return false;
            }

            _routine = StartCoroutine(Play(lines, lockInput, emitNoise, noiseRange, noisePosition,
                                           map, raiseFlagOnComplete, eventId));
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
            string eventId)
        {
            _playing = true;
            _lockInput = lockInput;
            _lines = lines;
            _lineIndex = 0;

            if (_lockInput)
                SuspendPlayers(true);

            while (_lineIndex < _lines.Length)
            {
                _line = _lines[_lineIndex];

                if (emitNoise && noiseRange > 0f)
                    NoiseBus.Emit(noisePosition, noiseRange, NoiseKind.Self, map);

                yield return RevealLine(_line.Text);
                yield return WaitForAdvance();
                _lineIndex++;
            }

            if (!string.IsNullOrEmpty(raiseFlagOnComplete))
                WorldState.Raise(raiseFlagOnComplete);

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

            if (_lockInput)
                SuspendPlayers(false);

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

            EnsureStyles();

            var viewport = Viewport();
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

        Rect Viewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            var view = camera.pixelRect;
            return new Rect(view.x, Screen.height - view.yMax, view.width, view.height);
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
