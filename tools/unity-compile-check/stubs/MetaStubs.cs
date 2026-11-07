// Signature stubs copied from Meta XR SDK 207.0.1 sources (only the members Handover uses):
// com.meta.xr.sdk.interaction Runtime/Scripts/Input/Hands/IHand.cs, Hand.cs, OpenXR/OpenXRHandPrimitives.cs,
// ThirdParty/InterfaceSupport/InterfaceAttribute.cs; com.meta.xr.sdk.core Scripts/OVRManager.cs, OVRDisplay.cs,
// OVRPassthroughLayer.cs, OVRCameraRig.cs; com.meta.xr.mrutilitykit Core/Scripts/MRUK.cs, MRUKRoom.cs, MRUKAnchor.cs.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Oculus.Interaction
{
    public class InterfaceAttribute : PropertyAttribute
    {
        public Type[] Types = null;
        public string TypeFromFieldName;
        public InterfaceAttribute(Type type, params Type[] types) { }
        public InterfaceAttribute(string typeFromFieldName) { }
    }
}

namespace Oculus.Interaction.Input
{
    public enum Handedness { Left = 0, Right = 1 }
    public enum HandFinger { Thumb = 0, Index = 1, Middle = 2, Ring = 3, Pinky = 4 }
    public enum HandJointId
    {
        Invalid = -1, HandStart = 0, HandPalm = HandStart + 0, HandWristRoot = HandStart + 1,
        HandThumb1 = HandStart + 2, HandThumb2 = HandStart + 3, HandThumb3 = HandStart + 4, HandThumbTip = HandStart + 5,
        HandIndex0 = HandStart + 6, HandIndex1 = HandStart + 7, HandIndex2 = HandStart + 8, HandIndex3 = HandStart + 9, HandIndexTip = HandStart + 10,
        HandMiddle0 = HandStart + 11, HandMiddle1 = HandStart + 12, HandMiddle2 = HandStart + 13, HandMiddle3 = HandStart + 14, HandMiddleTip = HandStart + 15,
        HandRing0 = HandStart + 16, HandRing1 = HandStart + 17, HandRing2 = HandStart + 18, HandRing3 = HandStart + 19, HandRingTip = HandStart + 20,
        HandPinky0 = HandStart + 21, HandPinky1 = HandStart + 22, HandPinky2 = HandStart + 23, HandPinky3 = HandStart + 24, HandPinkyTip = HandStart + 25,
        HandEnd = HandStart + 26,
    }
    public static class Constants
    {
        public static readonly Vector3 RightProximal = Vector3.back;
        public static readonly Vector3 RightDistal = Vector3.forward;
        public static readonly Vector3 RightPinkySide = Vector3.right;
        public static readonly Vector3 RightThumbSide = Vector3.left;
        public static readonly Vector3 RightPalmar = Vector3.down;
        public static readonly Vector3 RightDorsal = Vector3.up;
        public static readonly Vector3 LeftProximal = Vector3.back;
        public static readonly Vector3 LeftDistal = Vector3.forward;
        public static readonly Vector3 LeftPinkySide = Vector3.left;
        public static readonly Vector3 LeftThumbSide = Vector3.right;
        public static readonly Vector3 LeftPalmar = Vector3.down;
        public static readonly Vector3 LeftDorsal = Vector3.up;
    }
    public struct ReadOnlyHandJointPoses { }
    public interface IHand
    {
        Handedness Handedness { get; }
        bool IsConnected { get; }
        bool IsHighConfidence { get; }
        bool IsDominantHand { get; }
        float Scale { get; }
        bool GetFingerIsPinching(HandFinger finger);
        bool GetIndexFingerIsPinching();
        bool IsPointerPoseValid { get; }
        bool GetPointerPose(out Pose pose);
        bool GetJointPose(HandJointId handJointId, out Pose pose);
        bool GetJointPoseLocal(HandJointId handJointId, out Pose pose);
        bool GetJointPosesLocal(out ReadOnlyHandJointPoses localJointPoses);
        bool GetJointPoseFromWrist(HandJointId handJointId, out Pose pose);
        bool GetJointPosesFromWrist(out ReadOnlyHandJointPoses jointPosesFromWrist);
        bool GetPalmPoseLocal(out Pose pose);
        bool GetFingerIsHighConfidence(HandFinger finger);
        float GetFingerPinchStrength(HandFinger finger);
        bool IsTrackedDataValid { get; }
        bool GetRootPose(out Pose pose);
        int CurrentDataVersion { get; }
        event Action WhenHandUpdated;
    }
    public class Hand : MonoBehaviour, IHand
    {
        public Handedness Handedness => default;
        public bool IsConnected => false;
        public bool IsHighConfidence => false;
        public bool IsDominantHand => false;
        public float Scale => 1f;
        public bool GetFingerIsPinching(HandFinger finger) => false;
        public bool GetIndexFingerIsPinching() => false;
        public bool IsPointerPoseValid => false;
        public bool GetPointerPose(out Pose pose) { pose = default; return false; }
        public bool GetJointPose(HandJointId handJointId, out Pose pose) { pose = default; return false; }
        public bool GetJointPoseLocal(HandJointId handJointId, out Pose pose) { pose = default; return false; }
        public bool GetJointPosesLocal(out ReadOnlyHandJointPoses localJointPoses) { localJointPoses = default; return false; }
        public bool GetJointPoseFromWrist(HandJointId handJointId, out Pose pose) { pose = default; return false; }
        public bool GetJointPosesFromWrist(out ReadOnlyHandJointPoses jointPosesFromWrist) { jointPosesFromWrist = default; return false; }
        public bool GetPalmPoseLocal(out Pose pose) { pose = default; return false; }
        public bool GetFingerIsHighConfidence(HandFinger finger) => false;
        public float GetFingerPinchStrength(HandFinger finger) => 0f;
        public bool IsTrackedDataValid => false;
        public bool GetRootPose(out Pose pose) { pose = default; return false; }
        public int CurrentDataVersion => 0;
        public event Action WhenHandUpdated;
    }
}

