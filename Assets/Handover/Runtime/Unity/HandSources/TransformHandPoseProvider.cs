using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Reads palms from two transforms whose local +Y is the palm normal and local +Z points to
    /// the fingertips (the palm frame). Useful for custom rigs, recorded animation or a simulator
    /// that exposes palm transforms. Validity comes from the GameObjects being active and the
    /// optional per-hand flags; no confidence value is invented.
    /// </summary>
    [AddComponentMenu("Handover/Transform Hand Pose Provider")]
    public sealed class TransformHandPoseProvider : HandPoseSource
    {
        [SerializeField] private Transform _leftPalm;
        [SerializeField] private Transform _rightPalm;
        [Tooltip("Clear to report a hand as untracked without disabling its transform.")]
        [SerializeField] private bool _leftValid = true;
        [SerializeField] private bool _rightValid = true;

        private readonly PalmSample[] _samples = new PalmSample[2];
        private int _epoch;

        public override int OriginEpoch => _epoch;

        public void SetValid(HandId hand, bool valid)
        {
            if (hand == HandId.Left)
            {
                _leftValid = valid;
            }
            else
            {
                _rightValid = valid;
            }
        }

        /// <summary>Call when the tracking origin is reset so the simulation invalidates connections.</summary>
        public void NotifyOriginReset()
        {
            _epoch++;
        }

        public override void Poll()
        {
            double now = Now;
            _samples[0] = Sample(HandId.Left, _leftPalm, _leftValid, now);
            _samples[1] = Sample(HandId.Right, _rightPalm, _rightValid, now);
        }

        private PalmSample Sample(HandId hand, Transform palm, bool valid, double now)
        {
            if (palm == null || !palm.gameObject.activeInHierarchy || !valid)
            {
                return PalmSample.Invalid(hand, now, _epoch);
            }

            return new PalmSample(hand, palm.ToPose3(), true, now, _epoch);
        }

        public override PalmSample GetSample(HandId hand) => _samples[(int)hand];
    }
}
