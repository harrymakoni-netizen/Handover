using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Deterministic hand input that honours the same <see cref="IHandPoseProvider"/>
    /// contract as the SDK adapter. Tests and the Editor keyboard driver set palm
    /// poses directly; <see cref="Tick"/> stamps fresh samples, <see cref="Freeze"/>
    /// lets a hand go stale and <see cref="Recenter"/> starts a new origin epoch.
    /// </summary>
    public sealed class SyntheticHandPoseProvider : IHandPoseProvider
    {
        private readonly Pose3[] _poses = new Pose3[2];
        private readonly bool[] _valid = new bool[2];
        private readonly bool[] _frozen = new bool[2];
        private readonly PalmSample[] _samples = new PalmSample[2];

        public double Now { get; private set; }
        public int OriginEpoch { get; private set; }

        public SyntheticHandPoseProvider()
        {
            for (int i = 0; i < 2; i++)
            {
                _poses[i] = Pose3.Identity;
                _samples[i] = PalmSample.Invalid((HandId)i, 0.0, 0);
            }
        }

        public void SetPose(HandId hand, Pose3 palmFramePose, bool valid = true)
        {
            _poses[(int)hand] = palmFramePose;
            _valid[(int)hand] = valid;
            _frozen[(int)hand] = false;
            Stamp(hand);
        }

        public void SetPalmUp(HandId hand, Vector3 centre, float yawDegrees = 0f, float rollDegrees = 0f)
        {
            SetPose(hand, PalmBasis.PalmUp(centre, yawDegrees, rollDegrees));
        }

        public Pose3 GetPose(HandId hand) => _poses[(int)hand];

        public bool IsValid(HandId hand) => _valid[(int)hand];

        /// <summary>The hand disappears (invalid samples keep arriving).</summary>
        public void Hide(HandId hand)
        {
            _valid[(int)hand] = false;
            _frozen[(int)hand] = false;
            Stamp(hand);
        }

        /// <summary>The provider stops updating this hand: its last sample goes stale.</summary>
        public void Freeze(HandId hand)
        {
            _frozen[(int)hand] = true;
        }

        public void Recenter()
        {
            OriginEpoch++;
            Stamp(HandId.Left);
            Stamp(HandId.Right);
        }

        /// <summary>Advances the provider clock and stamps every non-frozen hand with a fresh sample.</summary>
        public void Tick(double deltaSeconds)
        {
            Now += deltaSeconds;
            Stamp(HandId.Left);
            Stamp(HandId.Right);
        }

        public PalmSample GetSample(HandId hand) => _samples[(int)hand];

        private void Stamp(HandId hand)
        {
            int i = (int)hand;
            if (_frozen[i])
            {
                return;
            }

            _samples[i] = new PalmSample(hand, _poses[i], _valid[i], Now, OriginEpoch);
        }
    }
}
