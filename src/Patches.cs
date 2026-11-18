using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SkyHook;
using UnityEngine;

namespace CalibrationFixer
{
    [HarmonyPatch(typeof(scrConductor), "Update")]
    static class ConductorLagPatch
    {
        public static bool seedAsyncOffset;

        static void Prefix() => DspClock.GetNow();

        static void Postfix(scrConductor __instance)
        {
            if (seedAsyncOffset && AsyncInputManager.isActive)
            {
                AsyncInputUtils.UpdateOffsetTime(1L);
                seedAsyncOffset = false;
            }
            if (AudioListener.pause || !Application.isFocused) return;
            DspClock.ObserveLag(__instance.dspTime - DspClock.GetNow());
        }
    }

    [HarmonyPatch]
    static class AudioSchedulePatch
    {
        static double shift;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(scrConductor), "StartMusicCo"));
            yield return AccessTools.Method(typeof(scrConductor), nameof(scrConductor.ScrubMusicToTime));
            yield return AccessTools.Method(typeof(scrConductor), nameof(scrConductor.PlayWithEndTime));
            yield return AccessTools.Method(typeof(AudioManager), "_Play");
            yield return AccessTools.Method(typeof(ffxPlaySound), nameof(ffxPlaySound.StartEffect));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var map = new Dictionary<MethodInfo, MethodInfo>
            {
                [AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayScheduled))] = AccessTools.Method(typeof(AudioSchedulePatch), nameof(PlayScheduled)),
                [AccessTools.Method(typeof(AudioSource), nameof(AudioSource.SetScheduledStartTime))] = AccessTools.Method(typeof(AudioSchedulePatch), nameof(SetScheduledStartTime)),
                [AccessTools.Method(typeof(AudioSource), nameof(AudioSource.SetScheduledEndTime))] = AccessTools.Method(typeof(AudioSchedulePatch), nameof(SetScheduledEndTime)),
            };
            foreach (var ci in code)
            {
                if (ci.operand is MethodInfo m && map.TryGetValue(m, out var replacement))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                }
                yield return ci;
            }
        }

        static void FreezeIfSong(AudioSource source)
        {
            if (!source.TryGetComponent(out scrConductor conductor) || source != conductor.song) return;
            shift = DspClock.Lag;
            Main.Logger?.Log($"Song scheduled, audio shifted by {-shift * 1000:0.00} ms");
        }

        static void PlayScheduled(AudioSource source, double time)
        {
            FreezeIfSong(source);
            source.PlayScheduled(time - shift);
        }

        static void SetScheduledStartTime(AudioSource source, double time)
        {
            FreezeIfSong(source);
            source.SetScheduledStartTime(time - shift);
        }

        static void SetScheduledEndTime(AudioSource source, double time) => source.SetScheduledEndTime(time - shift);
    }

    [HarmonyPatch(typeof(scnCalibration))]
    static class CalibrationPatches
    {
        const int InputNone = 0, InputKeyboard = 1;
        const long StartPressGrace = 200_000;

        static readonly AccessTools.FieldRef<scnCalibration, int> messageNumber =
            AccessTools.FieldRefAccess<scnCalibration, int>("currentMessageNumber");

        static readonly HashSet<AsyncKeyCode> held = new HashSet<AsyncKeyCode>();
        static readonly Queue<long> pending = new Queue<long>();
        static readonly List<SkyHookEvent> events = new List<SkyHookEvent>();
        static long ignoreBefore;
        static long pressTick;
        static bool asyncSeen;

        static bool AsyncOn => Persistence.GetChosenAsynchronousInput() && AsyncInputManager.isActive;

        [HarmonyPostfix, HarmonyPatch("Start")]
        static void StartPostfix()
        {
            if (Persistence.GetChosenAsynchronousInput())
            {
                if (!AsyncInputManager.isActive) AsyncInputManager.ToggleHook(true);
                ConductorLagPatch.seedAsyncOffset = true;
            }
            AsyncInputManager.ClearKeys();
            held.Clear();
            pending.Clear();
            pressTick = 0;
            asyncSeen = false;
        }

        [HarmonyPrefix, HarmonyPatch("Update")]
        static void UpdatePrefix(scnCalibration __instance)
        {
            if (!AsyncOn) return;
            bool collecting = messageNumber(__instance) == 1 && __instance.keyCooldown <= 0f && Application.isFocused;
            if (messageNumber(__instance) != 1) ignoreBefore = DateTime.Now.Ticks + StartPressGrace;

            events.Clear();
            while (AsyncInputManager.keyQueue.TryDequeue(out var e)) events.Add(e);
            if (events.Count > 0) asyncSeen = true;
            events.Sort((a, b) => a.GetTimeInTicks().CompareTo(b.GetTimeInTicks()));
            foreach (var e in events)
            {
                var key = new AsyncKeyCode(e.Key, e.Label);
                if (e.Type != SkyHook.EventType.KeyPressed) held.Remove(key);
                else if (held.Add(key) && collecting && e.GetTimeInTicks() > ignoreBefore) pending.Enqueue(e.GetTimeInTicks());
            }
            if (!collecting) pending.Clear();
        }

        [HarmonyTranspiler, HarmonyPatch("Update")]
        static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> code)
        {
            var getInput = AccessTools.Method(typeof(scnCalibration), "GetInputTypeForDown");
            foreach (var ci in code)
            {
                yield return ci;
                if (ci.Calls(getInput))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return CodeInstruction.Call(typeof(CalibrationPatches), nameof(FilterInput));
                }
            }
        }

        static int FilterInput(int inputType, scnCalibration calibration)
        {
            if (!AsyncOn || messageNumber(calibration) != 1) return inputType;
            if (!asyncSeen)
            {
                if (inputType == InputKeyboard) ignoreBefore = DateTime.Now.Ticks + StartPressGrace;
                return inputType;
            }
            if (inputType == InputKeyboard) inputType = InputNone;
            if (inputType == InputNone && pending.Count > 0)
            {
                pressTick = pending.Dequeue();
                return InputKeyboard;
            }
            return inputType;
        }

        [HarmonyPrefix, HarmonyPatch("GetOffset")]
        static void GetOffsetPrefix(scnCalibration __instance, ref double angleRad)
        {
            if (pressTick == 0) return;
            var conductor = __instance.conductor;
            double pos = AsyncInputUtils.GetSongPosition(conductor, pressTick - AsyncInputManager.offsetTick);
            angleRad = Mathf.PI / 2f + (pos + scrConductor.calibration_i * conductor.song.pitch) / conductor.crotchetAtStart * Mathf.PI;
            pressTick = 0;
        }
    }
}
