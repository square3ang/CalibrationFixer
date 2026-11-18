using HarmonyLib;
using UnityModManagerNet;

namespace CalibrationFixer
{
    public static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Logger { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            var harmony = new Harmony(modEntry.Info.Id);
            modEntry.OnToggle = (_, enabled) =>
            {
                if (enabled) harmony.PatchAll(typeof(Main).Assembly);
                else harmony.UnpatchAll(harmony.Id);
                return true;
            };
            return true;
        }
    }
}
