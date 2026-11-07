using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Handover.Unity
{
    /// <summary>
    /// Component-wise conversion between UnityEngine and System.Numerics. Both use the same
    /// (x, y, z, w) layout and composition order, and the core runs in Unity's world frame,
    /// so no axis flips are involved.
    /// </summary>
    public static class UnityConvert
    {
        public static NVector3 ToNumerics(this Vector3 v) => new NVector3(v.x, v.y, v.z);

        public static Vector3 ToUnity(this NVector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static NQuaternion ToNumerics(this Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);

        public static Quaternion ToUnity(this NQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        public static Core.Pose3 ToPose3(this Pose pose) => new Core.Pose3(pose.position.ToNumerics(), pose.rotation.ToNumerics());

        public static Core.Pose3 ToPose3(this Transform t) => new Core.Pose3(t.position.ToNumerics(), t.rotation.ToNumerics());

        public static Pose ToUnity(this Core.Pose3 pose) => new Pose(pose.Position.ToUnity(), pose.Rotation.ToUnity());
    }
}
