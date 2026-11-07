namespace Handover.Core
{
    /// <summary>
    /// Captions carry instructions (Section 7). Derived from state only, so the same words
    /// appear whatever the presentation, and colour or sound is never the only signal.
    /// </summary>
    public static class CaptionModel
    {
        public const string ShelterHint = "Shelter them: hold your free hand palm-down in the rain, a little behind your carrying hand.";

        public static string Current(HandoverSimulation sim)
        {
            ChapterState s = sim.Director.State;
            switch (sim.Session.State)
            {
                case SessionState.Setup:
                    return "Look at your table, then confirm the stage or choose the virtual tabletop.";
                case SessionState.Ready:
                    return "Offer a palm beside Keeper to begin.";
                case SessionState.Paused:
                    return "Paused. Keeper and friends are waiting safely.";
                case SessionState.Recovering:
                    return sim.RecoveryNeedsConfirmation
                        ? "Choose Return to safety to bring the passenger back to a dock."
                        : "Bring your hand back to the glowing marker, or choose Return to safety.";
                case SessionState.Results:
                    return "Everyone is home.";
                case SessionState.Error:
                    return sim.Session.ErrorMessage ?? "Something went wrong. Restart the chapter from the menu.";
            }

            // Playing
            bool keeperRiding = sim.Ledger.Doll(DollId.Keeper).Owner.Kind == OwnerKind.Palm;
            if (!s.TutorialHopDone)
            {
                return keeperRiding
                    ? "Hold your other palm beside Keeper's palm, then keep steady."
                    : "Offer a palm beside Keeper.";
            }

            if (!s.LanternEverLit)
            {
                return keeperRiding ? "Carry Keeper to the lantern refuge." : "Bring Keeper to the lantern refuge.";
            }

            if (s.LanternLatched)
            {
                return s.IsRescued(DollId.Keeper) ? "Everyone is home." : "The lantern holds on its own. Bring Keeper home.";
            }

            if (!sim.Director.LanternLit)
            {
                return "The harbour is dark. Return Keeper to the lantern.";
            }

            if (s.OrdinaryRescuedCount == 0)
            {
                return "Carry Pip or Luma to the harbour. Shelter them from the rain, or rest at the low refuge.";
            }

            return s.LowRefuge == RefugeStatus.Open
                ? "Bring the last passenger to the harbour."
                : "The low refuge has flooded. Cross directly, sheltered, to the harbour.";
        }

        public static string Refusal(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.KeeperFirst: return "Keeper goes first.";
                case RejectReason.LanternLocked: return "Pass Keeper from one palm to the other first.";
                case RejectReason.LanternKeeperOnly: return "Only Keeper can hold the lantern.";
                case RejectReason.HarbourDark: return "The harbour is dark until Keeper holds the lantern.";
                case RejectReason.KeeperComesHomeLast: return "Keeper comes home last.";
                case RejectReason.RefugeClosed: return "The low refuge is flooding.";
                case RejectReason.NotHomeBerth: return "That is someone else's place.";
                case RejectReason.AlreadyHome: return "They are safe at the harbour.";
                case RejectReason.DockFull: return "There is no room there.";
                case RejectReason.HarbourLanding: return "Wait for the landing to finish.";
                default: return null;
            }
        }

        public static string FitProblem(FitIssue issue)
        {
            switch (issue)
            {
                case FitIssue.TooSmall: return "That table is too small for the stage.";
                case FitIssue.TooFar: return "That table is out of comfortable reach.";
                case FitIssue.BadHeight: return "That surface is too high or too low for seated play.";
                case FitIssue.OutOfReach: return "Part of the stage would be out of comfortable reach.";
                case FitIssue.OutsideFieldOfView: return "Part of the stage would be hard to see.";
                case FitIssue.Obstructed: return "Something is in the way.";
                case FitIssue.StageShifted: return "The stage moved. Please place it again.";
                default: return null;
            }
        }
    }
}
