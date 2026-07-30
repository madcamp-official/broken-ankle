using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Core
{
    /// <summary>
    /// Prints the current controls in the corner of the screen.
    ///
    /// The keys come out of the input asset through <see cref="BindingText"/> rather than being
    /// typed in here, because the bindings have already moved several times — arrow keys left the
    /// first player, a flashlight toggle arrived, a second local player appeared — and a hint card
    /// that lies is worse than none.
    ///
    /// Whether it is showing belongs to <see cref="GameSettings"/> rather than to this component,
    /// so the pause menu's switch and this card's own key are the same switch and it is remembered
    /// between sessions.
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
        [SerializeField] string[] actions = { "Move", "Sprint", "Crouch", "Interact", "ToggleFlashlight", "ToggleHearing" };

        [Tooltip("Labels for those actions, in the same order.")]
        [SerializeField] string[] labels = { "이동", "달리기", "웅크리기", "상호작용", "손전등", "소음 링" };

        [Header("Look")]
        [SerializeField] Vector2 margin = new(12f, 12f);
        [SerializeField] int fontSize = 13;
        [SerializeField] Color textColour = new(0.92f, 0.92f, 0.96f);
        [SerializeField] Color panelColour = new(0f, 0f, 0f, 0.55f);

        [Tooltip("Hides and shows the card without disabling the object. The same switch the pause " +
                 "menu offers, so pressing this is remembered too.")]
        [SerializeField] Key toggleKey = Key.F1;

        // Rows are kept as two columns because Korean glyphs are twice the width of Latin ones,
        // so padding a single string with spaces never lines up.
        readonly List<string> _labelColumn = new();
        readonly List<string> _keyColumn = new();

        GUIStyle _style;

        /// <summary>The rows as drawn, for anything else that wants to show the same list.</summary>
        public IReadOnlyList<string> Labels => _labelColumn;

        /// <summary>The keys for those rows, one to one with <see cref="Labels"/>.</summary>
        public IReadOnlyList<string> Keys => _keyColumn;

        void Start() => Rebuild();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // Written to the setting rather than to a field of our own, so this key and the pause
            // menu's switch cannot disagree about whether the card is up.
            if (keyboard[toggleKey].wasPressedThisFrame)
                GameSettings.ShowControlsCard = !GameSettings.ShowControlsCard;
        }

        /// <summary>Re-reads the bindings. Call after rebinding at runtime.</summary>
        public void Rebuild() => BindingText.BuildRows(
            inputActions, maps, mapTitles, actions, labels, _labelColumn, _keyColumn);

        void OnGUI()
        {
            // Not while the menu is up. Its 조작 tab is this same list, and the card sits on top of
            // the panel rather than beside it.
            if (!GameSettings.ShowControlsCard || PauseMenu.AnyOpen || _labelColumn.Count == 0)
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

            Imgui.Fill(rect, panelColour);

            for (var i = 0; i < _labelColumn.Count; i++)
            {
                var y = rect.y + pad + i * lineHeight;
                GUI.Label(new Rect(rect.x + pad, y, labelWidth, lineHeight), _labelColumn[i], _style);
                GUI.Label(new Rect(rect.x + pad + labelWidth + gutter, y, keyWidth, lineHeight), _keyColumn[i], _style);
            }
        }
    }
}
