using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// The handful of things a player is allowed to change, and the only place they are remembered.
    ///
    /// Static and with no object in any scene, because a settings holder that lives in a scene is a
    /// settings holder somebody forgets to put in one, and every reader then has to cope with it
    /// being absent. Applying happens in the setter rather than in an Apply button: there is nothing
    /// here expensive enough to batch, and a slider that does not do anything until you press a
    /// button cannot be judged by ear.
    ///
    /// Written through <see cref="PlayerPrefs"/>. Not because it is a good store — it is a registry
    /// key on Windows — but because it needs no file format decided now and holds three values.
    /// </summary>
    public static class GameSettings
    {
        const string VolumeKey = "settings.masterVolume";
        const string FullscreenKey = "settings.fullscreen";

        static float _masterVolume = 1f;
        static bool _loaded;

        /// <summary>Everything the game plays, 0 to 1.</summary>
        public static float MasterVolume
        {
            get { Load(); return _masterVolume; }
            set
            {
                Load();
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_masterVolume, value))
                    return;

                _masterVolume = value;
                AudioListener.volume = value;
                PlayerPrefs.SetFloat(VolumeKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Whether the game has the screen to itself.
        ///
        /// Read from Unity rather than kept here, so a player who used the window manager or Alt+Enter
        /// does not find the menu insisting otherwise.
        /// </summary>
        public static bool Fullscreen
        {
            get => Screen.fullScreen;
            set
            {
                if (Screen.fullScreen == value)
                    return;

                if (value)
                    Screen.fullScreen = true;
                else
                    GoWindowed();

                PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The size the game is drawn at before the camera scales it up.</summary>
        const int ReferenceWidth = 640;
        const int ReferenceHeight = 360;

        /// <summary>
        /// Leaves fullscreen at a window the picture fits exactly.
        ///
        /// The camera scales by whole numbers only — its crop frame is Windowbox — so a window that
        /// is not a multiple of 640x360 is filled out with black bars and the picture shrinks to the
        /// next multiple down. At 1024x576 that is a single scale in the middle of a window six
        /// tenths black. Handing Unity a window that is already a multiple means there is nothing
        /// left over to put bars in.
        ///
        /// The largest multiple that fits the monitor, with room left for the desktop's own
        /// furniture: a window exactly as tall as the screen has its title bar under the taskbar.
        /// </summary>
        static void GoWindowed()
        {
            var display = Screen.currentResolution;

            var scale = Mathf.Max(1, Mathf.Min(display.width / ReferenceWidth,
                                               display.height / ReferenceHeight));

            // A window exactly the size of the screen has its title bar off the top of it and its
            // foot under the taskbar, so the largest multiple that fits is one step too large
            // whenever the screen is itself a multiple. Stepping down there rather than reserving a
            // fixed margin matters: a 1366x768 laptop has room for two scales with nothing to
            // spare, and a margin wide enough for a title bar would cost it one of them.
            if (scale > 1 &&
                ReferenceWidth * scale >= display.width &&
                ReferenceHeight * scale >= display.height)
                scale--;

            Screen.SetResolution(ReferenceWidth * scale, ReferenceHeight * scale,
                                 FullScreenMode.Windowed);
        }

        /// <summary>
        /// Reads the stored values in and applies them, once.
        ///
        /// Called from every getter rather than from a startup hook alone, so a component whose Awake
        /// runs before that hook cannot read a default over the player's choice.
        /// </summary>
        public static void Load()
        {
            if (_loaded)
                return;

            // Set before reading anything, because applying the volume below goes through the
            // property, which calls back in here.
            _loaded = true;

            _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));

            AudioListener.volume = _masterVolume;

            // Only when it was stored. Forcing the window mode on every start would undo a player
            // who dragged the window out of fullscreen and left it there.
            //
            // Through the same path the menu uses, or a game remembered as windowed comes back at
            // whatever size the last run left behind — which is the size the bars appear in.
            if (!PlayerPrefs.HasKey(FullscreenKey))
                return;

            if (PlayerPrefs.GetInt(FullscreenKey) != 0)
                Screen.fullScreen = true;
            else
                GoWindowed();
        }

        /// <summary>Puts everything back to how it ships. The menu's one destructive button.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(VolumeKey);
            PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.Save();

            _loaded = false;
            Load();
        }

        // The editor keeps statics between play sessions when it skips its domain reload, which on
        // its own is harmless here — the values would simply already be right. It is _loaded that
        // matters: cleared, the next getter re-reads the store, so a session started after the
        // player edited their settings elsewhere does not run on last session's copy.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReloadOnPlay() => _loaded = false;
    }
}
