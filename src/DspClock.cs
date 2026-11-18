using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CalibrationFixer
{
    public static class DspClock
    {
        const long Window = 40_000_000;
        const long Margin = 20_000;
        const double MaxLead = 0.25;
        const double LagSmoothing = 1.0 / 400;
        const double MaxLagStep = 0.02;

        static readonly double ticksPerStopwatchTick = 1e7 / Stopwatch.Frequency;
        static readonly List<(long wall, long sample)> samples = new List<(long, long)>();
        static double lastRaw = double.NaN;
        static long lastWall;
        static int lastFrame = -1;
        static double now;
        static bool hasLag;

        public static long Offset { get; private set; }

        public static double Lag { get; private set; }

        public static double GetNow()
        {
            if (lastFrame != Time.frameCount)
            {
                lastFrame = Time.frameCount;
                now = Sample(AudioSettings.dspTime, (long)(Stopwatch.GetTimestamp() * ticksPerStopwatchTick));
            }
            return now;
        }

        public static double Sample(double raw, long wall)
        {
            if (raw != lastRaw)
            {
                long maxDelay = double.IsNaN(lastRaw) ? long.MaxValue : Math.Max(0, Math.Min(wall - lastWall, (long)((raw - lastRaw) * 1e7)));
                lastRaw = raw;
                Observe(wall, (long)(raw * 1e7) - wall, maxDelay);
            }
            lastWall = wall;
            return Math.Min((wall + Offset) / 1e7, raw + MaxLead);
        }

        public static void Observe(long wall, long sample, long maxDelay)
        {
            long upper = maxDelay == long.MaxValue ? long.MaxValue : sample + maxDelay + Margin;
            samples.RemoveAll(s => s.wall < wall - Window || s.sample > upper);
            samples.Add((wall, sample));
            long max = long.MinValue;
            foreach (var s in samples)
                if (s.sample > max) max = s.sample;
            Offset = max;
        }

        public static void ObserveLag(double sample)
        {
            if (!hasLag) Lag = sample;
            else Lag += Math.Max(-MaxLagStep, Math.Min(MaxLagStep, sample - Lag)) * LagSmoothing;
            hasLag = true;
        }
    }
}
