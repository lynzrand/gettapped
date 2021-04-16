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
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Karenia.GetTapped.Util;

namespace Karenia.GetTapped.KK
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.kk";
        public const string projectName = "GetTapped.KK";
        public const string version = "0.2.1";

        public Plugin()
        {
            PluginConfig = new PluginConfig();
            PluginConfig.BindConfig(Config);
            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            Core = new PluginCore();
            Instance = this;
            harmony = new Harmony(id);
            harmony.PatchAll(typeof(Hook));
            // FIXME: It's not working now
            harmony.PatchAll(typeof(HTouchControlHook));
        }

        public void Update()
        {
            TouchDetector.Update();
        }

        public static Plugin Instance { get; private set; }

        public PluginConfig PluginConfig { get; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }

        private readonly Harmony harmony;

        public TouchPressDetector TouchDetector { get; private set; } = new TouchPressDetector();

        public IGetTappedPlugin Core { get; private set; }
    }

    public static class Hook
    {
        static Hook()
        {
        }

        private static readonly Type CamDat = AccessTools.Inner(typeof(BaseCameraControl), "CameraData");
        private static readonly FieldInfo CamDatField = AccessTools.Field(typeof(BaseCameraControl), "CamDat");
        private static readonly FieldInfo CamDatDir = AccessTools.Field(CamDat, "Dir");
        private static readonly FieldInfo CamDatRot = AccessTools.Field(CamDat, "Rot");
        private static readonly FieldInfo CamDatPos = AccessTools.Field(CamDat, "Pos");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CameraControl_Ver2), "Start")]
        public static void HookStart()
        {
            Plugin.Instance.Logger.LogInfo("Should be hooked now");
        }

        private static int lastFrame = -1;

        private static readonly List<RaycastResult> raycastResultsSketchpad = new List<RaycastResult>();

        private static bool IsPointerOverUI(Touch touch)
        {
            return EventSystem.current.IsPointerOverGameObject(touch.fingerId);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseCameraControl), "InputTouchProc")]
        public static void PatchCameraControl(BaseCameraControl __instance)
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;

            var movement = Plugin.Instance.Core.GetCameraMovement(shouldBeUntracked: IsPointerOverUI);

            if (!movement.HasMoved()) return;
            PluginConfig pluginConfig = Plugin.Instance.PluginConfig;
            var camdat = CamDatField.GetValue(__instance);
            Vector3 dir = (Vector3)CamDatDir.GetValue(camdat);
            Vector3 pos = (Vector3)CamDatPos.GetValue(camdat);
            Vector3 rot = (Vector3)CamDatRot.GetValue(camdat);
            dir.z *= Mathf.Clamp(dir.z + (movement.Zoom * pluginConfig.ZoomSensitivity.Value - 1), 0, float.PositiveInfinity);
            pos += __instance.transform.TransformDirection(movement.ScreenSpaceTranslation * pluginConfig.TranslationSensitivity.Value * 0.01f);
            rot += new Vector3(movement.ScreenSpaceRotation.y, -movement.ScreenSpaceRotation.x, 0) * 0.1f * pluginConfig.RotationSensitivity.Value;
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

            var movement = instance.Core.GetCameraMovement(shouldBeUntracked: IsPointerOverUI);

            if (!movement.HasMoved()) return;

            var camdat = __instance.GetCameraData();
            PluginConfig pluginConfig = instance.PluginConfig;
            camdat.Dir.z = Mathf.Clamp(camdat.Dir.z + (movement.Zoom * pluginConfig.ZoomSensitivity.Value - 1), 0, float.PositiveInfinity);
            camdat.Pos += __instance.transform.TransformDirection(movement.ScreenSpaceTranslation * pluginConfig.TranslationSensitivity.Value * 0.01f);
            camdat.Rot += new Vector3(movement.ScreenSpaceRotation.y, -movement.ScreenSpaceRotation.x, 0) * 0.1f * pluginConfig.RotationSensitivity.Value;
            __instance.SetCameraData(camdat);
        }
    }

    // FIXME: Controlling speed via sliding on related menu doesn't work yet
    public static class HTouchControlHook
    {
        private static int? speedControlLastClick = null;

        [HarmonyPostfix]
        //[HarmonyPatch(typeof(HSonyu), "LoopProc")]
        //[HarmonyPatch(typeof(HHoushi), "LoopProc")]
        //[HarmonyPatch(typeof(H3PSonyu), "LoopProc")]
        //[HarmonyPatch(typeof(H3PHoushi), "LoopProc")]
        //[HarmonyPatch(typeof(H3PDarkSonyu), "LoopProc")]
        //[HarmonyPatch(typeof(H3PDarkHoushi), "LoopProc")]
        [HarmonyPatch(typeof(HSprite), "OnSpeedUpClick")]
        public static void HSpeedControl(HSprite __instance, HFlag ___flags)
        {
            //bool onPad = __instance.IsCursorOnPad();
            var onPad = true;
            //Plugin.Instance.Logger.LogInfo($"Cursor on pad: {onPad}");
            if (onPad && Input.touchCount == 1)
            {
                var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
                float ySize = 100f;
                if (canvas != null) ySize *= canvas.scaleFactor;

                int fingerId = Input.GetTouch(0).fingerId;
                TouchPressDetector touchDetector = Plugin.Instance.TouchDetector;
                touchDetector.Update();
                var state = touchDetector.GetTouchTime(fingerId);
                if (state != null)
                {
                    if (state.hasMoved)
                    {
                        ___flags.SpeedUpClick(Input.GetTouch(0).deltaPosition.y / ySize, 1f);
                    }
                    else
                    {
                        if (speedControlLastClick != fingerId)
                        {
                            state.onLongPressCallback += (touch) =>
                            {
                                ___flags.click = HFlag.ClickKind.motionchange;
                                Plugin.Instance.Logger.LogDebug("Changing mode");
                            };
                            state.onDoubleClickCallback += (touch) =>
                             {
                                 ___flags.click = HFlag.ClickKind.modeChange;
                                 Plugin.Instance.Logger.LogDebug("Changing mode");
                             };
                            state.onPressCancelCallback += (touch) =>
                            {
                                speedControlLastClick = null;
                            };
                            speedControlLastClick = fingerId;
                        }
                    }
                }
            }
            else if (onPad && Input.touchCount == 2)
            {
                ___flags.click = HFlag.ClickKind.motionchange;
                Plugin.Instance.Logger.LogDebug("Changing motion");
            }
            else if (onPad && Input.touchCount == 3)
            {
                ___flags.click = HFlag.ClickKind.modeChange;
                Plugin.Instance.Logger.LogDebug("Changing mode");
            }
        }
    }
}

