using BepInEx;
using BepInEx.Configuration;
using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Karenia.GetTapped.Core;

namespace Karenia.GetTapped.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.com3d2";
        public const string projectName = "GetTapped.COM3D2";
        public const string version = "0.1.0";

        public Plugin()
        {
            Instance = this;
            Config = new PluginConfig()
            {
                DefaultRotationSensitivity = 0.01f,
                DefaultTranslationSensitivity = -0.01f
            };
            Config.BindConfig(base.Config);

            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            var harmony = new Harmony(id);
            Core = new PluginCore();

            harmony.PatchAll(typeof(Hook));
        }


        public static Plugin Instance { get; private set; }
        public new PluginConfig Config { get; set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
        public IGetTappedPlugin Core { get; private set; }

        public ConfigEntry<bool> PluginEnabled { get => Config.PluginEnabled; }
        public ConfigEntry<bool> SingleTapTranslate { get => Config.SingleTapTranslate; }
        public ConfigEntry<float> RotationSensitivity { get => Config.RotationSensitivity; }
        public ConfigEntry<float> TranslationSensitivity { get => Config.TranslationSensitivity; }
        public ConfigEntry<float> ZoomSensitivity { get => Config.ZoomSensitivity; }
    }

    public static class Hook
    {
        /// <summary>
        /// This method calculates the <b>rotation</b> update of the camera.
        /// </summary>
        /// <param name="___xVelocity"></param>
        /// <param name="___yVelocity"></param>
        /// <param name="___zoomVelocity"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UltimateOrbitCamera), "Update")]
        public static bool HookCameraRotation(
            UltimateOrbitCamera __instance,
            ref float ___xVelocity,
            ref float ___yVelocity,
            ref float ___zoomVelocity,
            ref Vector3 ___mVelocity)
        {
            var plugin = Plugin.Instance;
            if (!plugin.Config.PluginEnabled.Value) return true;

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value);

            if (__instance.mouseControl)
            {
                // flip x direction because it's like that
                ___xVelocity -= movement.ScreenSpaceRotation.x * plugin.RotationSensitivity.Value;
                ___yVelocity += movement.ScreenSpaceRotation.y * plugin.RotationSensitivity.Value;
                ___zoomVelocity += -Mathf.Log(movement.Zoom) * plugin.ZoomSensitivity.Value;

                var tranform = __instance.transform;
                ___mVelocity += (
                        tranform.right * movement.ScreenSpaceTranslation.x +
                        tranform.up * movement.ScreenSpaceTranslation.y
                    ) * plugin.TranslationSensitivity.Value;
            }

            return true;
        }
    }
}
