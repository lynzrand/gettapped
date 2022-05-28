using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Karenia.FixEyeMov.Core;
using BepInEx;
using System.Reflection;
using static Karenia.FixEyeMov.Com3d2.CharaExt;
using Karenia.FixEyeMov.Core.Poi;

namespace Karenia.FixEyeMov.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    [BepInProcess("COM3D2x64.exe")]
    [BepInDependency("org.bepinex.plugins.unityinjectorloader", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixeyemov.com3d2";
        public const string projectName = "FixationalEyeMovements.COM3D2";
        public const string version = "0.2.2";

        public Plugin()
        {
            Instance = this;
            EyeConfig = new EyeMovementConfig(base.Config, new EyeMovementDefaultConfig());
            PoiConfig = new PoiConfig(base.Config)
            {
                TargetWeightFactorFunction = (baseTransform, target, originalTarget) =>
                {
                    var dir = target - baseTransform.position;
                    var dist = dir.magnitude;
                    var distanceFactor = Mathf.Clamp(3 / dist, 0, 1);
                    var directionFactor = 1f;
                    if (originalTarget != null)
                    {
                        var originalDir = originalTarget - baseTransform.position;
                        directionFactor = Mathf.Cos(Vector3.Angle(dir, originalDir.Value) * Mathf.Deg2Rad);
                        var forward = baseTransform.up;
                        directionFactor *= Mathf.Cos(Vector3.Angle(dir, forward) * Mathf.Deg2Rad);
                        directionFactor *= 1.5f;
                    }
                    return distanceFactor * directionFactor;
                }
            };

            Logger = BepInEx.Logging.Logger.CreateLogSource("FixEyeMov");
            harmony = new Harmony(id);

            var gameVersion = Misc.GAME_VERSION;

            //Logger.LogDebug($"Game version is {gameVersion}. This is a debug message that might get useful in the future.");

            try
            {
                Logger.LogDebug("Patching misc items");
                MiscPatches.ApplyPatches(gameVersion, harmony, Logger);
            }
            catch (Exception e)
            {
                Logger.LogError("Error when applying misc patches");
                Logger.LogError(e);
            }

            try
            {
                Logger.LogDebug("Patching Eye Movements");
                harmony.PatchAll(typeof(EyeMovementHook));
            }
            catch (Exception e)
            {
                Logger.LogError("Error when patching EyeMovement");
                Logger.LogError(e);
            }

            try
            {
                Logger.LogDebug("Patching POI stuff");
                harmony.PatchAll(typeof(Poi.PoiHook));
                Poi.PoiHook.PatchKagSceneTags(harmony);
                Poi.PoiHook.PatchInitPoiInfo(harmony);
                Poi.PoiHook.Init();
            }
            catch (Exception e)
            {
                Logger.LogError("Error when patching POI");
                Logger.LogError(e);
            }

            try
            {
                Logger.LogDebug("Patching MaidVoicePitch");
                Poi.PoiHook.PatchMaidVoicePitch(harmony);
            }
            catch (Exception e)
            {
                Logger.LogError("Error when patching MaidVoicePitch");
                Logger.LogError(e);
            }
        }

        private Harmony harmony;

        public static Plugin? Instance { get; private set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public PoiConfig PoiConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
    }

    public class CharacterState
    {
        public CharacterState(EyeMovementConfig config, BepInEx.Logging.ManualLogSource? logger = null)
        {
            eyeMovement = new EyeMovementState(config, logger);
        }

        public EyeMovementState eyeMovement;
        public bool blinkedInLastFrame = false;
    }

    public static class CharaExt
    {
        public static string FormatMaidName(Maid maid) => $"{maid?.status?.firstName} {maid?.status?.lastName}";

        public static void AssertNotNull(object? o, string name)
        {
            if (o == null)
            {
                Plugin.Instance?.Logger.LogError($"{name} is null!");
            }
        }
    }

    public static class EyeMovementHook
    {
        private static readonly Dictionary<TBody, CharacterState> eyeInfoRepo = new Dictionary<TBody, CharacterState>();

        private static FieldInfo blinkParam = AccessTools.Field(typeof(Maid), "MabatakiVal");

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static void FixationalEyeMovement(TBody __instance, Quaternion ___quaDefEyeL, Quaternion ___quaDefEyeR, float ___m_editYorime, Vector3 ___EyeEulerAngle)
        {
            // Men have no eyes so nothing to see
            if (__instance.boMAN) return;
            if (Plugin.Instance == null || !Plugin.Instance.EyeConfig.Enabled.Value) return;

            // Abort when head and eye is locked
            if (__instance.boLockHeadAndEye) return;

            // abort when instance is not initialized
            if (__instance.trsEyeL == null || __instance.trsEyeR == null) return;

            // abort when instance is not registered
            if (!eyeInfoRepo.TryGetValue(__instance, out var info)) return;

            bool forceSaccade = false;
            if (__instance.maid != null)
            {
                float eyeCloseness = (float)blinkParam.GetValue(__instance.maid);
                var isBlinking = eyeCloseness > 0.8f;
                forceSaccade = isBlinking && !info.blinkedInLastFrame;
                info.blinkedInLastFrame = isBlinking;
            }

            var delta = info.eyeMovement.Tick(Time.deltaTime, forceSaccade: forceSaccade);

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
            if (Plugin.Instance == null) return;
            eyeInfoRepo.Add(__instance.body0, new CharacterState(Plugin.Instance.EyeConfig));
            Plugin.Instance?.Logger.LogInfo($"Initialized eye info at {FormatMaidName(__instance)}");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "OnDestroy")]
        public static void UnInitEyeInfo(TBody __instance)
        {
            eyeInfoRepo.Remove(__instance);
            Plugin.Instance?.Logger.LogInfo($"Uninitialized eye info at {FormatMaidName(__instance.maid)}");
        }
    }
}

