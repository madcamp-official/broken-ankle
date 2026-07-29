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

        // What this put into the live settings, so a save can take exactly that back out again and
        // leave a value somebody typed in by hand alone.
        static string _injectedRealtime;
        static string _injectedVoice;

        // Looked up once. In the editor Inject runs whenever the settings come up empty, and a
        // project with no asset yet would otherwise print the same warning every frame.
        static PhotonAppIds _cached;
        static bool _lookedUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Inject()
        {
            var ids = Ids();
            if (ids == null)
                return;

            var settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null)
            {
                Debug.LogError("PhotonServerSettings is missing. Reimport PUN.");
                return;
            }

            var app = settings.AppSettings;

            // Blank fields are left alone rather than cleared, so half a file still works and a
            // value set by hand for a one-off test is not silently wiped.
            if (!string.IsNullOrWhiteSpace(ids.realtime))
                app.AppIdRealtime = _injectedRealtime = ids.realtime.Trim();

            if (!string.IsNullOrWhiteSpace(ids.voice))
                app.AppIdVoice = _injectedVoice = ids.voice.Trim();
        }

        static PhotonAppIds Ids()
        {
            if (_lookedUp)
                return _cached;

            _lookedUp = true;
            _cached = Resources.Load<PhotonAppIds>(ResourceName);

            if (_cached == null)
            {
                Debug.LogWarning(
                    $"No {ResourceName} asset found, so Photon has no App ID and will not connect. " +
                    $"Create one with Assets > Create > Ashburn > Photon App Ids, put it in a " +
                    $"Resources folder as '{ResourceName}', and paste the IDs from the team. " +
                    "It is ignored by git on purpose.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(_cached.realtime))
                Debug.LogWarning($"{ResourceName} has no Realtime App ID. PUN will not connect.");

            if (string.IsNullOrWhiteSpace(_cached.voice))
                Debug.LogWarning($"{ResourceName} has no Voice App ID. The radio will be silent.");

            return _cached;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Fills the settings in for the whole editor session as well, not only while playing.
        ///
        /// PUN's own editor code checks whether the App ID is set and, finding it empty, says the
        /// setup wizard has never been run — every single time Play is pressed
        /// (<c>PhotonEditor.PlayModeStateChanged</c>). The project keeps that field empty on purpose,
        /// so the warning is permanent and tells nobody anything. Filling the live object in makes
        /// those checks see a configured project.
        ///
        /// Nothing reaches disk. <see cref="SaveGuard"/> takes the values back out for the moment of
        /// a save, which is the only moment they could be written to the committed asset.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void KeepFilledInEditor()
        {
            UnityEditor.EditorApplication.update += () =>
            {
                var settings = PhotonNetwork.PhotonServerSettings;
                if (settings == null)
                    return;

                // Self-healing rather than a one-off: the save guard below empties these, a domain
                // reload can land between the two, and whatever order those happen in, the next
                // tick puts them back. Once they are filled this costs two null checks.
                if (string.IsNullOrEmpty(settings.AppSettings.AppIdRealtime) ||
                    string.IsNullOrEmpty(settings.AppSettings.AppIdVoice))
                    Inject();
            };
        }

        /// <summary>
        /// Blanks the App IDs while the settings asset is being written, and puts them back after.
        ///
        /// This is the whole guarantee. Injecting writes into the live <c>PhotonServerSettings</c>
        /// object, which is the same object that gets serialised, so anything that decides that
        /// asset is dirty would commit somebody's App ID to a public repository. Rather than hoping
        /// nothing ever marks it dirty, the one moment that matters is caught directly.
        /// </summary>
        class SaveGuard : UnityEditor.AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                var settings = PhotonNetwork.PhotonServerSettings;
                if (settings == null || paths == null)
                    return paths;

                var assetPath = UnityEditor.AssetDatabase.GetAssetPath(settings);
                if (string.IsNullOrEmpty(assetPath) || System.Array.IndexOf(paths, assetPath) < 0)
                    return paths;

                var app = settings.AppSettings;
                var blanked = false;

                // Only what this put there. A value typed in by hand is somebody's decision.
                if (!string.IsNullOrEmpty(_injectedRealtime) && app.AppIdRealtime == _injectedRealtime)
                {
                    app.AppIdRealtime = string.Empty;
                    blanked = true;
                }

                if (!string.IsNullOrEmpty(_injectedVoice) && app.AppIdVoice == _injectedVoice)
                {
                    app.AppIdVoice = string.Empty;
                    blanked = true;
                }

                // Not put back here. The editor tick above notices they are empty and refills them
                // once the write is over, which is the only ordering that is actually guaranteed.
                return paths;
            }
        }
#endif
    }
}
