using Handover.Core;
using Handover.Unity;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Handover.MetaPlatform
{
    /// <summary>
    /// The one SDK hand adapter: reads Interaction SDK <see cref="IHand"/>s and produces
    /// <see cref="PalmSample"/>s. Uses the documented palm joint (<c>HandJointId.HandPalm</c>) with
    /// ISDK's own Palmar/Distal axis constants, or derives the basis from wrist and knuckle
    /// positions. Validity is ISDK's tracked/connected state; freshness follows the hand's
    /// data version; recentering advances the origin epoch. No confidence value is invented.
    /// API names checked against com.meta.xr.sdk.interaction 207.0.1 sources.
    /// </summary>
    [AddComponentMenu("Handover/Meta Hand Pose Source")]
    [DefaultExecutionOrder(-60)]
    public sealed class MetaHandPoseSource : HandPoseSource
    {
        [SerializeField, Interface(typeof(IHand))] private Object _leftHand;
        [SerializeField, Interface(typeof(IHand))] private Object _rightHand;
        [Tooltip("Use the palm joint's documented orientation; otherwise derive it from wrist and knuckle positions.")]
        [SerializeField] private bool _usePalmJointRotation = true;

        private IHand _left;
        private IHand _right;
        private readonly PalmSample[] _samples = new PalmSample[2];
        private readonly int[] _lastVersion = { int.MinValue, int.MinValue };
        private readonly double[] _lastStamp = new double[2];
        private int _epoch;
        private bool _subscribed;

        public override int OriginEpoch => _epoch;

        public void SetHands(IHand left, IHand right)
        {
            _left = left;
            _right = right;
            _leftHand = left as Object;
            _rightHand = right as Object;
        }

        private void Awake()
        {
            _left = _leftHand as IHand;
            _right = _rightHand as IHand;
            if (_left == null || _right == null)
            {
                FindRigHands();
            }
        }

        /// <summary>Picks the rig's top-level <see cref="Hand"/> components (exact type, not derived visuals).</summary>
        private void FindRigHands()
        {
            foreach (Hand hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (hand.GetType() != typeof(Hand))
                {
                    continue;
                }

                if (hand.Handedness == Handedness.Left && _left == null)
                {
                    _left = hand;
                }
                else if (hand.Handedness == Handedness.Right && _right == null)
                {
                    _right = hand;
                }
            }

            if (_left == null || _right == null)
            {
                Debug.LogWarning("[Handover] MetaHandPoseSource could not find both Interaction SDK hands; assign them in the Inspector.");
            }
        }

        private void Update()
        {
            // OVRManager.display exists once OVRManager has initialised.
            if (!_subscribed && OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose += OnRecentered;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose -= OnRecentered;
            }
        }

        private void OnRecentered()
        {
            _epoch++;
        }

        public override void Poll()
        {
            _samples[0] = Sample(HandId.Left, _left);
            _samples[1] = Sample(HandId.Right, _right);
        }

        public override PalmSample GetSample(HandId hand) => _samples[(int)hand];

        public override string Describe()
        {
            return $"Interaction SDK hands (L {(_samples[0].IsValid ? "ok" : "lost")}, R {(_samples[1].IsValid ? "ok" : "lost")}, epoch {_epoch})";
        }

        private PalmSample Sample(HandId id, IHand hand)
        {
            double now = Now;
            int i = (int)id;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid)
            {
                return PalmSample.Invalid(id, now, _epoch);
            }

            // A new data version is a fresh sample; an unchanged one ages until it goes stale.
            int version = hand.CurrentDataVersion;
            if (version != _lastVersion[i])
            {
                _lastVersion[i] = version;
                _lastStamp[i] = now;
            }

            if (!TryPalmPose(id, hand, out Core.Pose3 palm))
            {
                return PalmSample.Invalid(id, now, _epoch);
            }

            return new PalmSample(id, palm, true, _lastStamp[i], _epoch);
        }

        private bool TryPalmPose(HandId id, IHand hand, out Core.Pose3 palm)
        {
            bool left = hand.Handedness == Handedness.Left;
            if (_usePalmJointRotation && hand.GetJointPose(HandJointId.HandPalm, out Pose joint))
            {
                Vector3 palmar = left ? Oculus.Interaction.Input.Constants.LeftPalmar : Oculus.Interaction.Input.Constants.RightPalmar;
                Vector3 distal = left ? Oculus.Interaction.Input.Constants.LeftDistal : Oculus.Interaction.Input.Constants.RightDistal;
                palm = PalmBasis.FromProviderJoint(joint.ToPose3(), palmar.ToNumerics(), distal.ToNumerics());
                return palm.IsFinite;
            }

            if (hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)
                && hand.GetJointPose(HandJointId.HandMiddle1, out Pose middle)
                && hand.GetJointPose(HandJointId.HandIndex1, out Pose index)
                && hand.GetJointPose(HandJointId.HandPinky1, out Pose little))
            {
                NVector3 w = wrist.position.ToNumerics();
                if (PalmBasis.TryFromJointPositions(id, w, middle.position.ToNumerics(), index.position.ToNumerics(), little.position.ToNumerics(), out palm))
                {
                    // Prefer the documented palm joint position as the centre when it is available.
                    if (hand.GetJointPose(HandJointId.HandPalm, out Pose palmJoint))
                    {
                        palm = new Core.Pose3(palmJoint.position.ToNumerics(), palm.Rotation);
                    }

                    return true;
                }
            }

            palm = Core.Pose3.Identity;
            return false;
        }
    }
}
