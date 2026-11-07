// Signature stubs copied from 207.0.1 sources: interaction.ovr Editor/QuickActions/Scripts/OVRQuickActionsAPI.cs,
// interaction Editor/QuickActions/Scripts/QuickActionsAPI.cs, core Editor/OVRProjectConfig.cs.
using System.Collections.Generic;
using UnityEngine;

namespace Oculus.Interaction.OVR.Editor.QuickActions
{
    public static class OVRQuickActionsAPI
    {
        public static List<GameObject> AddOVRInteractionRig(bool generateAsEditableCopy = true) => null;
    }
}

namespace Oculus.Interaction.Editor.QuickActions
{
    public static class QuickActionsAPI
    {
        public static List<GameObject> AddRayCanvasInteraction(GameObject target, bool fixPointableCanvasModule = true) => null;
        public static List<GameObject> AddPokeCanvasInteraction(GameObject target, bool fixPointableCanvasModule = true) => null;
    }
}

public class OVRProjectConfig : ScriptableObject
{
    public enum HandTrackingSupport { ControllersOnly = 0, ControllersAndHands = 1, HandsOnly = 2 }
    public enum FeatureSupport { None = 0, Supported = 1, Required = 2 }
    public HandTrackingSupport handTrackingSupport = HandTrackingSupport.ControllersOnly;
    public FeatureSupport sceneSupport = FeatureSupport.None;
    public FeatureSupport insightPassthroughSupport { get; set; }
    public static OVRProjectConfig CachedProjectConfig => null;
    public static void CommitProjectConfig(OVRProjectConfig projectConfig) { }
}
