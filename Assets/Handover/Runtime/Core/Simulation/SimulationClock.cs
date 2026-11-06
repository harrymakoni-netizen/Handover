namespace Handover.Core
{
    /// <summary>
    /// The authoritative clock. Real time always advances (UI hands keep tracking);
    /// gameplay time advances only while the session runs, so pause and recovery
    /// freeze dwell, hop, weather and exposure timers together.
    /// </summary>
    public sealed class SimulationClock
    {
        /// <summary>Upper bound on one step so a hitch cannot teleport a hop or an exposure timer.</summary>
        public const float MaxStep = 0.1f;

        public double RealTime { get; private set; }
        public double GameplayTime { get; private set; }
        public float RealDelta { get; private set; }
        public float GameplayDelta { get; private set; }
        public long StepCount { get; private set; }

        public void Advance(float realDelta, bool gameplayRunning)
        {
            if (realDelta < 0f || float.IsNaN(realDelta))
            {
                realDelta = 0f;
            }

            if (realDelta > MaxStep)
            {
                realDelta = MaxStep;
            }

            RealDelta = realDelta;
            RealTime += realDelta;
            GameplayDelta = gameplayRunning ? realDelta : 0f;
            GameplayTime += GameplayDelta;
            StepCount++;
        }

        /// <summary>Aligns real time with an external monotonic clock (the provider's sample clock).</summary>
        public void SyncRealTime(double realTime)
        {
            if (realTime > RealTime)
            {
                RealTime = realTime;
            }
        }
    }
}
