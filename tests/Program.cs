using System;
using System.Collections.Generic;
using System.Linq;
using CalibrationFixer;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

var rng = new Random(1);

(double vanilla, double withMod) Run(double fps, int bufferSize, int frames, int measureFrom, int stallAt = -1, double stallSeconds = 0)
{
    DspClock.Reset();
    double blockSeconds = bufferSize / 48000.0;
    const double dspMinusWall = 100;
    double now = 1000, prevNow = now, stallStart = double.NaN, gameDsp = 0, lastReported = double.NaN, errorSum = 0;
    int errorCount = 0;
    for (int f = 0; f < frames; f++)
    {
        now += 1 / fps * (0.8 + 0.4 * rng.NextDouble());
        if (f == stallAt) stallStart = now;
        double lost = double.IsNaN(stallStart) ? 0 : Math.Clamp(now - stallStart, 0, stallSeconds);
        double trueDsp = now + dspMinusWall - lost;
        double raw = Math.Floor(trueDsp / blockSeconds) * blockSeconds;
        if (raw != lastReported) gameDsp = lastReported = raw;
        else gameDsp += now - prevNow;
        prevNow = now;
        DspClock.ObserveLag(gameDsp - DspClock.Sample(raw, (long)(now * 1e7)));
        if (f >= measureFrom) { errorSum += gameDsp - trueDsp; errorCount++; }
    }
    double gameClockError = errorSum / errorCount;
    return (gameClockError * 1000, (gameClockError - DspClock.Lag) * 1000);
}

var first = Run(60, 1024, 600, 300);
Check(Math.Abs(first.withMod) < 2, $"converged: {first.withMod:0.00} ms");

double before = DspClock.Lag;
DspClock.ObserveLag(before + 1);
Check(Math.Abs(DspClock.Lag - before) < 0.0001, $"one outlier moved the lag by {(DspClock.Lag - before) * 1000:0.000} ms");

foreach (var bufferSize in new[] { 256, 1024, 2048 })
{
    var vanilla = new List<double>();
    var withMod = new List<double>();
    foreach (var fps in new[] { 30.0, 60.0, 144.0, 240.0, 1000.0 })
    {
        var (v, m) = Run(fps, bufferSize, 4000, 2000);
        vanilla.Add(v);
        withMod.Add(m);
    }
    double vanillaSpread = vanilla.Max() - vanilla.Min(), modSpread = withMod.Max() - withMod.Min();
    Console.WriteLine($"buffer {bufferSize}: audio vs game clock at 30/60/144/240/1000 fps (ms)");
    Console.WriteLine($"  vanilla: {string.Join(", ", vanilla.Select(x => x.ToString("0.00")))}  spread {vanillaSpread:0.00}");
    Console.WriteLine($"  mod:     {string.Join(", ", withMod.Select(x => x.ToString("0.00")))}  spread {modSpread:0.00}");
    Check(modSpread < 1.5, $"buffer {bufferSize}: audio timing still depends on frame rate ({modSpread:0.00} ms)");
    Check(modSpread < vanillaSpread / 3, $"buffer {bufferSize}: no real improvement over vanilla");
}

foreach (var stallMs in new[] { 5.0, 20.0, 63.0, 300.0 })
{
    var (_, m) = Run(400, 1024, 4400, 4200, stallAt: 4000, stallSeconds: stallMs / 1000);
    Console.WriteLine($"audio stall {stallMs} ms on restart, song scheduled 1 s later: off by {m:0.00} ms");
    Check(Math.Abs(m) < 2, $"audio stall {stallMs} ms: song off by {m:0.00} ms");
}

// Fresh start (mod just turned on, then calibration): lag must settle within 3 s even at 30 FPS
var (_, warm) = Run(30, 2048, 90, 30);
Check(Math.Abs(warm) < 3, $"lag after 3 s at 30 FPS off by {warm:0.00} ms");

// Calibration presses: held repeats and presses before the start grace are dropped
var presses = new PressFilter<string>();
presses.Add("z", true, 100, collecting: true, ignoreBefore: 100);
presses.Add("z", false, 150, collecting: true, ignoreBefore: 100);
presses.Add("x", true, 200, collecting: true, ignoreBefore: 100);
presses.Add("x", true, 210, collecting: true, ignoreBefore: 100);
presses.Add("z", true, 220, collecting: false, ignoreBefore: 100);
Check(string.Join(",", presses.Pending) == "200", $"press filter queued [{string.Join(",", presses.Pending)}], want [200]");

// First-frame outlier and NaN must not poison Lag
DspClock.Reset();
DspClock.ObserveLag(double.NaN);
Check(DspClock.Lag == 0, $"NaN changed Lag to {DspClock.Lag}");
DspClock.ObserveLag(0.5);
Check(Math.Abs(DspClock.Lag) <= 0.02 + 1e-9, $"first-frame outlier not clamped: {DspClock.Lag * 1000:0.00} ms");

// Audio device reset (raw dspTime jumps backward to 0)
DspClock.Reset();
DspClock.Sample(100.0, 1_000_000_000);
double afterReset = DspClock.Sample(0.02, 1_000_200_000);
Check(Math.Abs(afterReset - 0.02) < 0.002, $"backward dspTime reset off: {afterReset:0.000}");

// Pause/unpause (AudioListener.pause = true for 2 s, inside the 4 s window: Sample runs, ObserveLag skipped)
DspClock.Reset();
double pNow = 1000, pDsp = 100.0;
const double pBlock = 1024 / 48000.0;
for (int i = 0; i < 60; i++)
{
    pNow += 1.0 / 60;
    pDsp += 1.0 / 60;
    DspClock.Sample(Math.Floor(pDsp / pBlock) * pBlock, (long)(pNow * 1e7));
}
double frozenRaw = Math.Floor(pDsp / pBlock) * pBlock;
for (int i = 0; i < 120; i++)
{
    pNow += 1.0 / 60;
    DspClock.Sample(frozenRaw, (long)(pNow * 1e7));
}
double afterPause = DspClock.Sample(frozenRaw + pBlock, (long)((pNow + 1.0 / 60) * 1e7));
Check(Math.Abs(afterPause - (frozenRaw + pBlock)) < 0.002, $"2 s pause/unpause off by {(afterPause - (frozenRaw + pBlock)) * 1000:0.00} ms");

Console.WriteLine("ok");
