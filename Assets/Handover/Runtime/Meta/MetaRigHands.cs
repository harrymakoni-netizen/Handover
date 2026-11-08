using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Handover.MetaPlatform
{
    /// <summary>
    /// Finds the rig's tracked hands by following serialized data-source references only, so it is
    /// safe in Edit mode and before the Interaction SDK has started. A tracked hand is an exact-type
    /// <see cref="Hand"/> whose modifier chain ends at a <see cref="FromOVRHandDataSource"/>.
    /// That excludes the derived filter and visual hands, and the comprehensive rig's synthetic
    /// prefab hands such as <c>HandSphereMap</c>: those are fed by a
    /// <see cref="FromHandPrefabDataSource"/> that always reports itself as tracked at the prefab's
    /// pose, and asking one for <see cref="Hand.Handedness"/> before its Awake has run throws
    /// (<c>Hand.Handedness</c> evaluates <c>GetData()</c>). Checked against
    /// com.meta.xr.sdk.interaction and com.meta.xr.sdk.interaction.ovr 207.0.1: the comprehensive
    /// rig nests <c>OVRHandsDataSource</c>, one Hand per side over LastKnownGoodHand over
    /// FromOVRHandDataSource.
    /// </summary>
    public static class MetaRigHands
    {
        private const int MaxChainLength = 16;

        /// <summary>The data source at the end of the hand's modifier chain. Evaluates no hand data.</summary>
        public static IDataSource<HandDataAsset> RootSource(Hand hand)
        {
            IDataSource<HandDataAsset> source = hand != null ? hand.ModifyDataFromSource : null;
            for (int i = 0; i < MaxChainLength && source is DataModifier<HandDataAsset> modifier; i++)
            {
                source = modifier.ModifyDataFromSource;
            }

            return source;
        }

        public static bool IsTrackedRigHand(Hand hand)
        {
            return hand != null && hand.GetType() == typeof(Hand) && RootSource(hand) is FromOVRHandDataSource;
        }

        /// <summary>Active tracked rig hands. The comprehensive rig has exactly one per side.</summary>
        public static List<Hand> FindTrackedRigHands()
        {
            var hands = new List<Hand>();
            foreach (Hand hand in Object.FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (IsTrackedRigHand(hand))
                {
                    hands.Add(hand);
                }
            }

            return hands;
        }
    }
}
