using System;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Support-edge measurements. Hops are judged on the gap between support edges, not
    /// wrist distance: each palm carries one bounded ellipse near its centre, each berth a
    /// small disc.
    /// </summary>
    public static class SupportGeometry
    {
        /// <summary>Radius of a palm's support ellipse along a world direction (projected into the palm plane).</summary>
        public static float PalmRadiusAlong(Pose3 palmFrame, Vector3 worldDirection, float halfWidth, float halfLength)
        {
            Vector3 local = palmFrame.InverseTransformDirection(worldDirection);
            float lx = local.X;
            float lz = local.Z;
            float length = (float)Math.Sqrt(lx * lx + lz * lz);
            if (length < 1e-5f)
            {
                return Math.Min(halfWidth, halfLength);
            }

            float ux = lx / length / halfWidth;
            float uz = lz / length / halfLength;
            return 1f / (float)Math.Sqrt(ux * ux + uz * uz);
        }

        public readonly struct Measurement
        {
            public readonly float EdgeGap;
            public readonly float VerticalOffset;
            public readonly float CentreDistance;

            public Measurement(float edgeGap, float verticalOffset, float centreDistance)
            {
                EdgeGap = edgeGap;
                VerticalOffset = verticalOffset;
                CentreDistance = centreDistance;
            }
        }

        public static Measurement PalmToPalm(PalmPlatform a, PalmPlatform b, InteractionTuning t)
        {
            Vector3 ca = a.SupportCenter;
            Vector3 cb = b.SupportCenter;
            Vector3 d = HMath.Horizontal(cb - ca);
            float distance = d.Length();
            Vector3 u = distance > 1e-5f ? d / distance : HMath.Right;
            float ra = PalmRadiusAlong(a.FilteredPose, u, t.SupportHalfWidth, t.SupportHalfLength);
            float rb = PalmRadiusAlong(b.FilteredPose, -u, t.SupportHalfWidth, t.SupportHalfLength);
            return new Measurement(distance - ra - rb, Math.Abs(cb.Y - ca.Y), distance);
        }

        public static Measurement PalmToBerth(PalmPlatform palm, Vector3 berth, InteractionTuning t)
        {
            Vector3 c = palm.SupportCenter;
            Vector3 d = HMath.Horizontal(berth - c);
            float distance = d.Length();
            Vector3 u = distance > 1e-5f ? d / distance : HMath.Right;
            float r = PalmRadiusAlong(palm.FilteredPose, u, t.SupportHalfWidth, t.SupportHalfLength);
            return new Measurement(distance - r - t.BerthSupportRadius, Math.Abs(berth.Y - c.Y), distance);
        }

        /// <summary>Kinematic hop arc derived from world up (no rigidbody ballistics).</summary>
        public static Vector3 Arc(Vector3 from, Vector3 to, float t, float arcHeight)
        {
            t = HMath.Clamp01(t);
            Vector3 p = Vector3.Lerp(from, to, t);
            return p + HMath.Up * (arcHeight * 4f * t * (1f - t));
        }
    }
}
