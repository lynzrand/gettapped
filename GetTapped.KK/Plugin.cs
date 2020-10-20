using BepInEx;
using System;
using Karenia.GetTapped.Core;
using HarmonyLib;
using System.IO;
using System.Diagnostics;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Karenia.GetTapped.KK
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.kk";
        public const string projectName = "GetTapped.KK";
        public const string version = "0.1.1";

        public Plugin()
        {
            PluginConfig = new PluginConfig();
            PluginConfig.BindConfig(Config);
            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            Core = new PluginCore();
            Instance = this;
            harmony = new Harmony(id);
            harmony.PatchAll(typeof(Hook));
        }

        public static Plugin Instance { get; private set; }

        public PluginConfig PluginConfig { get; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }

        private readonly Harmony harmony;

        public IGetTappedPlugin Core { get; private set; }

    }

    public static class Hook
    {
        static Hook()
        {
        }

        static readonly Type CamDat = AccessTools.Inner(typeof(BaseCameraControl), "CameraData");
        static readonly FieldInfo CamDatField = AccessTools.Field(typeof(BaseCameraControl), "CamDat");
        static readonly FieldInfo CamDatDir = AccessTools.Field(CamDat, "Dir");
        static readonly FieldInfo CamDatRot = AccessTools.Field(CamDat, "Rot");
        static readonly FieldInfo CamDatPos = AccessTools.Field(CamDat, "Pos");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CameraControl_Ver2), "Start")]
        public static void HookStart()
        {
            Plugin.Instance.Logger.LogInfo("Should be hooked now");
        }

        static int lastFrame = -1;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseCameraControl), "InputTouchProc")]
        public static void PatchCameraControl(BaseCameraControl __instance)
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            var movement = Plugin.Instance.Core.GetCameraMovement(forceRecalculate: true);

            if (!movement.HasMoved()) return;
            PluginConfig pluginConfig = Plugin.Instance.PluginConfig;
            var camdat = CamDatField.GetValue(__instance);
            Vector3 dir = (Vector3)CamDatDir.GetValue(camdat);
            Vector3 pos = (Vector3)CamDatPos.GetValue(camdat);
            Vector3 rot = (Vector3)CamDatRot.GetValue(camdat);
            dir.z *= Mathf.Clamp(dir.z + (movement.Zoom * pluginConfig.ZoomSensitivity.Value - 1), 0, float.PositiveInfinity);
            pos += __instance.transform.TransformDirection(movement.ScreenSpaceTranslation * pluginConfig.TranslationSensitivity.Value * 0.01f);
            rot += new Vector3(movement.ScreenSpaceRotation.y, movement.ScreenSpaceRotation.x, 0) * 0.1f * pluginConfig.RotationSensitivity.Value;
            CamDatDir.SetValue(camdat, dir);
            CamDatPos.SetValue(camdat, pos);
            CamDatRot.SetValue(camdat, rot);
            CamDatField.SetValue(__instance, camdat);
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(BaseCameraControl_Ver2), "CameraUpdate")]
        public static void PatchCameraControl2(BaseCameraControl_Ver2 __instance)
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Plugin instance = Plugin.Instance;
            var movement = instance.Core.GetCameraMovement(forceRecalculate: true);

            if (!movement.HasMoved()) return;

            var camdat = __instance.GetCameraData();
            PluginConfig pluginConfig = instance.PluginConfig;
            camdat.Dir.z = Mathf.Clamp(camdat.Dir.z + (movement.Zoom * pluginConfig.ZoomSensitivity.Value - 1), 0, float.PositiveInfinity);
            camdat.Pos += __instance.transform.TransformDirection(movement.ScreenSpaceTranslation * pluginConfig.TranslationSensitivity.Value * 0.01f);
            camdat.Rot += new Vector3(movement.ScreenSpaceRotation.y, movement.ScreenSpaceRotation.x, 0) * 0.1f * pluginConfig.RotationSensitivity.Value;
            __instance.SetCameraData(camdat);
        }
    }

}

