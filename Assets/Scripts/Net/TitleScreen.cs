using Ashburn.Core;
using UnityEngine;

namespace Ashburn.Net
{
    /// <summary>
    /// The first screen: which room, and who with.
    ///
    /// The room used to be a fixed string in the scene, which meant every copy of the game
    /// everywhere queued for the same two seats. The first pair in were playing; everybody else got
    /// a refusal in a console their build does not have, and a map that loaded with nobody in it.
    /// Three pairs wanting to play at once needed three different builds.
    ///
    /// A code fixes that without a lobby server. Whoever presses 만들기 gets four letters and reads
    /// them out; their partner types them in. Two people who have agreed to play can find each
    /// other and nobody else can wander in, which is the whole requirement.
    ///
    /// Still two to a room. The spawn points, the slot numbers and the roles are all built for a
    /// pair — see PlayerRoles.MD — so six players is three rooms, not one crowded one.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The connection this drives. Left empty it is looked for.")]
        [SerializeField] NetworkGame game;

        [Header("Room codes")]
        [Tooltip("Letters a generated code is drawn from. No O, I, 0 or 1: they are read aloud and " +
                 "written down, and those four are the pairs people get wrong.")]
        [SerializeField] string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [SerializeField, Range(3, 8)] int codeLength = 4;

        [Header("Look")]
        [Tooltip("IMGUI draw order. Lower is drawn on top, and this has to out-rank every other " +
                 "screen in the project: the fade sits at -1000 and the dialogue box at -800, so " +
                 "at the default 0 the briefing drew straight over the room code.")]
        [SerializeField] int guiDepth = -2000;

        [SerializeField] int titleSize = 26;
        [SerializeField] int fontSize = 13;
        [SerializeField] Color textColour = new(0.93f, 0.93f, 0.97f);
        [SerializeField] Color dimColour = new(0.03f, 0.03f, 0.05f, 0.96f);
        [SerializeField] Color panelColour = new(0.09f, 0.09f, 0.12f, 0.98f);

        const string TypedField = "ashburn.title.code";

        string _typed = string.Empty;
        bool _dismissed;
        bool _focusRequested;
        bool _suspended;

        GUIStyle _title;
        GUIStyle _label;
        GUIStyle _button;
        GUIStyle _code;
        GUIStyle _field;

        /// <summary>Whether the title screen is still in the way. Anything that should wait can ask.</summary>
        public static bool IsUp { get; private set; }

        void Awake()
        {
            if (game == null)
                game = FindAnyObjectByType<NetworkGame>();

            // Nothing to choose without a network game — the split-keyboard test runs with it
            // switched off, and a title screen demanding a room code would make that unreachable.
            if (game == null)
                _dismissed = true;
        }

        void OnEnable() => IsUp = !_dismissed;

        void OnDisable() => IsUp = false;

        void Update()
        {
            if (_dismissed)
                return;

            // Gone the moment both are in a room. The map is already loading underneath, and the
            // players have nothing left to decide.
            if (game != null && game.State == NetworkGame.Stage.Joined &&
                Photon.Pun.PhotonNetwork.CurrentRoom is { PlayerCount: >= 2 })
            {
                _dismissed = true;
                IsUp = false;
                SuspendPlayers(false);
                return;
            }

            // The local character is created the moment the room is joined, which is before the
            // partner arrives. Without this it walks about behind the title screen — blind, since
            // the screen is over the top of it — for as long as the wait lasts.
            SuspendPlayers(true);
        }

