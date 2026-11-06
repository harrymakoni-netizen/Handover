using System;
using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>Player settings. Assistance options are context, never a lower-tier award.</summary>
    public sealed class HandoverSettings
    {
        /// <summary>Wider alignment tolerances and slower hop anticipation.</summary>
        public bool SteadyMode;
        /// <summary>Keeps the low refuge open and removes weather penalties.</summary>
        public bool RelaxedMode;
        /// <summary>Larger characters, dock indicators and text.</summary>
        public bool LargeCues;
        public bool Captions = true;
        /// <summary>Removes optional splash bursts and exaggerated celebration movement.</summary>
        public bool ReducedMotion;
        /// <summary>An automatic canopy replaces the second hand's shield.</summary>
        public bool ShelterAssist;
        public float EffectsVolume = 0.8f;
        public float MusicVolume = 0.6f;

        public HandoverSettings Clone() => (HandoverSettings)MemberwiseClone();

        public bool AnyAssistance => SteadyMode || RelaxedMode || LargeCues || ShelterAssist;
    }

    /// <summary>Local results. No network account or analytics backend.</summary>
    public sealed class ProgressData
    {
        public bool ChapterCompleted;
        public int CompletedRuns;
        /// <summary>Fewest low-refuge stops in a completed run (Keeper's lantern stop excluded); -1 when none.</summary>
        public int BestLowRefugeStops = -1;
        /// <summary>The rescued group shown as a small harbour vignette when the app reopens.</summary>
        public List<DollId> HarbourVignette = new List<DollId>();

        public ProgressData Clone()
        {
            var copy = (ProgressData)MemberwiseClone();
            copy.HarbourVignette = new List<DollId>(HarbourVignette);
            return copy;
        }

        /// <summary>Records a completed run; returns true when it set a new best.</summary>
        public bool RecordCompletion(int lowRefugeStops)
        {
            ChapterCompleted = true;
            CompletedRuns++;
            HarbourVignette = new List<DollId>(Ids.Dolls);
            if (BestLowRefugeStops < 0 || lowRefugeStops < BestLowRefugeStops)
            {
                BestLowRefugeStops = lowRefugeStops;
                return true;
            }

            return false;
        }
    }

    public sealed class SaveData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public HandoverSettings Settings = new HandoverSettings();
        public ProgressData Progress = new ProgressData();
        /// <summary>Latest safe logical checkpoint of an unfinished chapter, or null.</summary>
        public Checkpoint Checkpoint;

        public SaveData Clone()
        {
            return new SaveData
            {
                SchemaVersion = SchemaVersion,
                Settings = Settings.Clone(),
                Progress = Progress.Clone(),
                Checkpoint = Checkpoint?.Clone(),
            };
        }
    }

    /// <summary>Versioned JSON mapping for <see cref="SaveData"/>. Unknown ids and illegal values are rejected.</summary>
    public static class SaveSerializer
    {
        public static string ToJson(SaveData data)
        {
            var root = new Dictionary<string, object>
            {
                ["schema"] = data.SchemaVersion,
                ["settings"] = SettingsToJson(data.Settings),
                ["progress"] = ProgressToJson(data.Progress),
                ["checkpoint"] = data.Checkpoint == null ? null : CheckpointToJson(data.Checkpoint),
            };
            return MiniJson.Serialize(root);
        }

        private static Dictionary<string, object> SettingsToJson(HandoverSettings s)
        {
            return new Dictionary<string, object>
            {
                ["steadyMode"] = s.SteadyMode,
                ["relaxedMode"] = s.RelaxedMode,
                ["largeCues"] = s.LargeCues,
                ["captions"] = s.Captions,
                ["reducedMotion"] = s.ReducedMotion,
                ["shelterAssist"] = s.ShelterAssist,
                ["effectsVolume"] = (double)s.EffectsVolume,
                ["musicVolume"] = (double)s.MusicVolume,
            };
        }

        private static Dictionary<string, object> ProgressToJson(ProgressData p)
        {
            var vignette = new List<object>();
            foreach (DollId d in p.HarbourVignette)
            {
                vignette.Add(d.Key());
            }

            return new Dictionary<string, object>
            {
                ["chapterCompleted"] = p.ChapterCompleted,
                ["completedRuns"] = p.CompletedRuns,
                ["bestLowRefugeStops"] = p.BestLowRefugeStops,
                ["harbourVignette"] = vignette,
            };
        }

        public static Dictionary<string, object> CheckpointToJson(Checkpoint c)
        {
            var placements = new List<object>();
            foreach (DollPlacement p in c.Placements)
            {
                placements.Add(new Dictionary<string, object>
                {
                    ["doll"] = p.Doll.Key(),
                    ["dock"] = p.Dock.Code(),
                    ["berth"] = p.Berth,
                });
            }

            var rescued = new List<object>();
            foreach (DollId d in Ids.Dolls)
            {
                if (c.State.IsRescued(d))
                {
                    rescued.Add(d.Key());
                }
            }

            return new Dictionary<string, object>
            {
                ["schema"] = c.SchemaVersion,
                ["config"] = c.ConfigVersion,
                ["label"] = c.Label ?? "",
                ["placements"] = placements,
                ["tutorialHopDone"] = c.State.TutorialHopDone,
                ["lanternEverLit"] = c.State.LanternEverLit,
                ["lanternLatched"] = c.State.LanternLatched,
                ["phase"] = c.State.Phase.ToString(),
                ["lowRefuge"] = c.State.LowRefuge.ToString(),
                ["rescued"] = rescued,
                ["lowRefugeStops"] = c.State.LowRefugeStops,
                ["exposureResets"] = c.State.ExposureResets,
                ["completed"] = c.State.Completed,
            };
        }

        public static bool TryFromJson(string json, out SaveData data, out string error)
        {
            data = null;
            if (!MiniJson.TryParse(json, out object root, out error))
            {
                error = "Unreadable save: " + error;
                return false;
            }

            try
            {
                var obj = AsObject(root, "save");
                int schema = GetInt(obj, "schema");
                if (schema != SaveData.CurrentSchemaVersion)
                {
                    error = $"Unsupported save schema {schema}.";
                    return false;
                }

                var result = new SaveData
                {
                    Settings = SettingsFromJson(AsObject(Get(obj, "settings"), "settings")),
                    Progress = ProgressFromJson(AsObject(Get(obj, "progress"), "progress")),
                };
                object cp = Get(obj, "checkpoint");
                result.Checkpoint = cp == null ? null : CheckpointFromJson(AsObject(cp, "checkpoint"));
                data = result;
                return true;
            }
            catch (FormatException e)
            {
                error = "Invalid save: " + e.Message;
                return false;
            }
        }

        private static HandoverSettings SettingsFromJson(Dictionary<string, object> o)
        {
            return new HandoverSettings
            {
                SteadyMode = GetBool(o, "steadyMode"),
                RelaxedMode = GetBool(o, "relaxedMode"),
                LargeCues = GetBool(o, "largeCues"),
                Captions = GetBool(o, "captions"),
                ReducedMotion = GetBool(o, "reducedMotion"),
                ShelterAssist = GetBool(o, "shelterAssist"),
                EffectsVolume = GetUnit(o, "effectsVolume"),
                MusicVolume = GetUnit(o, "musicVolume"),
            };
        }

        private static ProgressData ProgressFromJson(Dictionary<string, object> o)
        {
            var p = new ProgressData
            {
                ChapterCompleted = GetBool(o, "chapterCompleted"),
                CompletedRuns = GetInt(o, "completedRuns"),
                BestLowRefugeStops = GetInt(o, "bestLowRefugeStops"),
            };
            if (p.CompletedRuns < 0 || p.BestLowRefugeStops < -1 || (p.ChapterCompleted && p.BestLowRefugeStops < 0))
            {
                throw new FormatException("Illegal progress values.");
            }

            var seen = new HashSet<DollId>();
            foreach (object item in AsArray(Get(o, "harbourVignette"), "harbourVignette"))
            {
                DollId d = ParseDoll(item);
                if (!seen.Add(d))
                {
                    throw new FormatException($"Duplicate vignette passenger {d}.");
                }

                p.HarbourVignette.Add(d);
            }

            return p;
        }

        public static Checkpoint CheckpointFromJson(Dictionary<string, object> o)
        {
            var c = new Checkpoint
            {
                SchemaVersion = GetInt(o, "schema"),
                ConfigVersion = GetInt(o, "config"),
                Label = GetString(o, "label"),
            };

            var placements = new List<DollPlacement>();
            foreach (object item in AsArray(Get(o, "placements"), "placements"))
            {
                var p = AsObject(item, "placement");
                DollId doll = ParseDoll(Get(p, "doll"));
                if (!Ids.TryParseDock(GetString(p, "dock"), out DockId dock))
                {
                    throw new FormatException("Unknown dock id.");
                }

                placements.Add(new DollPlacement(doll, dock, GetInt(p, "berth")));
            }

            c.Placements = placements.ToArray();
            ChapterState s = c.State;
            s.TutorialHopDone = GetBool(o, "tutorialHopDone");
            s.LanternEverLit = GetBool(o, "lanternEverLit");
            s.LanternLatched = GetBool(o, "lanternLatched");
            s.Phase = ParseEnum<FloodPhase>(GetString(o, "phase"));
            s.LowRefuge = ParseEnum<RefugeStatus>(GetString(o, "lowRefuge"));
            foreach (object item in AsArray(Get(o, "rescued"), "rescued"))
            {
                if (!s.MarkRescued(ParseDoll(item)))
                {
                    throw new FormatException("Duplicate rescued passenger.");
                }
            }

            s.LowRefugeStops = GetInt(o, "lowRefugeStops");
            s.ExposureResets = GetInt(o, "exposureResets");
            s.Completed = GetBool(o, "completed");
            return c;
        }

        // ---- strict field access -------------------------------------------------

        private static object Get(Dictionary<string, object> o, string key)
        {
            if (!o.TryGetValue(key, out object v))
            {
                throw new FormatException($"Missing '{key}'.");
            }

            return v;
        }

        private static Dictionary<string, object> AsObject(object v, string what)
        {
            return v as Dictionary<string, object> ?? throw new FormatException($"'{what}' must be an object.");
        }

        private static List<object> AsArray(object v, string what)
        {
            return v as List<object> ?? throw new FormatException($"'{what}' must be an array.");
        }

        private static bool GetBool(Dictionary<string, object> o, string key)
        {
            return Get(o, key) is bool b ? b : throw new FormatException($"'{key}' must be true or false.");
        }

        private static int GetInt(Dictionary<string, object> o, string key)
        {
            if (Get(o, key) is double d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                return (int)d;
            }

            throw new FormatException($"'{key}' must be an integer.");
        }

        private static float GetUnit(Dictionary<string, object> o, string key)
        {
            if (Get(o, key) is double d && d >= 0.0 && d <= 1.0)
            {
                return (float)d;
            }

            throw new FormatException($"'{key}' must be between 0 and 1.");
        }

        private static string GetString(Dictionary<string, object> o, string key)
        {
            return Get(o, key) as string ?? throw new FormatException($"'{key}' must be a string.");
        }

        private static DollId ParseDoll(object v)
        {
            if (v is string s && Ids.TryParseDoll(s, out DollId d))
            {
                return d;
            }

            throw new FormatException("Unknown passenger id.");
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (Enum.TryParse(value, false, out T result) && Enum.IsDefined(typeof(T), result) && !int.TryParse(value, out _))
            {
                return result;
            }

            throw new FormatException($"Unknown {typeof(T).Name} '{value}'.");
        }
    }
}
