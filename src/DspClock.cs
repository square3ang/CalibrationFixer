using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CalibrationFixer
{
    public static class DspClock
    {
        const long Window = 40_000_000;
        const long JumpThreshold = 1_000_000;
        const double MaxLead = 0.25;
        const double LagSmoothing = 1.0 / 400;

        static readonly double ticksPerStopwatchTick = 1e7 / Stopwatch.Frequency;
        static readonly Queue<(long wall, long sample)> samples = new Queue<(long, long)>();
        static double lastRaw = double.NaN;
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
                lastRaw = raw;
                Observe(wall, (long)(raw * 1e7) - wall);
            }
            return Math.Min((wall + Offset) / 1e7, raw + MaxLead);
        }

        public static void Observe(long wall, long sample)
        {
            if (samples.Count > 0 && Math.Abs(sample - Offset) > JumpThreshold) samples.Clear();
            samples.Enqueue((wall, sample));
            while (samples.Peek().wall < wall - Window) samples.Dequeue();
            long max = long.MinValue;
            foreach (var s in samples)
                if (s.sample > max) max = s.sample;
            Offset = max;
        }

        public static void ObserveLag(double sample)
        {
            if (!hasLag || Math.Abs(sample - Lag) > JumpThreshold / 1e7) Lag = sample;
            else Lag += (sample - Lag) * LagSmoothing;
            hasLag = true;
        }
    }
}
