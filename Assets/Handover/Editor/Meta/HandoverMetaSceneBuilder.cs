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
    /// wired to the rig's hands, with the hand menu made ray- and poke-interactable.
    /// Afterwards run Meta > Tools > Project Setup Tool > Fix All for Android and Standalone.
    /// Every call used here was checked against the 207.0.1 package sources; verify in Play mode.
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
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Interaction SDK brings its own camera rig.
            GameObject defaultCamera = GameObject.Find("Main Camera");
            if (defaultCamera != null)
            {
                Object.DestroyImmediate(defaultCamera);
            }

            OVRQuickActionsAPI.AddOVRInteractionRig();
            report.AppendLine("- Added the OVR comprehensive interaction rig (Quick Actions).");

            ConfigurePassthrough(report);
            ConfigureProjectFeatures(report);

            var mrukGo = new GameObject("MRUK");
            mrukGo.AddComponent<MRUK>();
            report.AppendLine("- Added MRUK (scene data from the device; the app falls back to a virtual tabletop).");

            var root = new GameObject("Handover");
            HandoverApp app = root.AddComponent<HandoverApp>();
            MetaHandPoseSource hands = root.AddComponent<MetaHandPoseSource>();
            MetaLifecycleBridge lifecycle = root.AddComponent<MetaLifecycleBridge>();
            MruTableSurfaceSource surfaces = root.AddComponent<MruTableSurfaceSource>();
            HandoverEditorUtility.SetReference(app, "_handSource", hands);
            HandoverEditorUtility.SetReference(app, "_surfaceSource", surfaces);
            HandoverEditorUtility.SetReference(lifecycle, "_app", app);
            WireHands(hands, report);

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

            var menuGo = new GameObject("Handover Menu");
            HandoverMenu menu = menuGo.AddComponent<HandoverMenu>();
            HandoverEditorUtility.SetReference(menu, "_app", app);
            menu.BuildLayout();
            menu.Canvas.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0.65f), Quaternion.identity);
            QuickActionsAPI.AddRayCanvasInteraction(menu.Canvas.gameObject);
            QuickActionsAPI.AddPokeCanvasInteraction(menu.Canvas.gameObject);
            report.AppendLine("- Built the hand menu and added ray and poke canvas interaction (Quick Actions).");

            HandoverEditorUtility.EnsureFolder(HandoverEditorUtility.ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            HandoverEditorUtility.AddSceneToBuild(ScenePath, first: true);
            report.AppendLine("- Saved " + ScenePath + " as the first build scene.");
            report.AppendLine("Next: Meta > Tools > Project Setup Tool > Fix All (Android and Standalone), then Play with the XR Simulator. Zero Console errors is the bar.");
            Debug.Log(report.ToString());
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

        private static void WireHands(MetaHandPoseSource source, StringBuilder report)
        {
            Hand left = null;
            Hand right = null;
            foreach (Hand hand in Object.FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (hand.GetType() != typeof(Hand))
                {
                    continue;
                }

                if (hand.Handedness == Handedness.Left && left == null)
                {
                    left = hand;
                }
                else if (hand.Handedness == Handedness.Right && right == null)
                {
                    right = hand;
                }
            }

            HandoverEditorUtility.SetReference(source, "_leftHand", left);
            HandoverEditorUtility.SetReference(source, "_rightHand", right);
            report.AppendLine(left != null && right != null
                ? $"- Wired hands: {left.name} (left), {right.name} (right). Confirm these are the rig's tracked hands."
                : "- WARNING: could not find both ISDK Hand components; assign them on MetaHandPoseSource.");
        }
    }
}
