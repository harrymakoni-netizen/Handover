using System;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>Captions carry the instructions through the whole chapter (Section 7).</summary>
    public class CaptionTests
    {
        [Test]
        public void CaptionsFollowTheChapter()
        {
            var rig = new TestRig(confirmStage: false);
            StringAssert.Contains("confirm the stage", CaptionModel.Current(rig.Sim));
            rig.Sim.ConfirmStage();
            StringAssert.Contains("Offer a palm beside Keeper", CaptionModel.Current(rig.Sim));

            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            StringAssert.Contains("other palm beside Keeper", CaptionModel.Current(rig.Sim));
            ChapterScript.HopAt(rig, HandId.Right, DollId.Keeper, DockId.Departure);
            StringAssert.Contains("lantern refuge", CaptionModel.Current(rig.Sim));
            rig.Deposit(HandId.Left, DollId.Keeper, DockId.LanternRefuge, 0);
            StringAssert.Contains("Pip or Luma", CaptionModel.Current(rig.Sim));

            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            StringAssert.Contains("flooded", CaptionModel.Current(rig.Sim));
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Luma);
            StringAssert.Contains("Bring Keeper home", CaptionModel.Current(rig.Sim));
            ChapterScript.BringKeeperHome(rig);
            Assert.That(CaptionModel.Current(rig.Sim), Is.EqualTo("Everyone is home."));
        }

        [Test]
        public void PauseAndRecoveryExplainWhatToDo()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Sim.Pause(PauseReason.Menu);
            StringAssert.Contains("Paused", CaptionModel.Current(rig.Sim));
            rig.Sim.Resume();
            rig.Hands.Hide(HandId.Right);
            rig.Run(0.3f);
            StringAssert.Contains("Return to safety", CaptionModel.Current(rig.Sim));
        }

        [Test]
        public void EveryRefusalAndFitProblemHasWords()
        {
            foreach (RejectReason reason in Enum.GetValues(typeof(RejectReason)))
            {
                if (reason == RejectReason.None || reason == RejectReason.NotRunning)
                {
                    continue;
                }

                Assert.That(CaptionModel.Refusal(reason), Is.Not.Null.And.Not.Empty, reason.ToString());
            }

            foreach (FitIssue issue in Enum.GetValues(typeof(FitIssue)))
            {
                if (issue != FitIssue.None)
                {
                    Assert.That(CaptionModel.FitProblem(issue), Is.Not.Null.And.Not.Empty, issue.ToString());
                }
            }
        }
    }
}
