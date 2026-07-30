using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Makes the Windows build the team hands round.
    ///
    /// A script rather than the Build Profiles window because the settings that matter are the ones
    /// nobody remembers to check: the scene list has to start at Systems, every map has to be in it
    /// because maps are loaded additively by name, and a build made with the wrong scripting backend
    /// or the wrong architecture is one nobody notices until somebody cannot start it. Here they are
    /// written down and the same every time.
    ///
    /// Runs from the menu, or from the command line for a machine with no editor open:
    ///
    ///   Unity.exe -quit -batchmode -nographics -projectPath . -logFile - \
    ///             -executeMethod Ashburn.EditorTools.BuildWindows.Run
    ///
    /// The output folder is ignored by git. It is the zip that gets published — see Deploy.MD.
    /// </summary>
    public static class BuildWindows
    {
        /// <summary>Where the player is written, relative to the project folder.</summary>
        const string OutputFolder = "Build/Windows";

        /// <summary>Name of the executable, and so of the folder the player expects beside it.</summary>
        const string ProductFile = "Ashburn.exe";

        [MenuItem("Ashburn/Build Windows Player")]
        public static void FromMenu()
        {
            var report = Build();
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    "빌드 완료",
                    $"{summary.outputPath}\n\n" +
                    $"{Size(summary.totalSize)}, {summary.totalTime.TotalMinutes:0.0}분",
                    "확인");

                EditorUtility.RevealInFinder(summary.outputPath);
                return;
            }

            EditorUtility.DisplayDialog(
                "빌드 실패",
                $"{summary.result}. 오류 {summary.totalErrors}건. 콘솔을 보세요.",
                "확인");
        }

        /// <summary>Batch-mode entry point. Sets the exit code, because nothing else reads the log.</summary>
        public static void Run()
        {
            BuildReport report;

            try
            {
                report = Build();
            }
            catch (Exception error)
            {
                Debug.LogError($"Build threw: {error}");
                EditorApplication.Exit(2);
                return;
            }

            var summary = report.summary;
            Debug.Log($"Build {summary.result}: {summary.outputPath} " +
                      $"({Size(summary.totalSize)}, {summary.totalTime.TotalMinutes:0.0} min, " +
                      $"{summary.totalErrors} errors)");

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildReport Build()
        {
            var scenes = Scenes();
            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "The scene list is empty. Every map is loaded additively by name and must be in " +
                    "File > Build Profiles > Scene List, with Systems first.");

            if (!scenes[0].EndsWith("Systems.unity", StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"The first scene is '{scenes[0]}', not Systems. The build will " +
                                 "open on whatever that is.");

            WarnIfAppIdsMissing();

            // Mono rather than IL2CPP: the IL2CPP module is not installed on the machine this is
            // built from, and asking for it there fails late and unhelpfully. IL2CPP is the better
            // choice when it is available — add the module in Unity Hub and change this one line.
            var group = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.Mono2x);

            var output = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder, ProductFile);
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? OutputFolder);

            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,

                // Nothing else. A development build carries the profiler and the stack traces that
                // go with it, which is not what gets handed to a player.
                options = BuildOptions.None,
            });
        }

        static string[] Scenes() =>
            EditorBuildSettings.scenes.Where(scene => scene.enabled)
                                      .Select(scene => scene.path)
                                      .ToArray();

        /// <summary>
        /// Says so when the build would ship with no way to connect.
        ///
        /// The asset is ignored by git on purpose, so a fresh clone builds a game that reaches the
        /// title screen, takes a room code and then sits there — which looks like a bug in the
        /// networking rather than a missing file. See PhotonAppIds.
        /// </summary>
        static void WarnIfAppIdsMissing()
        {
            if (Resources.Load<Net.PhotonAppIds>(Net.PhotonAppIds.ResourceName) != null)
                return;

            Debug.LogWarning(
                $"No {Net.PhotonAppIds.ResourceName} asset in a Resources folder. This build will " +
                "not connect to anything. See the setup note in Multiplayer.MD.");
        }

        static string Size(ulong bytes) => $"{bytes / (1024f * 1024f):0} MB";
    }
}
