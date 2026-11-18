# CalibrationFixer

A Unity Mod Manager mod for **A Dance of Fire and Ice** that makes song timing independent of your frame rate and lets the calibration screen use async input.

> [!IMPORTANT]
> Recalibrate after turning the mod on or off. The audio moves by a few milliseconds, so your old input offset no longer fits.

## Features

| | Vanilla | With CalibrationFixer |
|---|---|---|
| Song and hit sounds vs. the game clock | Off by 0.5–15 ms, depending on FPS and audio buffer | Within ~1 ms at any FPS |
| Judgment code | — | Untouched |
| Calibration keyboard input | Frame-based (`Input.anyKeyDown`) | Async hook timestamp, same mapping as in-game async judgment |
| Calibration when the async hook gets no keys | — | Falls back to normal input |

## How it works

The game's clock (`scrConductor.dspTime`) follows `AudioSettings.dspTime`, which only moves once per mixed audio block. Between blocks the game adds frame time, so its clock runs behind the real audio clock, and how far behind depends on the frame rate. Judgment and visuals use the game's clock, but songs are scheduled on the real audio clock.

```mermaid
flowchart LR
    A[AudioSettings.dspTime<br/>steps once per audio block] --> B[Game clock<br/>steps + frame time]
    B --> J[Judgment & visuals]
    A --> S[Vanilla: song scheduled<br/>on the audio clock]
    B -. lag depends on FPS .-> S
    A --> C[DspClock<br/>smooth audio clock]
    B --> L[Average lag<br/>game clock − DspClock]
    C --> L
    L --> M[Mod: song & sounds<br/>shifted by the lag]
```

<details>
<summary>Details</summary>

- **`DspClock.Observe`** keeps the tightest offset between the audio clock and `Stopwatch` over the last 4 seconds. A block is always seen after it was mixed, so the largest offset is the closest to the truth.
- **`DspClock.ObserveLag`** averages *game clock − smooth clock* every frame.
- When the song is scheduled, that lag is frozen. The song, hit sounds, countdown ticks, hold sounds and `PlaySound` events are all moved by it. So they play exactly when the game's clock reaches their time.
- On the calibration screen, presses come from the SkyHook queue with their own timestamps. They are turned into song positions the same way a level does (`AsyncInputUtils.GetSongPosition`).

</details>

## Test results

`tests/` simulates the vanilla clock (block-stepped `dspTime` plus frame time) at different frame rates and audio buffer sizes. The numbers show how far the audio is from the game's clock, in milliseconds.

| Buffer | | 30 FPS | 60 FPS | 144 FPS | 240 FPS | 1000 FPS | Spread |
|---|---|---:|---:|---:|---:|---:|---:|
| 256 | Vanilla | −2.63 | −2.66 | −2.65 | −1.78 | −0.49 | 2.17 |
| | **Mod** | −0.05 | −0.02 | −0.03 | 0.01 | 0.00 | **0.06** |
| 1024 | Vanilla | −10.63 | −7.20 | −3.41 | −1.98 | −0.49 | 10.14 |
| | **Mod** | −0.11 | 0.01 | 0.23 | −0.10 | 0.02 | **0.34** |
| 2048 | Vanilla | −14.43 | −7.70 | −3.29 | −2.00 | −0.51 | 13.92 |
| | **Mod** | −0.94 | 0.11 | 0.26 | 0.10 | −0.11 | **1.19** |

## Install

1. Install Unity Mod Manager for the game.
2. Download `CalibrationFixer.zip` from [Releases](https://github.com/square3ang/CalibrationFixer/releases/latest) and install it with UMM, or extract it into the game's `Mods/` folder.
3. Start the game and recalibrate.

> [!TIP]
> To use async input on the calibration screen, turn on **Asynchronous Input** in the game settings first.

## Build

Building needs the game installed. Set `GameRootDir` if your game is not in the default Steam folder on Linux.

```sh
dotnet build -c Release                     # builds and copies into Mods/CalibrationFixer/
dotnet build -c Release -p:GameRootDir="/path/to/A Dance of Fire and Ice/"
dotnet run --project tests                  # simulation tests
```

The UMM log shows `Song scheduled, audio shifted by X ms` each time a song starts. Use it to check the mod on your setup.

## Limitations

> [!WARNING]
> - With very low FPS and a large audio buffer (30 FPS, 2048 samples) about 1 ms of difference is left.
> - After a big FPS change, the lag average needs a few seconds to settle.
> - Async calibration needs the SkyHook keyboard hook (X11/XWayland on Linux). Without it, the calibration screen uses normal input.
> - Judgment itself is vanilla. The game's per-frame clock jitter and its async offset at the start of each attempt are not changed.

## License

[MIT](LICENSE)
