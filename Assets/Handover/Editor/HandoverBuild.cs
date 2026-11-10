using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Handover.EditorTools
{
    /// <summary>
    /// One-click Android test build of the Quest scene (Gate A: "Android compiles").
    /// Builds only <c>Bootstrap.unity</c> as a development APK to <c>Builds/Handover-test.apk</c>
    /// and reports the outcome in one message. It changes no project settings.
    /// Command line (the Editor must not have the project open):
    /// <c>Unity -batchmode -quit -projectPath . -buildTarget Android
    /// -executeMethod Handover.EditorTools.HandoverBuild.BuildAndroidTestApkBatch -logFile build.log</c>
    /// A test build is debug-signed: fine for sideloading, not for store upload. A release build
    /// needs your own package name and keystore (Docs/Setup.md). A successful build shows that the
    /// project compiles and packages for Android; it is not a headset test.
    /// </summary>
    public static class HandoverBuild
    {
        public const string BootstrapScene = HandoverEditorUtility.ScenesFolder + "/Bootstrap.unity";
        public const string TestApkPath = "Builds/Handover-test.apk";
        private const int MaxReportedErrors = 6;

        [MenuItem("Handover/Build Android Test APK", priority = 30)]
        public static void BuildAndroidTestApkFromMenu()
        {
            // A build reads scenes from disk, so unsaved changes would be left out.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            bool ok = BuildAndroidTestApk(out string summary);
            EditorUtility.DisplayDialog(ok ? "Handover: build succeeded" : "Handover: build did not succeed", summary, "OK");
            if (ok)
            {
                EditorUtility.RevealInFinder(TestApkPath);
            }
        }

        /// <summary>Command-line entry point: exit code 0 on success, 1 otherwise.</summary>
        public static void BuildAndroidTestApkBatch()
        {
            bool ok = BuildAndroidTestApk(out _);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        /// <summary>Checks the preconditions, builds, and logs one summary (also returned).</summary>
        public static bool BuildAndroidTestApk(out string summary)
        {
            List<string> blockers = Preflight();
            if (blockers.Count > 0)
            {
                var notStarted = new StringBuilder("[Handover] Android test build not started:\n");
                foreach (string blocker in blockers)
                {
                    notStarted.AppendLine("- " + blocker);
                }

                summary = notStarted.ToString();
                Debug.LogError(summary);
                return false;
            }

            string packageId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            Directory.CreateDirectory(Path.GetDirectoryName(TestApkPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScene },
                locationPathName = TestApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            };

            var text = new StringBuilder();
            bool ok;
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary result = report.summary;
                ok = result.result == BuildResult.Succeeded;
                text.AppendLine(ok
                    ? "[Handover] Android test build SUCCEEDED"
                    : "[Handover] Android test build " + result.result.ToString().ToUpperInvariant());
                text.AppendLine($"- Time {result.totalTime.TotalMinutes:0.0} min, errors {result.totalErrors}, warnings {result.totalWarnings}.");
                if (ok)
                {
                    // BuildSummary.totalSize is not the size of the APK file, so read the file.
                    long apkBytes = File.Exists(TestApkPath) ? new FileInfo(TestApkPath).Length : 0L;
                    text.AppendLine($"- APK: {Path.GetFullPath(TestApkPath)} ({apkBytes / (1024f * 1024f):0.0} MB)");
                }
                else
                {
                    AppendErrors(text, report);
                }
            }
            catch (Exception e)
            {
                ok = false;
                Debug.LogException(e);
                text.AppendLine("[Handover] Android test build FAILED with an exception");
                text.AppendLine($"- {e.GetType().Name}: {e.Message}");
            }

            text.AppendLine($"- Unity {Application.unityVersion}, package {packageId}, version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}), development build, scene Bootstrap only.");
            if (ok)
            {
                if (packageId.StartsWith("com.DefaultCompany", StringComparison.OrdinalIgnoreCase))
                {
                    text.AppendLine("- Note: the package name is Unity's default. Fine for a test build; set your own before any store upload.");
                }

                text.AppendLine("This shows the project compiles and packages for Android. It is not a headset test:");
                text.AppendLine("install it on a Quest 3 and follow Docs/RemoteTestSheet.md.");
            }
            else
            {
                text.AppendLine("Send this message and the red Console lines above it.");
            }

            summary = text.ToString();
            if (ok)
            {
                Debug.Log(summary);
            }
            else
            {
                Debug.LogError(summary);
            }

            return ok;
        }

        private static List<string> Preflight()
        {
            var blockers = new List<string>();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                blockers.Add("Stop Play mode first.");
            }

            if (EditorApplication.isCompiling)
            {
                blockers.Add("Unity is still compiling scripts. Wait for it to finish and try again.");
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                blockers.Add("Android Build Support is not installed for this Unity version (Unity Hub > Installs > Add modules).");
            }
            else if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                blockers.Add($"The active platform is {EditorUserBuildSettings.activeBuildTarget}. Switch to Android in File > Build Profiles first.");
            }

            if (!File.Exists(BootstrapScene))
            {
                blockers.Add($"{BootstrapScene} does not exist. Run Handover > Create Meta XR Bootstrap Scene first.");
            }

            return blockers;
        }

        private static void AppendErrors(StringBuilder text, BuildReport report)
        {
            int shown = 0;
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception)
                    {
                        continue;
                    }

                    if (shown == MaxReportedErrors)
                    {
                        text.AppendLine("- (more errors in the Console)");
                        return;
                    }

                    string content = message.content ?? "";
                    int newline = content.IndexOf('\n');
                    if (newline > 0)
                    {
                        content = content.Substring(0, newline);
                    }

                    text.AppendLine($"- [{step.name}] {(content.Length > 300 ? content.Substring(0, 300) + "..." : content)}");
                    shown++;
                }
            }
        }
    }
}
