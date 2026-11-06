using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Builds palm-frame poses (local +Y = palm normal, local +Z = towards fingertips).
    /// </summary>
    public static class PalmBasis
    {
        /// <summary>
        /// Derives a palm pose from tracked joint positions: forward from the wrist to
        /// the middle knuckle, lateral across the knuckles (little to index), then the
        /// perpendicular normal. Handedness convention (Unity world frame, verified by
        /// tests): for a right hand normal = forward x lateral; for a left hand the
        /// lateral axis points the other way, so normal = lateral x forward. In both
        /// cases a palm-up hand yields +Y.
        /// </summary>
        public static bool TryFromJointPositions(
            HandId hand,
            Vector3 wrist,
            Vector3 middleKnuckle,
            Vector3 indexKnuckle,
            Vector3 littleKnuckle,
            out Pose3 palm)
        {
            Vector3 forward = middleKnuckle - wrist;
            Vector3 lateral = indexKnuckle - littleKnuckle;
            if (forward.LengthSquared() < 1e-8f || lateral.LengthSquared() < 1e-8f)
            {
                palm = Pose3.Identity;
                return false;
            }

            forward = Vector3.Normalize(forward);
            lateral = Vector3.Normalize(HMath.ProjectOnPlane(lateral, forward));
            if (lateral.LengthSquared() < 1e-8f || !HMath.IsFinite(lateral))
            {
                palm = Pose3.Identity;
                return false;
            }

            Vector3 normal = hand == HandId.Right
                ? Vector3.Cross(forward, lateral)
                : Vector3.Cross(lateral, forward);

            Vector3 centre = Vector3.Lerp(wrist, middleKnuckle, 0.5f);
            palm = new Pose3(centre, HMath.LookRotation(forward, normal));
            return palm.IsFinite;
        }

        /// <summary>
        /// Converts a provider's documented palm joint pose into the palm frame using
        /// the provider's own axis constants (for example Interaction SDK's
        /// <c>Constants.RightPalmar</c> / <c>RightDistal</c>).
        /// </summary>
        public static Pose3 FromProviderJoint(Pose3 joint, Vector3 palmarLocalAxis, Vector3 distalLocalAxis)
        {
            Vector3 normal = joint.TransformDirection(palmarLocalAxis);
            Vector3 forward = joint.TransformDirection(distalLocalAxis);
            return new Pose3(joint.Position, HMath.LookRotation(forward, normal));
        }

        /// <summary>
        /// A palm-up pose for synthetic input: yaw turns the fingertips around world up
        /// (0 = +Z), roll tilts the palm around its own forward axis (positive rolls the
        /// palm normal towards +X for yaw 0), pitch lifts the fingertips.
        /// </summary>
        public static Pose3 PalmUp(Vector3 centre, float yawDegrees = 0f, float rollDegrees = 0f, float pitchDegrees = 0f)
        {
            Quaternion yaw = HMath.AngleAxis(yawDegrees, Vector3.UnitY);
            Quaternion pitch = HMath.AngleAxis(-pitchDegrees, Vector3.UnitX);
            Quaternion roll = HMath.AngleAxis(-rollDegrees, Vector3.UnitZ);
            return new Pose3(centre, Quaternion.Normalize(yaw * pitch * roll));
        }

        /// <summary>A palm-down pose (normal towards -Y) for synthetic shielding input.</summary>
        public static Pose3 PalmDown(Vector3 centre, float yawDegrees = 0f)
        {
            Quaternion yaw = HMath.AngleAxis(yawDegrees, Vector3.UnitY);
            Quaternion flip = HMath.AngleAxis(180f, Vector3.UnitZ);
            return new Pose3(centre, Quaternion.Normalize(yaw * flip));
        }
    }
}
