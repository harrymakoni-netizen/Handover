using System;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>Position and rotation in the Unity-convention world frame (metres, +Y up).</summary>
    public readonly struct Pose3 : IEquatable<Pose3>
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public Pose3(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static Pose3 Identity => new Pose3(Vector3.Zero, Quaternion.Identity);

        public Vector3 Right => HMath.Rotate(Rotation, Vector3.UnitX);
        public Vector3 Up => HMath.Rotate(Rotation, Vector3.UnitY);
        public Vector3 Forward => HMath.Rotate(Rotation, Vector3.UnitZ);

        public Vector3 TransformPoint(Vector3 local)
        {
            return Position + HMath.Rotate(Rotation, local);
        }

        public Vector3 InverseTransformPoint(Vector3 world)
        {
            return HMath.Rotate(Quaternion.Inverse(Rotation), world - Position);
        }

        public Vector3 TransformDirection(Vector3 local)
        {
            return HMath.Rotate(Rotation, local);
        }

        public Vector3 InverseTransformDirection(Vector3 world)
        {
            return HMath.Rotate(Quaternion.Inverse(Rotation), world);
        }

        public bool IsFinite => HMath.IsFinite(Position) && HMath.IsFinite(Rotation);

        public bool Equals(Pose3 other)
        {
            return Position.Equals(other.Position) && Rotation.Equals(other.Rotation);
        }

        public override bool Equals(object obj)
        {
            return obj is Pose3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Rotation);
        }

        public override string ToString()
        {
            return $"Pose3({Position.X:0.000}, {Position.Y:0.000}, {Position.Z:0.000})";
        }
    }
}
