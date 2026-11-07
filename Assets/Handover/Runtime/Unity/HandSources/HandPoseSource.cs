using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Base class for every hand adapter in a scene (SDK, transform, keyboard). It implements
    /// the core's single <see cref="IHandPoseProvider"/> contract, so vendor types never reach
    /// the simulation. Exactly one active source drives the palms.
    /// </summary>
    public abstract class HandPoseSource : MonoBehaviour, IHandPoseProvider
    {
        public abstract PalmSample GetSample(HandId hand);

        public abstract int OriginEpoch { get; }

        /// <summary>Monotonic time base shared with <see cref="PalmSample.SampleTime"/>.</summary>
        public virtual double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Called by <see cref="HandoverApp"/> once per frame before the simulation step.</summary>
        public virtual void Poll()
        {
        }

        /// <summary>Human-readable status for the debug overlay.</summary>
        public virtual string Describe() => GetType().Name;
    }
}
