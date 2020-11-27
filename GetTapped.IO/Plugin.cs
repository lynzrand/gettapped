using BepInEx;
using BepInEx.Configuration;
using System;
using HarmonyLib;
using System.Drawing.Design;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using Karenia.GetTapped.Core;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.EventSystems;

namespace Karenia.GetTapped.IO
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.io";
        public const string projectName = "GetTapped.IO";
        public const string version = "0.1.0";

        public Plugin()
        {
            Config = new PluginConfig();
            Config.BindConfig(base.Config);
            Logger = BepInEx.Logging.Logger.CreateLogSource("LipSync");
            var harmony = new Harmony(id);
            harmony.PatchAll(typeof(Hook));
            Core = new PluginCore();
            Instance = this;
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
        private static bool IsPointerOverUI(Touch touch)
        {
            return EventSystem.current.IsPointerOverGameObject(touch.fingerId);
        }


        /// <summary>
        /// This method calculates the <b>rotation</b> update of the camera.
        /// </summary>
        /// <param name="___xVelocity"></param>
        /// <param name="___yVelocity"></param>
        /// <param name="___zoomVelocity"></param>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UltimateOrbitCamera), "LateUpdate")]
        public static bool HookCameraRotation(
            ref float ___xVelocity,
            ref float ___yVelocity,
            ref float ___zoomVelocity)
        {
            var plugin = Plugin.Instance;
            if (!plugin.Config.PluginEnabled.Value) return true;

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value, shouldBeUntracked: IsPointerOverUI);

            ___xVelocity += movement.ScreenSpaceRotation.x * plugin.RotationSensitivity.Value;
            ___yVelocity += movement.ScreenSpaceRotation.y * plugin.RotationSensitivity.Value;
            ___zoomVelocity += -Mathf.Log(movement.Zoom) * plugin.ZoomSensitivity.Value;

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SmoothCameraOrbit), "LateUpdate")]
        public static bool HookSmoothCameraOrbitRotation(
            ref float ___xDeg,
            ref float ___yDeg,
            ref float ___desiredDistance)
        {
            var plugin = Plugin.Instance;
            if (!plugin.Config.PluginEnabled.Value) return true;

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value, shouldBeUntracked: IsPointerOverUI);

            ___xDeg += movement.ScreenSpaceRotation.x * plugin.RotationSensitivity.Value;
            ___yDeg += movement.ScreenSpaceRotation.y * plugin.RotationSensitivity.Value;
            ___desiredDistance += -Mathf.Log(movement.Zoom) * plugin.ZoomSensitivity.Value;

            return true;
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(SmoothCamera), "eA")]
        public static bool HookFpvRotation(
            ref float ___hor,
            ref float ___ver)
        {
            var plugin = Plugin.Instance;
            if (!plugin.Config.PluginEnabled.Value) return true;

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value, shouldBeUntracked: IsPointerOverUI);

            ___hor += movement.ScreenSpaceRotation.x * plugin.RotationSensitivity.Value;
            ___ver += movement.ScreenSpaceRotation.y * plugin.RotationSensitivity.Value;

            return true;
        }

        static bool cameraPatched = false;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(Aim2), "Update")]
        public static IEnumerable<CodeInstruction> HookCameraTranslation(IEnumerable<CodeInstruction> instructions)
        {
            if (cameraPatched) return instructions;
            cameraPatched = true;
            var instList = instructions.ToList();

            Console.WriteLine("Hooked.");

            /*
             * We are hacking onto the code here:
             * 
             *      if ((Input.GetMouseButton(2) || ... ) { ... } else { ... }
             *  --> 
             *      this.tage += Vector3.Lerp(this.target.transform.position, ...);
             *      this.target.transform.position = Vector3.Lerp(...);
             * 
             * The code we're inserting is the following:
             *      
             *      // load references to local x and y variables
             *      ldflda  float32 Aim2::loc_x
             *      ldflda  float32 Aim2::loc_y
             *      // call our method!
             *      call    void Hook::HoocCameraTranslationData
             *      
             * So we're looking for the pattern:
             * 
             * -->
             *      131	01CF	ldarg.0
             *      132	01D0	ldfld       class [UnityEngine]UnityEngine.Transform Aim2::target
             *      133	01D5	callvirt    instance class [UnityEngine]UnityEngine.Transform [UnityEngine]UnityEngine.Component::get_transform()
             */

            var pattern_targetField = AccessTools.Field(typeof(Aim2), "tage");

            int? target = null;


            for (int i = 1; i < instList.Count - 2; i++)
            {
                if (instList[i].IsLdarg(0)
                    && instList[i + 1].opcode == OpCodes.Dup
                    && instList[i + 2].LoadsField(pattern_targetField))
                {
                    target = i;
                    break;
                }
            }

            if (target != null)
            {
                var locXField = AccessTools.Field(typeof(Aim2), "loc_x");
                var locYField = AccessTools.Field(typeof(Aim2), "loc_y");
                var hookFunction = AccessTools.Method(typeof(Hook), nameof(HookCameraTranslationData));

                if (locXField == null || locYField == null || hookFunction == null)
                {
                    throw new Exception($"Unable to get methods: locx -> {locXField}, locy -> {locYField}, hook -> {hookFunction}");
                }

                var label = instList[target.Value].labels;
                instList[target.Value].labels = new List<Label>();

                // patch the code
                instList.InsertRange(target.Value, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0){ labels = label },
                    new CodeInstruction(OpCodes.Ldflda, locXField),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, locYField),
                    new CodeInstruction(OpCodes.Call, hookFunction)
                });
            }

            return instList;
        }

        static void HookCameraTranslationData(ref float x, ref float y)
        {
            var plugin = Plugin.Instance;
            if (!plugin.Config.PluginEnabled.Value) return;

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value);
            x += movement.ScreenSpaceTranslation.x * plugin.TranslationSensitivity.Value;
            y += movement.ScreenSpaceTranslation.y * plugin.TranslationSensitivity.Value;
        }
    }
}
