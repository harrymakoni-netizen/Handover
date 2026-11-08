using System.Text;
using Handover.MetaPlatform;
using Handover.Unity;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Input;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Handover.EditorTools.MetaSetup
{
    /// <summary>
    /// Builds the persistent Bootstrap scene for Quest: the Interaction SDK comprehensive rig
    /// (via Meta's Quick Actions, the documented path), passthrough, MRUK, and the Handover app
    /// wired to the rig's tracked hands, with the hand menu made ray- and poke-interactable.
    /// Afterwards run Window > Meta > Tools > Project Setup Tool > Fix All for Android and Windows.
    /// Every call used here was checked against the 207.0.1 package sources; verify in Play mode.
    /// A failing step is reported and skipped so the scene is always saved; rerunning rebuilds it.
    /// </summary>
    public static class HandoverMetaSceneBuilder
    {
        public const string ScenePath = HandoverEditorUtility.ScenesFolder + "/Bootstrap.unity";

        [MenuItem("Handover/Create Meta XR Bootstrap Scene", priority = 0)]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var report = new StringBuilder("[Handover] Bootstrap scene\n");
            int failures = 0;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Interaction SDK brings its own camera rig.
            GameObject defaultCamera = GameObject.Find("Main Camera");
            if (defaultCamera != null)
            {
                Object.DestroyImmediate(defaultCamera);
            }

            failures += Step(report, "add the interaction rig", () =>
            {
                OVRQuickActionsAPI.AddOVRInteractionRig();
                report.AppendLine("- Added the OVR comprehensive interaction rig (Quick Actions).");
            });
            failures += Step(report, "enable passthrough", () => ConfigurePassthrough(report));
            failures += Step(report, "set the project features", () => ConfigureProjectFeatures(report));
            failures += Step(report, "add MRUK", () =>
            {
                var mrukGo = new GameObject("MRUK");
                mrukGo.AddComponent<MRUK>();
                report.AppendLine("- Added MRUK (scene data from the device; the app falls back to a virtual tabletop).");
            });

            var root = new GameObject("Handover");
            HandoverApp app = root.AddComponent<HandoverApp>();
            MetaHandPoseSource hands = root.AddComponent<MetaHandPoseSource>();
            MetaLifecycleBridge lifecycle = root.AddComponent<MetaLifecycleBridge>();
            MruTableSurfaceSource surfaces = root.AddComponent<MruTableSurfaceSource>();
            HandoverEditorUtility.SetReference(app, "_handSource", hands);
            HandoverEditorUtility.SetReference(app, "_surfaceSource", surfaces);
            HandoverEditorUtility.SetReference(lifecycle, "_app", app);
            failures += Step(report, "wire the hands", () => WireHands(hands, report));
            failures += Step(report, "find the centre eye", () =>
            {
                OVRCameraRig rig = Object.FindAnyObjectByType<OVRCameraRig>();
                Transform centreEye = rig != null ? rig.transform.Find("TrackingSpace/CenterEyeAnchor") : null;
                if (centreEye != null)
                {
                    HandoverEditorUtility.SetReference(app, "_viewer", centreEye);
                }
                else
                {
                    report.AppendLine("- WARNING: CenterEyeAnchor not found; HandoverApp will use Camera.main.");
                }
            });

            var menuGo = new GameObject("Handover Menu");
            HandoverMenu menu = menuGo.AddComponent<HandoverMenu>();
            HandoverEditorUtility.SetReference(menu, "_app", app);
            failures += Step(report, "build the hand menu", () =>
            {
                menu.BuildLayout();
                menu.Canvas.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0.65f), Quaternion.identity);
                report.AppendLine("- Built the hand menu.");
            });
            failures += Step(report, "make the menu ray- and poke-interactable", () =>
            {
                QuickActionsAPI.AddRayCanvasInteraction(menu.Canvas.gameObject);
                QuickActionsAPI.AddPokeCanvasInteraction(menu.Canvas.gameObject);
                report.AppendLine("- Added ray and poke canvas interaction to the menu (Quick Actions).");
            });

            HandoverEditorUtility.EnsureFolder(HandoverEditorUtility.ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            HandoverEditorUtility.AddSceneToBuild(ScenePath, first: true);
            report.AppendLine("- Saved " + ScenePath + " as the first build scene.");
            report.AppendLine(failures == 0
                ? "Next: Window > Meta > Tools > Project Setup Tool > Fix All (Android and Windows tabs), then Play with the XR Simulator. Zero Console errors is the bar."
                : $"{failures} step(s) FAILED (see the errors above). The scene was saved anyway; fix the cause and run this menu again.");
            if (failures == 0)
            {
                Debug.Log(report.ToString());
            }
            else
            {
                Debug.LogWarning(report.ToString());
            }
        }

        /// <summary>Runs one setup step; a failure is logged and reported instead of aborting the build.</summary>
        private static int Step(StringBuilder report, string name, System.Action action)
        {
            try
            {
                action();
                return 0;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                report.AppendLine($"- FAILED to {name}: {e.GetType().Name}: {e.Message}");
                return 1;
            }
        }

        private static void ConfigurePassthrough(StringBuilder report)
        {
            OVRManager manager = Object.FindAnyObjectByType<OVRManager>();
            if (manager == null)
            {
                report.AppendLine("- WARNING: no OVRManager found; enable passthrough manually.");
                return;
            }

            var so = new SerializedObject(manager);
            SerializedProperty passthrough = so.FindProperty("isInsightPassthroughEnabled");
            if (passthrough != null)
            {
                passthrough.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (manager.GetComponent<OVRPassthroughLayer>() == null)
            {
                manager.gameObject.AddComponent<OVRPassthroughLayer>();
            }

            OVRCameraRig rig = manager.GetComponent<OVRCameraRig>();
            Transform eye = rig != null ? rig.transform.Find("TrackingSpace/CenterEyeAnchor") : null;
            Camera camera = eye != null ? eye.GetComponent<Camera>() : null;
            if (camera != null)
            {
                // Passthrough shows through a transparent clear colour.
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.nearClipPlane = 0.01f;
            }

            report.AppendLine("- Enabled passthrough (OVRManager + OVRPassthroughLayer, transparent clear colour).");
        }

        private static void ConfigureProjectFeatures(StringBuilder report)
        {
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
            {
                report.AppendLine("- WARNING: OVRProjectConfig unavailable; set hand tracking, scene and passthrough support manually.");
                return;
            }

            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(config);
            report.AppendLine("- Project config: hand tracking (controllers and hands), scene support, passthrough support.");
        }

        /// <summary>
        /// Wires the rig's two tracked hands. Never reads <see cref="Hand.Handedness"/>: in Edit mode
        /// that evaluates hand data and throws on the rig's synthetic HandSphereMap hands. The side
        /// comes from the serialized <c>_handedness</c> of the hand's FromOVRHandDataSource instead.
        /// </summary>
        private static void WireHands(MetaHandPoseSource source, StringBuilder report)
        {
            Hand left = null;
            Hand right = null;
            int found = 0;
            foreach (Hand hand in MetaRigHands.FindTrackedRigHands())
            {
                found++;
                var root = MetaRigHands.RootSource(hand) as Object;
                SerializedProperty side = root != null ? new SerializedObject(root).FindProperty("_handedness") : null;
                if (side == null)
                {
                    continue;
                }

                if (side.intValue == (int)Handedness.Left && left == null)
                {
                    left = hand;
                }
                else if (side.intValue == (int)Handedness.Right && right == null)
                {
                    right = hand;
                }
            }

            HandoverEditorUtility.SetReference(source, "_leftHand", left);
            HandoverEditorUtility.SetReference(source, "_rightHand", right);
            if (left != null && right != null)
            {
                report.AppendLine($"- Wired the tracked hands: {HierarchyPath(left.transform)} (left), {HierarchyPath(right.transform)} (right).");
                if (found > 2)
                {
                    report.AppendLine($"- WARNING: {found} tracked hands found; check the two wired on MetaHandPoseSource.");
                }
            }
            else
            {
                report.AppendLine("- WARNING: could not find both tracked hands; MetaHandPoseSource will search on the first frame (or assign them in the Inspector).");
            }
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                path = p.name + "/" + path;
            }

            return path;
        }
    }
}
