using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>Shared references for the services stepped by <see cref="HandoverSimulation"/>.</summary>
    public sealed class SimulationContext
    {
        public SimulationContext(ChapterDefinition chapter, RescueLedger ledger, PalmPlatform[] palms,
            SimulationClock clock, SessionService session, List<SimEvent> events)
        {
            Chapter = chapter;
            Ledger = ledger;
            Palms = palms;
            Clock = clock;
            Session = session;
            Events = events;
        }

        public ChapterDefinition Chapter { get; }
        public RescueLedger Ledger { get; }
        public PalmPlatform[] Palms { get; }
        public SimulationClock Clock { get; }
        public SessionService Session { get; }
        public List<SimEvent> Events { get; }

        /// <summary>Effective tuning (steady mode applied).</summary>
        public InteractionTuning Tuning { get; internal set; }
        public IDockPolicy Policy { get; internal set; }
        public StageLayout Stage { get; internal set; }
        public int ProviderEpoch { get; internal set; }

        public PalmPlatform Palm(HandId hand) => Palms[(int)hand];

        public void Emit(in SimEvent e) => Events.Add(e);
    }
}