public class OVRDisplay
{
    public event System.Action RecenteredPose;
}

public class OVRManager : MonoBehaviour
{
    public static OVRManager instance { get; private set; }
    public static OVRDisplay display { get; private set; }
    public bool isInsightPassthroughEnabled = false;
    public static event Action HMDMounted;
    public static event Action HMDUnmounted;
    public static event Action InputFocusAcquired;
    public static event Action InputFocusLost;
}

public class OVRPassthroughLayer : MonoBehaviour { }

public class OVRCameraRig : MonoBehaviour
{
    public Transform trackingSpace { get; private set; }
    public Transform centerEyeAnchor { get; private set; }
}

namespace Meta.XR.MRUtilityKit
{
    public partial class MRUK : MonoBehaviour
    {
        public static MRUK Instance { get; private set; }
        public bool IsInitialized { get; private set; } = false;
        public void RegisterSceneLoadedCallback(UnityAction callback) { }
        public MRUKRoom GetCurrentRoom() => null;
        public List<MRUKRoom> Rooms => null;
    }
    public class MRUKRoom : MonoBehaviour
    {
        public List<MRUKAnchor> Anchors => null;
    }
    public class MRUKAnchor : MonoBehaviour
    {
        [Flags]
        public enum SceneLabels { FLOOR = 1 << 0, CEILING = 1 << 1, WALL_FACE = 1 << 2, TABLE = 1 << 3, COUCH = 1 << 4 }
        public SceneLabels Label { get; internal set; }
        public Rect? PlaneRect { get; internal set; }
        public Bounds? VolumeBounds { get; internal set; }
        public bool HasAnyLabel(SceneLabels labelFlags) => (Label & labelFlags) != 0;
    }
}
