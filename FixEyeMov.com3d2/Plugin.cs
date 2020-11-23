using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Karenia.FixEyeMov.Core;
using BepInEx;

namespace Karenia.FixEyeMov.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixeyemov.com3d2";
        public const string projectName = "FixationalEyeMovements.COM3D2";
        public const string version = "0.1.1";

        public Plugin()
        {
            Instance = this;
            EyeConfig = new EyeMovementConfig();
            EyeConfig.Bind(base.Config);

            Logger = BepInEx.Logging.Logger.CreateLogSource("FixEyeMov");
            var harmony = new Harmony(id);

            harmony.PatchAll(typeof(EyeMovementHook));
        }

        public static Plugin Instance { get; private set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
    }


    public static class EyeMovementHook
    {
        private static readonly Dictionary<TBody, EyeMovementState> eyeInfoRepo = new Dictionary<TBody, EyeMovementState>();

        private static string FormatMaidName(Maid maid) => $"{maid?.status?.firstName} {maid?.status?.lastName}";

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static void FixationalEyeMovement(TBody __instance, Quaternion ___quaDefEyeL, Quaternion ___quaDefEyeR, float ___m_editYorime, Vector3 ___EyeEulerAngle)
        {
            // Men have no eyes so nothing to see
            if (__instance.boMAN) return;
            if (!Plugin.Instance.EyeConfig.Enabled.Value) return;

            // abort when instance is not initialized
            if (__instance.trsEyeL == null || __instance.trsEyeR == null) return;

            // abort when instance is not registered
            if (!eyeInfoRepo.TryGetValue(__instance, out var info)) return;

            var delta = info.Tick(Time.deltaTime);

            var eulerAngles = new Vector3(delta.x, 0, delta.y);

            // Revert left and right eye angle to raw state
            var revertQuatL = Quaternion.Euler(0f, -___EyeEulerAngle.x * 0.2f + ___m_editYorime, -___EyeEulerAngle.z * 0.1f);
            var revertQuatR = Quaternion.Euler(0f, ___EyeEulerAngle.x * 0.2f + ___m_editYorime, ___EyeEulerAngle.z * 0.1f);

            __instance.trsEyeL.localRotation *= Quaternion.Inverse(revertQuatL);
            __instance.trsEyeR.localRotation *= Quaternion.Inverse(revertQuatR);

            // Add original euler angle to movement
            eulerAngles += ___EyeEulerAngle;

            // Recalculate rotation
            __instance.trsEyeL.localRotation *= Quaternion.Euler(0f, -eulerAngles.x * 0.2f + ___m_editYorime, -eulerAngles.z * 0.1f);
            __instance.trsEyeR.localRotation *= Quaternion.Euler(0f, eulerAngles.x * 0.2f + ___m_editYorime, eulerAngles.z * 0.1f);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "Initialize")]
        public static void InitEyeInfo(Maid __instance)
        {
            if (__instance.boMAN) return;
            eyeInfoRepo.Add(__instance.body0, new EyeMovementState(Plugin.Instance.EyeConfig));
            Plugin.Instance.Logger.LogInfo($"Initialized eye info at {FormatMaidName(__instance)}");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "OnDestroy")]
        public static void UnInitEyeInfo(TBody __instance)
        {
            eyeInfoRepo.Remove(__instance);
            Plugin.Instance.Logger.LogInfo($"Uninitialized eye info at {FormatMaidName(__instance.maid)}");
        }

        // obsolete method
        //[HarmonyTranspiler, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        //public static IEnumerable<CodeInstruction> AddFixationalEyeMovement(IEnumerable<CodeInstruction> instructions)
        //{
        //    if (patched) return instructions;
        //    patched = true;

        //    AssertNotNull(instructions, nameof(instructions));

        //    var instList = instructions.ToList();
        //    var targetField_trsEyeL = AccessTools.Field(typeof(TBody), nameof(TBody.trsEyeL));
        //    var targetField_quaDefEyeL = AccessTools.Field(typeof(TBody), "quaDefEyeL");

        //    AssertNotNull(targetField_trsEyeL, nameof(targetField_trsEyeL));
        //    AssertNotNull(targetField_quaDefEyeL, nameof(targetField_quaDefEyeL));
        //    AssertNotNull(instList, nameof(instList));

        //    int targetInstruction = -1;

        //    for (int i = 1; i < instList.Count - 5; i++)
        //    {
        //        if (instList[i].IsLdarg(0)
        //            && instList[i + 1].LoadsField(targetField_trsEyeL)
        //            && instList[i + 2].IsLdarg(0)
        //            && instList[i + 3].LoadsField(targetField_quaDefEyeL)
        //            && instList[i + 4].Is(OpCodes.Ldc_R4, (float)0)
        //            && instList[i + 5].IsLdarg(0))
        //        {
        //            targetInstruction = i;
        //            break;
        //        }
        //    }

        //    if (targetInstruction > 0)
        //    {
        //        var eulerAngleField = AccessTools.Field(typeof(TBody), "EyeEulerAngle");
        //        var hookFunction = AccessTools.Method(typeof(EyeMovementHook), nameof(FixationalEyeMovementPatch2));

        //        AssertNotNull(eulerAngleField, nameof(eulerAngleField));
        //        AssertNotNull(hookFunction, nameof(hookFunction));
        //        AssertNotNull(targetInstruction, nameof(targetInstruction));

        //        var labels = instList[targetInstruction].labels;
        //        instList[targetInstruction].labels = new List<Label>();

        //        instList.InsertRange(targetInstruction, new CodeInstruction[]
        //        {
        //            new CodeInstruction(OpCodes.Ldarg_0) {labels = labels},
        //            new CodeInstruction(OpCodes.Ldarg_0) { },
        //            new CodeInstruction(OpCodes.Ldflda, eulerAngleField),
        //            new CodeInstruction(OpCodes.Call, hookFunction)
        //        });

        //        Plugin.Instance.Logger.LogInfo($"Patched at TBody#MoveHeadAndEye+{targetInstruction}");
        //    }
        //    else
        //    {
        //        Plugin.Instance.Logger.LogInfo("For some reason patching failed");
        //    }

        //    return instList;
        //}

        private static void AssertNotNull(object o, string name)
        {
            if (o == null)
            {
                string message = $"Unexpected: {name} is null!";
                Plugin.Instance.Logger.LogFatal(message);
                throw new Exception(message);
            }
        }
    }

}
