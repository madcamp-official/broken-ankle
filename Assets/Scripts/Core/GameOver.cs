using Ashburn.Monster;
using Ashburn.World;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Ashburn.Core
{
    /// <summary>
    /// Ends the run when nobody is left standing, and starts it again.
    ///
    /// One rule covers both of the ways it can happen. Two players both on the floor have nobody
    /// left to help either of them; a player on their own has nobody to begin with, so going down
    /// once is the end. Rather than counting players and special-casing the lonely one, this asks
    /// whether anybody is still on their feet — which is the same question in both games.
    ///
    /// Decided on each machine rather than announced by the host. Both clients already agree about
    /// who is down, because that state crosses the wire with the character it belongs to, so both
    /// reach the same verdict at the same moment without a message being sent.
    ///
    /// Installs itself. It has to outlive the reload it performs, and a component sitting in the
    /// systems scene would be destroyed halfway through starting the game over.
    /// </summary>
    public class GameOver : MonoBehaviour
    {
        /// <summary>Scene the game starts from. Everything else is loaded by it.</summary>
        const string SystemsScene = "Systems";

        /// <summary>
        /// Grace period after the level appears, in seconds.
        ///
        /// A character spawns and registers a frame or two before its partner does, and for that
        /// window the only player in the level might be one who is down. Without the wait a rescue
        /// that arrives late by one frame reads as the run being over.
        /// </summary>
        const float SettleSeconds = 1f;

        static GameOver _instance;

        bool _over;
        float _liveSince;
        GUIStyle _title;
        GUIStyle _line;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_instance != null)
                return;

            var host = new GameObject(nameof(GameOver)) { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<GameOver>();
        }

        void Update()
        {
            if (_over)
            {
                if (Restarted())
                    Restart();

                return;
            }

            // Nobody in the level is a game that has not started — the characters are made after
            // the map is up, and for those frames there is nobody to be standing.
            if (Downed.All.Count == 0)
            {
                _liveSince = 0f;
                return;
            }

            if (_liveSince <= 0f)
                _liveSince = Time.time;

            if (Time.time - _liveSince < SettleSeconds)
                return;

            if (!Downed.AnyStanding)
                _over = true;
        }

        static bool Restarted()
        {
            var keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame);
        }

        /// <summary>
        /// Puts the game back to its first moment.
        ///
        /// Loading the systems scene on its own is most of it: it is a single load, so every map
        /// held open beside it goes as well, and the zones going with them hand back the slots they
        /// claimed. What survives a scene load is the static state, and that has to be said out
        /// loud — a second attempt that began with the first one's doors already unlocked would be
        /// a strange kind of restart.
        /// </summary>
        void Restart()
        {
            _over = false;
            _liveSince = 0f;

            // The room is left rather than reused. NetworkGame connects in its Start, and the copy
            // arriving with the reloaded scene would find itself already in a room, holding a slot
            // that was decided for a game that is over.
            if (PhotonNetwork.IsConnected)
                PhotonNetwork.Disconnect();

            WorldState.Clear();
            MapLoader.ForgetClaims();

            SceneManager.LoadScene(SystemsScene, LoadSceneMode.Single);
        }

        void OnGUI()
        {
            if (!_over)
                return;

            EnsureStyles();

            // Measured in the game's own 640x360 pixels. See Imgui.Scaled.
            using var screen = Imgui.Scaled();

            var view = screen.Area;
            Imgui.Fill(view, new Color(0f, 0f, 0f, 0.82f));

            var box = new Rect(view.x, view.y + view.height * 0.42f, view.width, 60f);
            GUI.Label(box, "모두 쓰러졌다", _title);

            GUI.Label(new Rect(box.x, box.yMax, box.width, 24f),
                      "R 또는 Enter — 처음부터", _line);
        }

        void EnsureStyles()
        {
            if (_title != null)
                return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _title.normal.textColor = new Color(0.92f, 0.9f, 0.9f);

            _line = new GUIStyle(_title)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
            };
            _line.normal.textColor = new Color(0.75f, 0.73f, 0.75f);
        }
    }
}
