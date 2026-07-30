using System.Collections.Generic;
using Ashburn.Interaction;
using Ashburn.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Core
{
    /// <summary>
    /// The menu Escape opens: what the player can change, what the keys are, and what the two of
    /// them are carrying.
    ///
    /// It does not stop time, and that is the whole reason it is a menu and not a pause screen. The
    /// partner is a different person on a different machine, and there is nothing this build could
    /// freeze on their behalf; a local <c>Time.timeScale = 0</c> would only stop the monster on one
    /// screen while it kept walking on the other. So the world carries on and the menu takes this
    /// player's hands off the keys instead — see <see cref="PlayerRig.SuspendInput"/> — which is also
    /// the honest thing to tell them, and the footer says so.
    ///
    /// Drawn with IMGUI, like <see cref="ControlsOverlay"/> beside it. Not because IMGUI is the right
    /// answer for a shipped menu, but because a Canvas is a pile of scene objects to author and this
    /// is one file that works the moment it is dropped in. Replacing it with uGUI once there is menu
    /// art means deleting this and keeping <see cref="GameSettings"/>, which is where the decisions
    /// actually live.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        enum Tab
        {
            Settings,
            Controls,
        }

        [Header("Input")]
        [Tooltip("Opens and closes the menu — the settings and the key list. Read straight off the " +
                 "keyboard rather than through an action, because the action maps are what this " +
                 "switches off.")]
        [SerializeField] Key toggleKey = Key.Escape;

        [Tooltip("Opens and closes the pockets, which are their own panel and nothing else. What " +
                 "you are carrying is checked mid-game and often; the settings are not.")]
        [SerializeField] Key itemsKey = Key.Tab;

        [Header("Controls list")]
        [Tooltip("Drag Assets/InputSystem_Actions here.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Tag PlayerRig puts on the character the screen belongs to. Its keys are the ones " +
                 "listed; the map below is only used until that character exists.")]
        [SerializeField] string viewerTag = "Player";

        [Tooltip("Action map to fall back on before anybody has spawned.")]
        [SerializeField] string actionMap = "Player";

        [Tooltip("Actions to list, in order. Anything else in the map is skipped.")]
        [SerializeField] string[] actions =
            { "Move", "Sprint", "Crouch", "Interact", "ToggleFlashlight", "ToggleHearing", "PushToTalk" };

        [Tooltip("Labels for those actions, in the same order.")]
        [SerializeField] string[] labels =
            { "이동", "달리기", "웅크리기", "상호작용", "손전등", "소음 링", "무전" };

        [Header("Look")]
        [Tooltip("Panel width, and the least it is ever tall, in the game's own pixels. The picture " +
                 "is 640x360, so there is not much room: the panel grows past the height given here " +
                 "to fit whichever tab is open, and stops at the edge of the picture either way.")]
        [SerializeField] Vector2 size = new(420f, 190f);

        [Tooltip("Small on purpose. The controls tab is fourteen rows and the picture is 360 pixels " +
                 "tall, which at 14pt does not fit however the panel is arranged.")]
        [SerializeField] int fontSize = 11;
        [SerializeField] Color textColour = new(0.93f, 0.93f, 0.97f);
        [SerializeField] Color dimColour = new(0f, 0f, 0f, 0.6f);
        [SerializeField] Color panelColour = new(0.07f, 0.07f, 0.09f, 0.97f);

        readonly List<string> _controlLabels = new();
        readonly List<string> _controlKeys = new();
        readonly List<string> _itemLabels = new();
        readonly List<string> _itemKeys = new();

        Tab _tab = Tab.Settings;
        bool _open;
        bool _itemsOpen;
        bool _itemsStale = true;
        InputActionMap _map;

        GUIStyle _label;
        GUIStyle _heading;
        GUIStyle _button;

        /// <summary>Whether the menu is up. Anything that should hold off while it is can ask.</summary>
        public bool IsOpen => _open;

        /// <summary>
        /// Whether any menu is up.
        ///
        /// Static because the thing that needs to know is the controls card, which draws in the same
        /// corner of the same screen and has no reason to hold a reference to this. With the menu open
        /// the card is both redundant — the 조작 tab is the same list — and directly on top of it.
        /// </summary>
        public static bool AnyOpen { get; private set; }

        void OnEnable() => Inventory.Changed += OnInventoryChanged;

        void OnDisable()
        {
            Inventory.Changed -= OnInventoryChanged;

            // Leaving the object disabled with either panel open would strand the players with no
            // input and nothing on screen to explain why.
            if (_open || _itemsOpen)
                SetOpen(false, false);
        }

        void OnInventoryChanged() => _itemsStale = true;

        void Update()
        {
            // The character whose keys these are is created after this object exists — a networked
            // one once the room is joined — so the map is re-read until it settles.
            var map = BindingText.LocalMap(viewerTag, inputActions, actionMap);
            if (map != _map)
            {
                _map = map;
                BindingText.BuildRows(_map, actions, labels, _controlLabels, _controlKeys);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // Two panels, two keys, and each key only ever works its own. They were one screen with
            // the pockets as a third tab, which meant reaching for your pockets mid-game put the
            // volume slider on top of you and reading the settings was two presses from an
            // inventory you did not want.
            // Each key toggles its own panel and shuts the other. Pressing one while the other is
            // up is a player changing their mind, not asking for both.
            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                SetOpen(!_open, false);
                return;
            }

            if (keyboard[itemsKey].wasPressedThisFrame)
                SetOpen(false, !_itemsOpen);
        }

        /// <summary>
        /// Shows or hides the two panels.
        ///
        /// Never both at once. They are drawn in the same place and each dims the picture behind
        /// it, so a second one over the first is unreadable — and whichever key was pressed is a
        /// statement about which of the two the player wants.
        /// </summary>
        void SetOpen(bool menu, bool items)
        {
            _open = menu;
            _itemsOpen = items;

            var open = menu || items;
            AnyOpen = open;

            if (items)
                _itemsStale = true;

            // Every character this machine drives, which in the split-keyboard test is both of them:
            // one person is at the keyboard either way. SuspendInput leaves a character that was
            // never controlled here alone, so there is no need to sort them out first.
            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                rig.SuspendInput(open);
        }

        /// <summary>
        /// Rebuilds the carried-items list.
        ///
        /// Off an event rather than every frame, because reading a character's pockets is a scan of
        /// the whole world state and OnGUI would pay for it on both players every repaint.
        /// </summary>
        void RebuildItems()
        {
            _itemsStale = false;
            _itemLabels.Clear();
            _itemKeys.Clear();

            if (Inventory.All.Count == 0)
            {
                _itemLabels.Add("(아직 아무도 없음)");
                _itemKeys.Add(string.Empty);
                return;
            }

            foreach (var pockets in Inventory.All)
            {
                if (pockets == null)
                    continue;

                if (_itemLabels.Count > 0)
                {
                    _itemLabels.Add(string.Empty);
                    _itemKeys.Add(string.Empty);
                }

                _itemLabels.Add(pockets.OwnerName);
                _itemKeys.Add($"{pockets.Slot + 1}P");

                var items = pockets.Items();
                if (items.Count == 0)
                {
                    _itemLabels.Add("  (빈손)");
                    _itemKeys.Add(string.Empty);
                    continue;
                }

                foreach (var item in items)
                {
                    _itemLabels.Add("  " + item);
                    _itemKeys.Add(string.Empty);
                }
            }
        }

        void OnGUI()
        {
            if (_itemsOpen)
            {
                DrawItemsPanel();
                return;
            }

            if (!_open)
                return;

            EnsureStyles();

            // Measured in the game's own 640x360 pixels, anchored to the camera's viewport rather
            // than the window: Pixel Perfect letterboxes the game into the middle of a larger
            // window, and centring on the window would put the panel off-centre over the picture.
            // See Imgui.Scaled.
            using var screen = Imgui.Scaled();

            var viewport = screen.Area;
            Imgui.Fill(viewport, dimColour);

            const float pad = 11f;

            // The title, the tabs, the gap under them and the footer: 1.6 + 1.4 + 0.5 + 1.4, and it
            // has to be exactly that sum. Guessed at 4.5 it was a third of a line short, which cost
            // the controls tab its last row — the panel was sized for thirteen and drew fourteen.
            const float chrome = 4.9f;
            var line = _label.lineHeight + 2f;

            // Tall enough for whichever tab is open rather than one size for all of them, then held
            // inside the picture: at 640x360 there is no arrangement that fits everything, so the
            // last resort is DrawColumns stopping early rather than drawing over the footer.
            var width = Mathf.Min(size.x, viewport.width - 24f);
            var height = Mathf.Min(Mathf.Max(pad * 2f + line * (chrome + BodyRows()), size.y),
                                   viewport.height - 16f);

            var panel = new Rect(
                viewport.x + (viewport.width - width) * 0.5f,
                viewport.y + (viewport.height - height) * 0.5f,
                width, height);

            Imgui.Fill(panel, panelColour);

            var inner = new Rect(panel.x + pad, panel.y + pad,
                                 panel.width - pad * 2f, panel.height - pad * 2f);

            GUI.Label(new Rect(inner.x, inner.y, inner.width, line), "메뉴", _heading);

            var y = inner.y + line * 1.6f;
            y = DrawTabs(inner, y, line);
            y += line * 0.5f;

            var body = new Rect(inner.x, y, inner.width, inner.yMax - y - line * 1.4f);

            if (_tab == Tab.Settings)
                DrawSettings(body, line);
            else
                DrawColumns(body, line, _controlLabels, _controlKeys);

            // The keys are named here because neither is an action in the input asset, so the
            // controls card cannot know about them. The last part is said out loud because a menu
            // that looks like a pause screen and is not one gets somebody killed while they read it.
            GUI.Label(new Rect(inner.x, inner.yMax - line, inner.width, line),
                      $"{toggleKey} 닫기 · 게임은 멈추지 않는다", _label);
        }

        /// <summary>
        /// The pockets, on their own.
        ///
        /// Its own panel rather than a tab, and deliberately plain: this is read in the middle of
        /// a game, often with something walking about, so it has to be one key in, one key out, and
        /// nothing to click past.
        /// </summary>
        void DrawItemsPanel()
        {
            EnsureStyles();

            if (_itemsStale)
                RebuildItems();

            // Measured in the game's own 640x360 pixels. See Imgui.Scaled.
            using var screen = Imgui.Scaled();

            var viewport = screen.Area;
            Imgui.Fill(viewport, dimColour);

            const float pad = 11f;
            const float chrome = 3.0f;   // title, the gap under it, and the footer
            var line = _label.lineHeight + 2f;

            var width = Mathf.Min(size.x, viewport.width - 24f);
            var height = Mathf.Min(Mathf.Max(pad * 2f + line * (chrome + _itemLabels.Count), 120f),
                                   viewport.height - 16f);

            var panel = new Rect(
                viewport.x + (viewport.width - width) * 0.5f,
                viewport.y + (viewport.height - height) * 0.5f,
                width, height);

            Imgui.Fill(panel, panelColour);

            var inner = new Rect(panel.x + pad, panel.y + pad,
                                 panel.width - pad * 2f, panel.height - pad * 2f);

            GUI.Label(new Rect(inner.x, inner.y, inner.width, line), "소지품", _heading);

            var y = inner.y + line * 1.6f;
            DrawColumns(new Rect(inner.x, y, inner.width, inner.yMax - y - line * 1.4f),
                        line, _itemLabels, _itemKeys);

            GUI.Label(new Rect(inner.x, inner.yMax - line, inner.width, line),
                      $"{itemsKey} 닫기 · 게임은 멈추지 않는다", _label);
        }

        float DrawTabs(Rect inner, float y, float line)
        {
            var width = inner.width / 2f;
            var height = line * 1.4f;

            if (GUI.Button(new Rect(inner.x, y, width, height), Title(Tab.Settings, "설정"), _button))
                _tab = Tab.Settings;

            if (GUI.Button(new Rect(inner.x + width, y, width, height), Title(Tab.Controls, "도움말"), _button))
                _tab = Tab.Controls;

            return y + height;
        }

        // The open tab is marked rather than restyled: a second GUIStyle for one bracket is not worth
        // keeping in step with the first.
        string Title(Tab tab, string text) => _tab == tab ? $"[ {text} ]" : text;

        void DrawSettings(Rect body, float line)
        {
            var y = body.y;
            var labelWidth = body.width * 0.45f;
            var controlX = body.x + labelWidth;
            var controlWidth = body.width - labelWidth;

            GUI.Label(new Rect(body.x, y, labelWidth, line),
                      $"마스터 볼륨  {Mathf.RoundToInt(GameSettings.MasterVolume * 100f)}%", _label);

            // Vertically nudged: a slider's grab handle sits taller than a line of text and reads as
            // misaligned otherwise.
            GameSettings.MasterVolume = GUI.HorizontalSlider(
                new Rect(controlX, y + line * 0.25f, controlWidth, line), GameSettings.MasterVolume,
                0f, 1f);

            y += line * 1.6f;
            GUI.Label(new Rect(body.x, y, labelWidth, line), "전체 화면", _label);
            if (GUI.Button(new Rect(controlX, y, controlWidth * 0.5f, line * 1.3f),
                           GameSettings.Fullscreen ? "켬" : "끔", _button))
                GameSettings.Fullscreen = !GameSettings.Fullscreen;

            y += line * 1.9f;
            GUI.Label(new Rect(body.x, y, labelWidth, line), "조작키 카드", _label);
            if (GUI.Button(new Rect(controlX, y, controlWidth * 0.5f, line * 1.3f),
                           GameSettings.ShowControlsCard ? "켬" : "끔", _button))
                GameSettings.ShowControlsCard = !GameSettings.ShowControlsCard;

            y += line * 2.4f;
            if (GUI.Button(new Rect(body.x, y, body.width * 0.5f, line * 1.4f), "기본값으로", _button))
                GameSettings.Reset();
        }

        /// <summary>
        /// Draws a label column and a key column side by side.
        ///
        /// Two columns rather than one padded string, for the same reason the controls card uses two:
        /// Korean glyphs are twice the width of Latin ones and spaces never line up.
        /// </summary>
        void DrawColumns(Rect body, float line, List<string> labelColumn, List<string> keyColumn)
        {
            var keyWidth = 0f;
            foreach (var s in keyColumn)
                keyWidth = Mathf.Max(keyWidth, _label.CalcSize(new GUIContent(s)).x);

            const float gutter = 12f;
            var labelWidth = Mathf.Max(0f, body.width - keyWidth - gutter);

            for (var i = 0; i < labelColumn.Count; i++)
            {
                var y = body.y + i * line;

                // Silently stops rather than drawing over the footer. A list longer than the panel
                // wants scrolling, which this does not have yet.
                if (y + line > body.yMax)
                    break;

                GUI.Label(new Rect(body.x, y, labelWidth, line), labelColumn[i], _label);

                if (i < keyColumn.Count && !string.IsNullOrEmpty(keyColumn[i]))
                    GUI.Label(new Rect(body.x + labelWidth + gutter, y, keyWidth, line),
                              keyColumn[i], _label);
            }
        }

        /// <summary>How many rows of body the open tab needs, for the panel to be that tall.</summary>
        float BodyRows()
        {
            // The settings tab is a hand-placed layout rather than a list; the number is what
            // DrawSettings advances y by, and the two have to be changed together.
            return _tab == Tab.Controls ? _controlLabels.Count : 8f;
        }

        // A static outlives a play session when the editor skips its domain reload, and one left true
        // by a session stopped with the menu open would hide the controls card for the whole of the
        // next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => AnyOpen = false;

        void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
            };
            _label.normal.textColor = textColour;

            _heading = new GUIStyle(_label) { fontSize = fontSize + 4, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = textColour;

            _button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        }
    }
}
