using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>Where one passenger rests in a checkpoint. Always a dock berth: never a palm, a hop or the hold.</summary>
    public readonly struct DollPlacement
    {
        public readonly DollId Doll;
        public readonly DockId Dock;
        public readonly int Berth;

        public DollPlacement(DollId doll, DockId dock, int berth)
        {
            Doll = doll;
            Dock = dock;
            Berth = berth;
        }

        public SlotRef Slot => SlotRef.DockBerth(Dock, Berth);

        public override string ToString() => $"{Doll}@{Dock.Code()}[{Berth}]";
    }

    /// <summary>
    /// A safe logical snapshot: every owner normalized to a dock berth plus the chapter's
    /// logical state. Raw hand poses, room meshes and transaction progress are never part of it.
    /// </summary>
    public sealed class Checkpoint
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public int ConfigVersion = InteractionTuning.ConfigVersion;
        public string Label = "start";
        public DollPlacement[] Placements = new DollPlacement[0];
        public ChapterState State = new ChapterState();

        public DollPlacement PlacementOf(DollId doll)
        {
            foreach (DollPlacement p in Placements)
            {
                if (p.Doll == doll)
                {
                    return p;
                }
            }

            return default;
        }

        public Checkpoint Clone()
        {
            return new Checkpoint
            {
                SchemaVersion = SchemaVersion,
                ConfigVersion = ConfigVersion,
                Label = Label,
                Placements = (DollPlacement[])Placements.Clone(),
                State = State.Clone(),
            };
        }

        public override string ToString() => $"Checkpoint({Label}: {string.Join(", ", Placements)})";
    }

    /// <summary>Static rules a checkpoint must satisfy before it is accepted or restored.</summary>
    public static class CheckpointRules
    {
        /// <param name="berthCounts">Berths per dock, indexed by <see cref="DockId"/>.</param>
        /// <param name="homeBerths">Departure berth owned by each doll, indexed by <see cref="DollId"/>.</param>
        public static List<string> Validate(Checkpoint c, IReadOnlyList<int> berthCounts, IReadOnlyList<int> homeBerths)
        {
            var problems = new List<string>();
            if (c == null)
            {
                problems.Add("Checkpoint is missing.");
                return problems;
            }

            if (c.SchemaVersion != Checkpoint.CurrentSchemaVersion)
            {
                problems.Add($"Unsupported checkpoint schema {c.SchemaVersion}.");
                return problems;
            }

            ChapterState s = c.State ?? new ChapterState();
            if (c.Placements == null || c.Placements.Length != Ids.Dolls.Length)
            {
                problems.Add("A checkpoint must place exactly three passengers.");
                return problems;
            }

            var dolls = new HashSet<DollId>();
            var berths = new HashSet<SlotRef>();
            var atHarbour = new HashSet<DollId>();
            foreach (DollPlacement p in c.Placements)
            {
                if (p.Doll != DollId.Keeper && p.Doll != DollId.Pip && p.Doll != DollId.Luma)
                {
                    problems.Add($"Unknown passenger id {(int)p.Doll}.");
                    continue;
                }

                if (!dolls.Add(p.Doll))
                {
                    problems.Add($"{p.Doll} is placed twice.");
                }

                if ((int)p.Dock < 0 || (int)p.Dock > 3 || p.Berth < 0 || p.Berth >= berthCounts[(int)p.Dock])
                {
                    problems.Add($"{p} is outside the dock's capacity.");
                    continue;
                }

                if (!berths.Add(p.Slot))
                {
                    problems.Add($"Berth {p.Slot} holds two passengers.");
                }

                switch (p.Dock)
                {
                    case DockId.Departure:
                        if (homeBerths[(int)p.Doll] != p.Berth)
                        {
                            problems.Add($"{p.Doll} may only rest on its own Departure berth.");
                        }

                        break;
                    case DockId.LanternRefuge:
                        if (p.Doll != DollId.Keeper)
                        {
                            problems.Add("Only Keeper may hold the lantern refuge.");
                        }
                        else if (!s.TutorialHopDone)
                        {
                            problems.Add("Keeper is at the lantern before the teaching hop.");
                        }

                        break;
                    case DockId.LowRefuge:
                        if (s.LowRefuge == RefugeStatus.Closed)
                        {
                            problems.Add("The low refuge is closed but occupied.");
                        }

                        break;
                    case DockId.Harbour:
                        atHarbour.Add(p.Doll);
                        break;
                }
            }

            foreach (DollId d in s.Rescued)
            {
                if (d != DollId.Keeper && d != DollId.Pip && d != DollId.Luma)
                {
                    problems.Add($"Unknown rescued id {(int)d}.");
                }
                else if (!atHarbour.Contains(d))
                {
                    problems.Add($"{d} is counted as rescued but is not at the harbour.");
                }
            }

            foreach (DollId d in atHarbour)
            {
                if (!s.IsRescued(d))
                {
                    problems.Add($"{d} is at the harbour but not counted as rescued.");
                }
            }

            int ordinary = s.OrdinaryRescuedCount;
            if (s.IsRescued(DollId.Keeper) && !s.LanternLatched)
            {
                problems.Add("Keeper is home before the lantern was latched.");
            }

            if (s.Completed != (s.IsRescued(DollId.Keeper) && ordinary == 2))
            {
                problems.Add("Completion flag does not match the rescued passengers.");
            }

            if (s.LanternEverLit && !s.TutorialHopDone)
            {
                problems.Add("The lantern was lit before the teaching hop.");
            }

            if (s.Phase != FloodPhase.Calm && !s.LanternEverLit)
            {
                problems.Add("The flood advanced before Keeper lit the lantern.");
            }

            if (ordinary > 0 && !s.LanternEverLit)
            {
                problems.Add("A passenger was rescued before the lantern was lit.");
            }

            switch (ordinary)
            {
                case 0:
                    if (s.Phase == FloodPhase.RisingWater || s.Phase == FloodPhase.LastCrossing)
                    {
                        problems.Add("The water rose before any rescue.");
                    }

                    if (s.LowRefuge != RefugeStatus.Open)
                    {
                        problems.Add("The low refuge closed before any rescue.");
                    }

                    if (s.LanternLatched)
                    {
                        problems.Add("The lantern latched before both rescues.");
                    }

                    break;
                case 1:
                    if (s.Phase == FloodPhase.LastCrossing || s.Phase == FloodPhase.Calm)
                    {
                        problems.Add("Flood phase does not match one rescue.");
                    }

                    if (s.Phase == FloodPhase.RisingWater && s.LowRefuge == RefugeStatus.ClosurePending)
                    {
                        problems.Add("The water rose while the refuge closure was still pending.");
                    }

                    if (s.LanternLatched)
                    {
                        problems.Add("The lantern latched before both rescues.");
                    }

                    break;
                case 2:
                    if (s.Phase != FloodPhase.LastCrossing)
                    {
                        problems.Add("Both passengers are home but the last crossing has not begun.");
                    }

                    if (!s.LanternLatched)
                    {
                        problems.Add("Both passengers are home but the lantern is not latched.");
                    }

                    if (s.LowRefuge == RefugeStatus.ClosurePending)
                    {
                        problems.Add("A refuge closure is still pending after both rescues.");
                    }

                    break;
            }

            if (s.LowRefugeStops < 0 || s.ExposureResets < 0)
            {
                problems.Add("Counters cannot be negative.");
            }

            return problems;
        }
    }
}