        /// <summary>Takes the players' hands off the keys while the screen is up, and gives them back.</summary>
        void SuspendPlayers(bool suspended)
        {
            if (_suspended == suspended)
                return;

            _suspended = suspended;

            foreach (var rig in FindObjectsByType<Player.PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                rig.SuspendInput(suspended);
        }

        // A static outlives a play session when the editor skips its domain reload, and one left
        // true would have everything else waiting for a screen that is not there.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => IsUp = false;

        void OnGUI()
        {
            if (_dismissed || game == null)
                return;

            GUI.depth = guiDepth;
            EnsureStyles();

            var viewport = Viewport();
            Imgui.Fill(viewport, dimColour);

            var width = Mathf.Min(360f, viewport.width - 24f);
            var height = Mathf.Min(200f, viewport.height - 16f);
            var panel = new Rect(
                viewport.x + (viewport.width - width) * 0.5f,
                viewport.y + (viewport.height - height) * 0.5f,
                width, height);

            Imgui.Fill(panel, panelColour);

            const float pad = 14f;
            var inner = new Rect(panel.x + pad, panel.y + pad,
                                 panel.width - pad * 2f, panel.height - pad * 2f);
            var line = _label.lineHeight + 3f;

            GUI.Label(new Rect(inner.x, inner.y, inner.width, line * 1.6f), "ASHBURN", _title);
            var y = inner.y + line * 2.2f;

            switch (game.State)
            {
                case NetworkGame.Stage.Joined:
                    DrawWaiting(inner, y, line);
                    break;

                case NetworkGame.Stage.Working:
                    GUI.Label(new Rect(inner.x, y, inner.width, line), "연결하는 중…", _label);
                    break;

                default:
                    DrawChoices(inner, y, line);
                    break;
            }
        }

        /// <summary>The room is held and the partner has not arrived. The code is the whole screen.</summary>
        void DrawWaiting(Rect inner, float y, float line)
        {
            GUI.Label(new Rect(inner.x, y, inner.width, line),
                      game.IsHost ? "이 번호를 동료에게 알려주세요" : "동료를 기다리는 중…", _label);

            GUI.Label(new Rect(inner.x, y + line * 1.3f, inner.width, line * 2f),
                      game.RoomName.ToUpperInvariant(), _code);
        }

        void DrawChoices(Rect inner, float y, float line)
        {
            var half = (inner.width - 8f) * 0.5f;

            if (GUI.Button(new Rect(inner.x, y, half, line * 1.8f), "방 만들기", _button))
                game.Enter(NewCode());

            GUI.SetNextControlName(TypedField);
            _typed = GUI.TextField(new Rect(inner.x + half + 8f, y, half, line * 1.8f),
                                   _typed, codeLength, _field);

            // Typed straight over the top of a code somebody read out, so the case it arrives in is
            // not the player's problem.
            _typed = _typed.ToUpperInvariant();

            if (!_focusRequested)
            {
                _focusRequested = true;
                GUI.FocusControl(TypedField);
            }

            y += line * 2.2f;

            var ready = _typed.Trim().Length > 0;
            var enter = Event.current.type == EventType.KeyDown &&
                        (Event.current.keyCode == KeyCode.Return ||
                         Event.current.keyCode == KeyCode.KeypadEnter);

            using (new GUIEnabled(ready))
            {
                if (GUI.Button(new Rect(inner.x, y, inner.width, line * 1.8f), "들어가기", _button) ||
                    (ready && enter))
                    game.Enter(_typed);
            }

            y += line * 2.2f;

            GUI.Label(new Rect(inner.x, y, inner.width, line * 2f),
                      string.IsNullOrEmpty(game.Problem)
                          ? "방을 만들어 번호를 알려주거나, 받은 번호를 적으세요."
                          : game.Problem,
                      _label);
        }

        string NewCode()
        {
            var letters = new char[codeLength];
            for (var i = 0; i < letters.Length; i++)
                letters[i] = alphabet[Random.Range(0, alphabet.Length)];

            return new string(letters);
        }

        Rect Viewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            // pixelRect counts up from the bottom of the window, GUI coordinates down from the top.
            var view = camera.pixelRect;
            return new Rect(view.x, Screen.height - view.yMax, view.width, view.height);
        }

        void EnsureStyles()
        {
            if (_title != null)
                return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            _label.normal.textColor = textColour;

            _title = new GUIStyle(_label)
            {
                fontSize = titleSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };

            _code = new GUIStyle(_title) { fontSize = titleSize - 2 };
            _code.normal.textColor = new Color(0.85f, 0.76f, 0.42f);

            _button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = titleSize - 8,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        /// <summary>Turns GUI.enabled off for a block and puts it back, whatever happens inside.</summary>
        readonly struct GUIEnabled : System.IDisposable
        {
            readonly bool _was;

            public GUIEnabled(bool enabled)
            {
                _was = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose() => GUI.enabled = _was;
        }
    }
}
