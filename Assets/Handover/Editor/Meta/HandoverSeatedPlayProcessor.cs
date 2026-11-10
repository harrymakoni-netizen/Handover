using Oculus.Interaction.Locomotion;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Handover.EditorTools.MetaSetup
{
    /// <summary>
    /// Handover is played seated, so nothing may move the TrackingSpace. The comprehensive rig ships
    /// ISDK locomotion whose <see cref="FirstPersonLocomotor"/> applies gravity every frame; with no
    /// floor collider it pulls the player down while MRUK's world lock pulls the space back.
    /// Unity calls this for every scene when a player is built and when the Editor enters Play mode
    /// (on the copy being built or played, not the saved scene), so the APK and Play mode are safe
    /// even if the saved scene still has the locomotor ticked.
    /// </summary>
    public sealed class HandoverSeatedPlayProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            int count = SwitchOffLocomotion(scene);
            if (count > 0)
            {
                Debug.Log($"[Handover] Seated play: switched off {count} FirstPersonLocomotor in '{scene.name}' for this {(report != null ? "build" : "Play session")}.");
            }
        }

        /// <summary>Disables every enabled locomotor in the scene; returns how many it changed.</summary>
        internal static int SwitchOffLocomotion(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (FirstPersonLocomotor locomotor in root.GetComponentsInChildren<FirstPersonLocomotor>(true))
                {
                    if (locomotor.enabled)
                    {
                        locomotor.enabled = false;
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
