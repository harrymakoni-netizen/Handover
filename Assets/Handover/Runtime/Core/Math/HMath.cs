using System;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Engine-free math helpers. The world frame matches Unity: metres, +Y up,
    /// X right, Z forward (left-handed). System.Numerics stores quaternions as
    /// (x, y, z, w) and composes them the same way as UnityEngine.Quaternion,
    /// so values can be copied component-wise between the two.
    /// </summary>
    public static class HMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float Epsilon = 1e-6f;

        public static readonly Vector3 Up = Vector3.UnitY;
        public static readonly Vector3 Forward = Vector3.UnitZ;
        public static readonly Vector3 Right = Vector3.UnitX;

        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>Unsigned angle in degrees between two directions.</summary>
        public static float AngleDegrees(Vector3 a, Vector3 b)
        {
            float lengths = a.Length() * b.Length();
            if (lengths < Epsilon)
            {
                return 0f;
            }

            float cos = Clamp(Vector3.Dot(a, b) / lengths, -1f, 1f);
            return (float)Math.Acos(cos) * Rad2Deg;
        }

        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float length = v.Length();
            return length > Epsilon ? v / length : fallback;
        }

        public static Vector3 Horizontal(Vector3 v)
        {
            return new Vector3(v.X, 0f, v.Z);
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Horizontal(b - a).Length();
        }

        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 planeNormal)
        {
            return v - planeNormal * Vector3.Dot(v, planeNormal);
        }

        /// <summary>
        /// Frame-rate-independent exponential damping factor:
        /// alpha = 1 - exp(-deltaTime / timeConstant).
        /// </summary>
        public static float DampingAlpha(float deltaTime, float timeConstant)
        {
            if (timeConstant <= Epsilon)
            {
                return 1f;
            }

            if (deltaTime <= 0f)
            {
                return 0f;
            }

            return 1f - (float)Math.Exp(-deltaTime / timeConstant);
        }

        /// <summary>Shortest-path spherical interpolation.</summary>
        public static Quaternion SlerpShortest(Quaternion from, Quaternion to, float t)
        {
            if (Quaternion.Dot(from, to) < 0f)
            {
                to = Quaternion.Negate(to);
            }

            return Quaternion.Normalize(Quaternion.Slerp(from, to, t));
        }

        public static Vector3 Rotate(Quaternion rotation, Vector3 v)
        {
            return Vector3.Transform(v, rotation);
        }

        /// <summary>
        /// Rotation whose local +Z maps to <paramref name="forward"/> and local +Y is as
        /// close as possible to <paramref name="up"/> (same contract as Unity's
        /// Quaternion.LookRotation).
        /// </summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 f = SafeNormalize(forward, Forward);
            Vector3 u = SafeNormalize(up, Up);
            Vector3 r = Vector3.Cross(u, f);
            if (r.LengthSquared() < Epsilon)
            {
                // forward and up are parallel; pick any perpendicular right axis.
                Vector3 alternative = Math.Abs(f.Y) < 0.99f ? Up : Right;
                r = Vector3.Cross(alternative, f);
            }

            r = Vector3.Normalize(r);
            u = Vector3.Cross(f, r);

            // System.Numerics uses row vectors: each row is the image of a basis axis.
            var m = new Matrix4x4(
                r.X, r.Y, r.Z, 0f,
                u.X, u.Y, u.Z, 0f,
                f.X, f.Y, f.Z, 0f,
                0f, 0f, 0f, 1f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        public static Quaternion AngleAxis(float degrees, Vector3 axis)
        {
            return Quaternion.CreateFromAxisAngle(SafeNormalize(axis, Up), degrees * Deg2Rad);
        }

        /// <summary>Signed yaw (radians) of a direction around world up, 0 along +Z, positive towards +X.</summary>
        public static float YawOf(Vector3 direction)
        {
            return (float)Math.Atan2(direction.X, direction.Z);
        }

        public static Quaternion YawRotation(float yawRadians)
        {
            return Quaternion.CreateFromAxisAngle(Up, yawRadians);
        }

        public static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        }

        public static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.X) && IsFinite(q.Y) && IsFinite(q.Z) && IsFinite(q.W);
        }

        public static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }
    }
}
