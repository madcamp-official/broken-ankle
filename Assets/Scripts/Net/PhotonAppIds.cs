using Photon.Pun;
using UnityEngine;

namespace Ashburn.Net
{
    /// <summary>
    /// Holds the Photon App IDs on one machine, and puts them into Photon's settings before
    /// anything connects.
    ///
    /// The App IDs cannot live in <c>PhotonServerSettings</c> where Photon puts them, because that
    /// asset has to be committed: it also carries <c>RpcList</c>, and PUN sends an RPC as an index
    /// into that list rather than by name. Two machines with lists in different orders do not fail
    /// loudly — they call the wrong method. Since this repository is public, the App IDs are kept
    /// out of it and everything else in that asset stays shared.
    ///
    /// An App ID is not a password and gives nobody access to the dashboard, but it is what a
    /// client connects with, so a stranger who finds one can spend the plan's concurrent users.
    ///
    /// The asset this loads is ignored by git. See the setup note in Multiplayer.MD.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Ashburn/Photon App Ids")]
    public class PhotonAppIds : ScriptableObject
    {
        /// <summary>
        /// Name the asset must have, in a Resources folder, for this to find it.
        /// </summary>
        public const string ResourceName = "PhotonAppIds";

        [Tooltip("App ID of the Realtime application on the Photon dashboard. Used by PUN.")]
        [SerializeField] string realtime;

        [Tooltip("App ID of the Voice application. A separate application from the Realtime one, " +
                 "not the same ID.")]
        [SerializeField] string voice;

        // Kept so the editor can put the asset back the way it found it. Injecting writes into the
        // live PhotonServerSettings object, and an editor that later decides that asset is dirty
        // would write the App IDs into the file that does get committed.
        static string _previousRealtime;
        static string _previousVoice;
        static bool _injected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Inject()
        {
            var ids = Resources.Load<PhotonAppIds>(ResourceName);
            if (ids == null)
            {
                Debug.LogWarning(
                    $"No {ResourceName} asset found, so Photon has no App ID and will not connect. " +
                    $"Create one with Assets > Create > Ashburn > Photon App Ids, put it in a " +
                    $"Resources folder as '{ResourceName}', and paste the IDs from the team. " +
                    "It is ignored by git on purpose.");
                return;
            }

            var settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null)
            {
                Debug.LogError("PhotonServerSettings is missing. Reimport PUN.");
                return;
            }

            var app = settings.AppSettings;

            _previousRealtime = app.AppIdRealtime;
            _previousVoice = app.AppIdVoice;
            _injected = true;

            // Blank fields are left alone rather than cleared, so half a file still works and a
            // value set by hand for a one-off test is not silently wiped.
            if (!string.IsNullOrWhiteSpace(ids.realtime))
                app.AppIdRealtime = ids.realtime.Trim();

            if (!string.IsNullOrWhiteSpace(ids.voice))
                app.AppIdVoice = ids.voice.Trim();

            if (string.IsNullOrWhiteSpace(app.AppIdRealtime))
                Debug.LogWarning($"{ResourceName} has no Realtime App ID. PUN will not connect.");

            if (string.IsNullOrWhiteSpace(app.AppIdVoice))
                Debug.LogWarning($"{ResourceName} has no Voice App ID. The radio will be silent.");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Puts the App IDs back when play mode ends.
        ///
        /// Entering play mode does not copy a ScriptableObject, so the fields written above are the
        /// ones in the committed asset. Whether that reaches disk depends on domain reload settings
        /// and on whether something else marks the asset dirty, which is too thin a guarantee for a
        /// public repository. Undoing it costs nothing and does not depend on either.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void RestoreOnExitingPlayMode()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state != UnityEditor.PlayModeStateChange.EnteredEditMode || !_injected)
                    return;

                _injected = false;

                var settings = PhotonNetwork.PhotonServerSettings;
                if (settings == null)
                    return;

                settings.AppSettings.AppIdRealtime = _previousRealtime;
                settings.AppSettings.AppIdVoice = _previousVoice;
            };
        }
#endif
    }
}
