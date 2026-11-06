namespace Handover.Core
{
    /// <summary>
    /// One normalized hand sample, the only hand data the simulation consumes.
    /// <see cref="Pose"/> is in the palm frame: position is the palm centre, local +Y
    /// is the palm normal (the side a doll stands on) and local +Z points towards the
    /// fingertips. No SDK confidence value is invented: validity is the provider's own
    /// tracked/valid flag.
    /// </summary>
    public readonly struct PalmSample
    {
        public readonly HandId Hand;
        public readonly Pose3 Pose;
        public readonly bool IsValid;
        /// <summary>Monotonic time (seconds) at which the provider produced this pose.</summary>
        public readonly double SampleTime;
        /// <summary>Tracking-origin generation. Changes on recenter or origin reset.</summary>
        public readonly int OriginEpoch;

        public PalmSample(HandId hand, Pose3 pose, bool isValid, double sampleTime, int originEpoch)
        {
            Hand = hand;
            Pose = pose;
            IsValid = isValid && pose.IsFinite;
            SampleTime = sampleTime;
            OriginEpoch = originEpoch;
        }

        public static PalmSample Invalid(HandId hand, double sampleTime, int originEpoch)
        {
            return new PalmSample(hand, Pose3.Identity, false, sampleTime, originEpoch);
        }
    }

    /// <summary>
    /// The single adapter contract between hand data and the simulation. Vendor SDK
    /// types never cross it; synthetic test input implements the same interface.
    /// </summary>
    public interface IHandPoseProvider
    {
        /// <summary>Latest sample for a hand. Must not allocate.</summary>
        PalmSample GetSample(HandId hand);

        /// <summary>Current tracking-origin generation.</summary>
        int OriginEpoch { get; }

        /// <summary>Current time in the same monotonic time base as <see cref="PalmSample.SampleTime"/>.</summary>
        double Now { get; }
    }
}
