using Handover.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Handover.EditorTools
{
    /// <summary>
    /// Builds a desktop greybox scene: a seated camera, a light, the Handover app with keyboard
    /// palms on a virtual tabletop. Playable in the Editor without a headset or the XR
    /// Simulator, using the same simulation and the same hand contract as the device.
    /// </summary>
    public static class HandoverGreyboxSceneBuilder
    {
        public const string ScenePath = HandoverEditorUtility.ScenesFolder + "/HandoverGreybox.unity";

        [MenuItem("Handover/Create Greybox Scene (keyboard hands)", priority = 10)]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.18f);
            // Seated eye height; the virtual tabletop appears 0.42 m ahead and below.
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 0f), Quaternion.Euler(40f, 0f, 0f));

            var lightGo = new GameObject("Directional Light", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var root = new GameObject("Handover");
            HandoverApp app = root.AddComponent<HandoverApp>();
            SyntheticHandDriver driver = root.AddComponent<SyntheticHandDriver>();
            HandoverEditorUtility.SetReference(app, "_handSource", driver);
            HandoverEditorUtility.SetReference(app, "_viewer", cameraGo.transform);
            HandoverEditorUtility.SetBool(app, "_autoConfirmStage", true);
            HandoverEditorUtility.SetBool(app, "_preferVirtualTabletop", true);
            HandoverEditorUtility.SetBool(app, "_editorWindowFocusPauses", false);
            HandoverEditorUtility.SetReference(driver, "_app", app);

            HandoverEditorUtility.EnsureFolder(HandoverEditorUtility.ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            HandoverEditorUtility.AddSceneToBuild(ScenePath, first: false);
            Debug.Log("[Handover] Greybox scene saved to " + ScenePath + ". Press Play; the HUD lists the keyboard controls (O toggles it).");
        }
    }
}
