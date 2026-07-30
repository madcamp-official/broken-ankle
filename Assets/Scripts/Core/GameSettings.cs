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
        const string ControlsCardKey = "settings.showControlsCard";

        static float _masterVolume = 1f;
        static bool _showControlsCard = true;
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

                Screen.fullScreen = value;
                PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Whether the controls card sits in the corner. See <see cref="ControlsOverlay"/>.</summary>
        public static bool ShowControlsCard
        {
            get { Load(); return _showControlsCard; }
            set
            {
                Load();
                if (_showControlsCard == value)
                    return;

                _showControlsCard = value;
                PlayerPrefs.SetInt(ControlsCardKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
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
            _showControlsCard = PlayerPrefs.GetInt(ControlsCardKey, 1) != 0;

            AudioListener.volume = _masterVolume;

            // Only when it was stored. Forcing the window mode on every start would undo a player
            // who dragged the window out of fullscreen and left it there.
            if (PlayerPrefs.HasKey(FullscreenKey))
                Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) != 0;
        }

        /// <summary>Puts everything back to how it ships. The menu's one destructive button.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(VolumeKey);
            PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.DeleteKey(ControlsCardKey);
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
