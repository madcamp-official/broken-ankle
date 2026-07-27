using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Keeps the Photon App IDs out of PhotonServerSettings whenever it is written.
    ///
    /// The asset has to be committed — it carries RpcList, and PUN sends an RPC as an index into
    /// that list, so two machines with different copies call each other's wrong methods. The App
    /// IDs must not be, because this repository is public and an App ID is what a client connects
    /// with. <see cref="Ashburn.Net.PhotonAppIds"/> puts them back at runtime from an asset that
    /// git ignores.
    ///
    /// Asking people to remember to blank two fields before every commit did not work: they came
    /// back twice, both times because the editor still had them in memory and saved the project.
    /// Blanking them on the way to disk is the only version of this that cannot be forgotten.
    /// </summary>
    class PhotonAppIdGuard : AssetModificationProcessor
    {
        const string SettingsPath = "Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset";

        static readonly string[] Fields = { "AppIdRealtime", "AppIdFusion", "AppIdChat", "AppIdVoice" };

        static string[] OnWillSaveAssets(string[] paths)
        {
            // In play mode the IDs are supposed to be in there — that is the injection doing its
            // job — and a save at that moment would be writing a running value, not an authored one.
            if (EditorApplication.isPlaying)
                return paths;

            foreach (var path in paths)
                if (path == SettingsPath)
                    Blank(path);

            return paths;
        }

        static void Blank(string path)
        {
            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path);
            var changed = false;

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var field in Fields)
                {
                    var prefix = $"    {field}: ";
                    if (!lines[i].StartsWith(prefix) || lines[i].Length <= prefix.Length)
                        continue;

                    lines[i] = prefix.TrimEnd();
                    changed = true;
                }
            }

            if (!changed)
                return;

            File.WriteAllLines(path, lines);
            Debug.Log($"Kept the App IDs out of {Path.GetFileName(path)}. They are injected at " +
                      "runtime from Assets/Resources/PhotonAppIds, which git ignores.");
        }
    }
}
