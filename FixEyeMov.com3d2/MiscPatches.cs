using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;

namespace Karenia.FixEyeMov.Com3d2
{
    /// <summary>
    /// Miscellaneous patches that smooth out compatibility stuff.
    /// </summary>
    public static class MiscPatches
    {
        /// <summary>
        /// Apply all compatibility patches.
        /// </summary>
        /// <param name="gameVersion">
        /// The game's version, in format like <c>XYYZ</c>, which corresponds to game version <c>X.YY.Z</c>.
        /// </param>
        /// <param name="harmony">The Harmony patcher instance.</param>
        /// <param name="logger">BepInEx's logger instance</param>
        public static void ApplyPatches(
            int gameVersion,
            HarmonyLib.Harmony harmony,
            BepInEx.Logging.ManualLogSource logger)
        {
            var getSlotLoadedMethod = AccessTools.Method(typeof(TBody), "GetSlotLoaded");
            if (gameVersion < 1560)
            {
                harmony.Patch(getSlotLoadedMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(MiscPatches), nameof(LoadSlotBoundsCheck))));
            }
        }

        /// <summary>
        /// Adds bounds check for <c>TBody.GetSlotLoaded</c>. Prevents the following exception:
        ///
        /// <code>
        /// ArgumentOutOfRangeException: Argument is out of range.
        ///     Parameter name: index
        ///     at System.Collections.Generic.List`1<TBodySkin>.get_Item(int) <0x00083>
        ///     at TBody.GetSlotLoaded(TBody/SlotID) <0x00034>
        ///     at TBody.GetSlotVisible(TBody/SlotID) <0x0001c>
        ///     at Karenia.FixEyeMov.Com3d2.Poi.PoiHook.RecalculateSceneTargets(single, single, single, single, single, bool, bool) <0x005f1>
        ///     at Karenia.FixEyeMov.Com3d2.Poi.PoiHook.RegenerateTargetsInKagScene() <0x00190>
        ///     at(wrapper dynamic-method) TBody.DMD<TBody..UnInit>(TBody) <0x00356>
        ///     at Maid.Uninit() <0x0009f>
        ///     at(wrapper dynamic-method) CharacterMgr.DMD<CharacterMgr..Deactivate>(CharacterMgr, int, bool) <0x00244>
        ///     at CharacterMgr.DeactivateMaid(int) <0x00027>
        ///     at FreeModeInit.OnCall() <0x00772>
        ///     at WfScreenChildren.Call() <0x0012b>
        ///     at WfScreenManager.RunScreen(string) <0x00148>
        ///     at WfScreenManager.Update() <0x0014c>
        /// </code>
        /// </summary>
        /// <param name="__result"></param>
        /// <param name="f_eSlot"></param>
        /// <param name="__instance"></param>
        /// <returns></returns>
        private static bool LoadSlotBoundsCheck(ref bool __result, TBody.SlotID f_eSlot, TBody __instance)
        {
            if (__instance.goSlot.Count >= (int)f_eSlot) { return true; }
            else { __result = false; return false; }
        }
    }
}
