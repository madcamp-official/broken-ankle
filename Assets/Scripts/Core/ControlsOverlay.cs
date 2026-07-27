using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Core
{
    /// <summary>
    /// Prints the current controls in the corner of the screen.
    ///
    /// The keys are read out of the input asset rather than typed in here, because the bindings
    /// have already moved several times — arrow keys left the first player, a flashlight toggle
    /// arrived, a second local player appeared — and a hint card that lies is worse than none.
    /// </summary>
    public class ControlsOverlay : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Drag Assets/InputSystem_Actions here.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Action maps to list, in order. One block each.")]
        [SerializeField] string[] maps = { "Player", "Player2" };

        [Tooltip("Heading shown above each map's block.")]
        [SerializeField] string[] mapTitles = { "1P", "2P (더미 동료)" };

        [Tooltip("Actions to list, in order. Anything else in the map is skipped.")]
        [SerializeField] string[] actions = { "Move", "Sprint", "Crouch", "Interact", "ToggleFlashlight" };

        [Tooltip("Labels for those actions, in the same order.")]
        [SerializeField] string[] labels = { "이동", "달리기", "웅크리기", "상호작용", "손전등" };

        [Header("Look")]
        [SerializeField] Vector2 margin = new(12f, 12f);
        [SerializeField] int fontSize = 13;
        [SerializeField] Color textColour = new(0.92f, 0.92f, 0.96f);
        [SerializeField] Color panelColour = new(0f, 0f, 0f, 0.55f);

        [Tooltip("Hides and shows the card without disabling the object.")]
        [SerializeField] Key toggleKey = Key.F1;

        [SerializeField] bool visible = true;

        // Rows are kept as two columns because Korean glyphs are twice the width of Latin ones,
        // so padding a single string with spaces never lines up.
        readonly List<string> _labelColumn = new();
        readonly List<string> _keyColumn = new();

        GUIStyle _style;
        Texture2D _panel;

        /// <summary>
        /// Unity's readable names are written out in full, which is fine in a rebinding menu and
        /// far too wide on a corner card. Four arrow keys alone run past a third of the screen.
        /// </summary>
        static readonly Dictionary<string, string> ShortNames = new()
        {
            { "Up Arrow", "↑" },
            { "Down Arrow", "↓" },
            { "Left Arrow", "←" },
            { "Right Arrow", "→" },
            { "Left Shift", "Shift" },
            { "Right Shift", "R Shift" },
            { "Left Control", "Ctrl" },
            { "Right Control", "R Ctrl" },
            { "Numpad 0", "Num 0" },
            { "Numpad .", "Num ." },
        };

        void Start() => Rebuild();

        void OnDestroy()
        {
            if (_panel != null)
                Destroy(_panel);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;
        }

        /// <summary>Re-reads the bindings. Call after rebinding at runtime.</summary>
        public void Rebuild()
        {
            _labelColumn.Clear();
            _keyColumn.Clear();

            if (inputActions == null)
            {
                _labelColumn.Add("(입력 에셋이 연결되지 않음)");
                _keyColumn.Add(string.Empty);
                return;
            }

            for (var m = 0; m < maps.Length; m++)
            {
                var map = inputActions.FindActionMap(maps[m], throwIfNotFound: false);
                if (map == null)
                    continue;

                if (_labelColumn.Count > 0)
                {
                    _labelColumn.Add(string.Empty);
                    _keyColumn.Add(string.Empty);
                }

                _labelColumn.Add(m < mapTitles.Length ? mapTitles[m] : maps[m]);
                _keyColumn.Add(string.Empty);

                for (var a = 0; a < actions.Length; a++)
                {
                    var action = map.FindAction(actions[a]);
                    if (action == null)
                        continue;

                    var keys = KeyboardBindingsOf(action);
                    if (keys.Length == 0)
                        continue;

                    _labelColumn.Add("  " + (a < labels.Length ? labels[a] : actions[a]));
                    _keyColumn.Add(keys);
                }
            }
        }

        /// <summary>
        /// Only the keyboard bindings. A gamepad's buttons mean nothing to somebody reading a
        /// card while sitting at a keyboard, and listing both doubles the height for no gain.
        /// </summary>
        static string KeyboardBindingsOf(InputAction action)
        {
            var parts = new List<string>();

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || string.IsNullOrEmpty(binding.path))
                    continue;

                if (!binding.path.StartsWith("<Keyboard>"))
                    continue;

                var readable = InputControlPath.ToHumanReadableString(
                    binding.path, InputControlPath.HumanReadableStringOptions.OmitDevice);

                if (ShortNames.TryGetValue(readable, out var shortened))
                    readable = shortened;

                if (!parts.Contains(readable))
                    parts.Add(readable);
            }

            // A stick composite reads as four separate keys, which is exactly what the player
            // presses, so they are joined rather than summarised.
            return string.Join(" ", parts);
        }

        void OnGUI()
        {
            if (!visible || _labelColumn.Count == 0)
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                    padding = new RectOffset(0, 0, 0, 0),
                };
                _style.normal.textColor = textColour;

                _panel = new Texture2D(1, 1);
                _panel.SetPixel(0, 0, panelColour);
                _panel.Apply();
            }

            var lineHeight = _style.lineHeight + 2f;
            var labelWidth = 0f;
            var keyWidth = 0f;
            foreach (var s in _labelColumn)
                labelWidth = Mathf.Max(labelWidth, _style.CalcSize(new GUIContent(s)).x);
            foreach (var s in _keyColumn)
                keyWidth = Mathf.Max(keyWidth, _style.CalcSize(new GUIContent(s)).x);

            const float gutter = 14f;
            const float pad = 9f;

            // Anchor to the camera's viewport, not the window. Pixel Perfect letterboxes the game
            // into the middle of a larger window, and screen-corner coordinates land in the black
            // bars outside it, where the card is easy to miss entirely.
            var origin = Vector2.zero;
            var camera = Camera.main;
            if (camera != null)
            {
                var view = camera.pixelRect;
                origin = new Vector2(view.x, Screen.height - view.yMax);
            }

            var rect = new Rect(origin.x + margin.x, origin.y + margin.y,
                labelWidth + gutter + keyWidth + pad * 2f,
                _labelColumn.Count * lineHeight + pad * 2f);

            GUI.DrawTexture(rect, _panel);

            for (var i = 0; i < _labelColumn.Count; i++)
            {
                var y = rect.y + pad + i * lineHeight;
                GUI.Label(new Rect(rect.x + pad, y, labelWidth, lineHeight), _labelColumn[i], _style);
                GUI.Label(new Rect(rect.x + pad + labelWidth + gutter, y, keyWidth, lineHeight), _keyColumn[i], _style);
            }
        }
    }
}
