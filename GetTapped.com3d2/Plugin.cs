using BepInEx;
using BepInEx.Configuration;
using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Karenia.GetTapped.Core;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Karenia.GetTapped.Util;

namespace Karenia.GetTapped.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.com3d2";
        public const string projectName = "GetTapped.COM3D2";
        public const string version = "0.2.0";

        public Plugin()
        {
            Instance = this;
            Config = new PluginConfig()
            {
                DefaultRotationSensitivity = 0.007f,
                DefaultTranslationSensitivity = -0.01f
            };
            Config.BindConfig(base.Config);
            LongPressThreshold = base.Config.Bind("default", nameof(LongPressThreshold), 0.6f, "Press longer than this value (in seconds) will be viewed as a long press");
            LongPressThreshold.SettingChanged += (sender, _) => { TouchPressDetector.LongPressThreshold = ((ConfigEntry<float>)sender).Value; };

            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            var harmony = new Harmony(id);
            Core = new PluginCore();

            harmony.PatchAll(typeof(Hook));
        }

        public void LateUpdate()
        {
            TouchPressDetector.LateUpdate();
        }

        public static Plugin Instance { get; private set; }
        public new PluginConfig Config { get; set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
        public IGetTappedPlugin Core { get; private set; }

        public TouchPressDetector TouchPressDetector { get; } = new TouchPressDetector();

        public ConfigEntry<bool> PluginEnabled { get => Config.PluginEnabled; }
        public ConfigEntry<bool> SingleTapTranslate { get => Config.SingleTapTranslate; }
        public ConfigEntry<float> RotationSensitivity { get => Config.RotationSensitivity; }
        public ConfigEntry<float> TranslationSensitivity { get => Config.TranslationSensitivity; }
        public ConfigEntry<float> ZoomSensitivity { get => Config.ZoomSensitivity; }
        public ConfigEntry<float> LongPressThreshold { get; }
    }



    public static class Hook
    {
        private static readonly List<RaycastResult> raycastResultSketchboard = new List<RaycastResult>();

        private class DummyWidget : UIWidgetContainer
        {
            public void OnClick() { }
        }

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


            bool isPointerOverUiChecker(Touch touch)
            {
                // NGUI raycasting touch input
                // See: http://www.tasharen.com/forum/index.php?topic=138.0
                var isPointerOverUi = UICamera.Raycast(touch.position);
                return isPointerOverUi;
            }

            var movement = plugin.Core.GetCameraMovement(plugin.SingleTapTranslate.Value, shouldBeUntracked: isPointerOverUiChecker);

            if (__instance.mouseControl && movement.HasMoved())
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

        /// <summary>
        /// Recreates the button triggering method but being touchscreen aware
        /// </summary>
        /// <param name="__instance"></param>
        /// <returns></returns>
        [HarmonyPostfix, HarmonyPatch(typeof(SaveAndLoadMgr), "ClickSaveOrLoadData")]
        public static void FixUiButtonClick(SaveAndLoadMgr __instance, string ___currentActiveData, SaveAndLoadCtrl ___m_ctrl, SaveAndLoadMgr.ViewType ___currentView)
        {
            var name = UIButton.current.name;
            if (___currentActiveData == name)
            {
                return;
            }

            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                TouchPressDetector touchPressDetector = Plugin.Instance.TouchPressDetector;
                touchPressDetector.SetCallback(touch.fingerId, EventState.LongPress, (_) =>
                {
                    if (___m_ctrl.ExistData(name))
                    {
                        ___m_ctrl.DeleteSaveOrLoadData(name);
                    }
                });
                touchPressDetector.SetCallback(touch.fingerId, EventState.ShortPress, (_) =>
                {
                    __instance.SetCurrentActiveData(name);
                    ___m_ctrl.SaveAndLoad(___currentView, name);
                });
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIRoot), "Start")]
        public static void AddEventSystem(UIRoot __instance)
        {
            __instance.gameObject.AddComponent<EventSystem>();
            var raycaster = __instance.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.TwoD;
            raycaster.ignoreReversedGraphics = false;
        }
    }
}
