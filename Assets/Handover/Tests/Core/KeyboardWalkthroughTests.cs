using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>
    /// Plays the keyboard walkthrough documented in Docs/Setup.md against the real rules, using
    /// the same placement maths as SyntheticHandDriver (snaps lift the palm clear for two frames
    /// before landing beside a berth; G raises the active palm 8 cm and brings the other alongside).
    /// If the driver's placement changes, change this script with it.
    /// </summary>
    public class KeyboardWalkthroughTests
    {
        private sealed class Keyboard
        {
            private readonly TestRig _rig;
            private readonly Vector3[] _support = new Vector3[2];
            public HandId Active = HandId.Right;

            public Keyboard(TestRig rig)
            {
                _rig = rig;
                Vector3 origin = TestRig.StageOrigin;
                _support[0] = origin + new Vector3(-0.14f, 0.2f, -0.2f);
                _support[1] = origin + new Vector3(0.14f, 0.2f, -0.2f);
                Apply(HandId.Left);
                Apply(HandId.Right);
                rig.Settle();
            }

            private void Apply(HandId hand) => _rig.SetSupport(hand, _support[(int)hand]);

            public void Tab() => Active = Active.Other();

            /// <summary>Keys 1-4.</summary>
            public void Snap(DockId dock)
            {
                HandId hand = Active;
                DockLayout layout = _rig.Stage.Dock(dock);
                DollId riding = _rig.Ledger.PalmDoll(hand);
                int berth = 0;
                int best = int.MaxValue;
                for (int b = 0; b < layout.Capacity; b++)
                {
                    SlotState s = _rig.Ledger.Get(SlotRef.DockBerth(dock, b));
                    bool wanted = riding == DollId.None
                        ? s.IsOccupied
                        : s.IsEmpty && (layout.NamedBerths == null || layout.BerthOwner(b) == riding);
                    int score = wanted ? b : 100 + b;
                    if (score < best)
                    {
                        best = score;
                        berth = b;
                    }
                }

                Vector3 towards = HMath.Horizontal(_rig.Stage.Pose.Position - layout.Center);
                Vector3 side = towards.LengthSquared() > 1e-6f ? Vector3.Normalize(towards) : -Vector3.UnitZ;
                float offset = _rig.Tuning.SupportHalfWidth + _rig.Tuning.BerthSupportRadius + 0.015f;

                _support[(int)hand] += new Vector3(0f, 0.15f, 0f);
                Apply(hand);
                _rig.Step(2);
                _support[(int)hand] = layout.Berths[berth] + side * offset;
                Apply(hand);
            }

            /// <summary>Key G: raise the active palm and bring the other alongside.</summary>
            public void Alongside()
            {
                HandId anchor = Active;
                HandId mover = anchor.Other();
                _support[(int)anchor] += new Vector3(0f, 0.08f, 0f);
                float side = mover == HandId.Left ? -1f : 1f;
                _support[(int)mover] = _support[(int)anchor] + new Vector3(side * (2f * _rig.Tuning.SupportHalfWidth + 0.02f), 0f, 0f);
                Apply(anchor);
                Apply(mover);
            }
        }

        private static void Wait(TestRig rig, System.Func<bool> done, string what)
        {
            Assert.IsTrue(rig.RunUntil(done, 4f), "Walkthrough stalled: " + what);
            rig.Run(0.3f);
        }

        [Test]
        public void TheDocumentedKeySequenceCompletesTheChapter()
        {
            var rig = new TestRig();
            var keys = new Keyboard(rig);

            keys.Snap(DockId.Departure);                       // 1
            Wait(rig, () => rig.IsOnPalm(DollId.Keeper, HandId.Right), "Keeper boards the right palm");
            keys.Alongside();                                  // G
            Wait(rig, () => rig.IsOnPalm(DollId.Keeper, HandId.Left), "the teaching hop");
            Assert.IsTrue(rig.Ledger.PalmIsEmpty(HandId.Right), "nothing else boards the raised free palm");
            keys.Tab();
            keys.Snap(DockId.LanternRefuge);                   // 3
            Wait(rig, () => rig.IsAtBerth(DollId.Keeper, DockId.LanternRefuge, 0), "Keeper lights the lantern");
            keys.Tab();
            keys.Snap(DockId.Departure);                       // 1
            Wait(rig, () => rig.IsOnPalm(DollId.Pip, HandId.Right), "Pip boards");
            keys.Snap(DockId.LowRefuge);                       // 2
            Wait(rig, () => rig.IsAtBerth(DollId.Pip, DockId.LowRefuge, 0), "Pip rests at R");
            keys.Snap(DockId.LowRefuge);                       // 2
            Wait(rig, () => rig.IsOnPalm(DollId.Pip, HandId.Right), "Pip leaves R");
            keys.Snap(DockId.Harbour);                         // 4
            Wait(rig, () => rig.Ledger.DollIsAtDock(DollId.Pip, DockId.Harbour), "Pip is home");
            keys.Snap(DockId.Departure);                       // 1
            Wait(rig, () => rig.IsOnPalm(DollId.Luma, HandId.Right), "Luma boards");
            keys.Snap(DockId.Harbour);                         // 4
            Wait(rig, () => rig.Ledger.DollIsAtDock(DollId.Luma, DockId.Harbour), "Luma is home");
            keys.Snap(DockId.LanternRefuge);                   // 3
            Wait(rig, () => rig.IsOnPalm(DollId.Keeper, HandId.Right), "Keeper is released");
            keys.Snap(DockId.Harbour);                         // 4
            Wait(rig, () => rig.Sim.Session.State == SessionState.Results, "Keeper comes home");

            ChapterScript.AssertCompleted(rig, 1);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
        }
    }
}
