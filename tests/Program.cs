using System;
using System.Linq;
using CalibrationFixer;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

const long trueOffset = 5_000_000, block = 213_333, frame = 166_667;
long wall = 0, lastBlock = -1;
for (int i = 0; i < 600; i++, wall += frame)
{
    long dsp = (wall + trueOffset) / block * block;
    if (dsp != lastBlock) { lastBlock = dsp; DspClock.Observe(wall, dsp - wall); }
}
Check(trueOffset - DspClock.Offset is >= 0 and < 20_000, $"converged: {DspClock.Offset}");

DspClock.Observe(wall, -30_000_000);
Check(DspClock.Offset == -30_000_000, "reset");

var rng = new Random(1);
for (int i = 0; i < 2000; i++) DspClock.ObserveLag(-0.008 + (rng.NextDouble() - 0.5) * 0.016);
Check(Math.Abs(DspClock.Lag + 0.008) < 0.002, $"lag: {DspClock.Lag}");
DspClock.ObserveLag(0.5);
Check(DspClock.Lag == 0.5, "lag reset");

foreach (var bufferSize in new[] { 256, 1024, 2048 })
{
    double blockSeconds = bufferSize / 48000.0;
    var vanilla = new System.Collections.Generic.List<double>();
    var withMod = new System.Collections.Generic.List<double>();
    foreach (var fps in new[] { 30.0, 60.0, 144.0, 240.0, 1000.0 })
    {
        double dspMinusWall = 100 + vanilla.Count * 10 + bufferSize;
        double now = 1000, prevNow = now, gameDsp = 0, lastReported = double.NaN, errorSum = 0;
        int errorCount = 0;
        for (int f = 0; f < 4000; f++)
        {
            now += 1 / fps * (0.8 + 0.4 * rng.NextDouble());
            double trueDsp = now + dspMinusWall;
            double raw = Math.Floor(trueDsp / blockSeconds) * blockSeconds;
            if (raw != lastReported) gameDsp = lastReported = raw;
            else gameDsp += now - prevNow;
            prevNow = now;
            DspClock.ObserveLag(gameDsp - DspClock.Sample(raw, (long)(now * 1e7)));
            if (f >= 2000) { errorSum += gameDsp - trueDsp; errorCount++; }
        }
        double gameClockError = errorSum / errorCount;
        vanilla.Add(gameClockError * 1000);
        withMod.Add((gameClockError - DspClock.Lag) * 1000);
    }
    double vanillaSpread = vanilla.Max() - vanilla.Min(), modSpread = withMod.Max() - withMod.Min();
    Console.WriteLine($"buffer {bufferSize}: audio vs game clock at 30/60/144/240/1000 fps (ms)");
    Console.WriteLine($"  vanilla: {string.Join(", ", vanilla.Select(x => x.ToString("0.00")))}  spread {vanillaSpread:0.00}");
    Console.WriteLine($"  mod:     {string.Join(", ", withMod.Select(x => x.ToString("0.00")))}  spread {modSpread:0.00}");
    Check(modSpread < 1.5, $"buffer {bufferSize}: audio timing still depends on frame rate ({modSpread:0.00} ms)");
    Check(modSpread < vanillaSpread / 3, $"buffer {bufferSize}: no real improvement over vanilla");
}

Console.WriteLine("ok");
