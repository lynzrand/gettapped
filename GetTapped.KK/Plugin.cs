using BepInEx;
using System;
using Karenia.GetTapped.Core;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine.EventSystems;
using Karenia.GetTapped.Util;

namespace Karenia.GetTapped.KK
{
    [BepInPlugin(id, projectName, version)]
    // It's always good to have this plugin in advance so we don't have to load it again
    [BepInDependency(realPovPluginName, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.gettapped.kk";
        public const string projectName = "GetTapped.KK";
        public const string version = "0.3.1";

        public const string realPovPluginName = "keehauled.realpov";

        public Plugin()
        {
            PluginConfig = new PluginConfig();
            PluginConfig.BindConfig(Config);
            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            Core = new PluginCore();
            Instance = this;
            harmony = new Harmony(id);
            harmony.PatchAll(typeof(CameraHook));
            // FIXME: It's not working now
            harmony.PatchAll(typeof(HTouchControlHook));
            CameraHook.TryPatchRealPov(harmony);
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

    public static class CameraHook
    {
        static CameraHook()
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
            //Plugin.Instance.Logger.LogInfo("Should be hooked now");
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
            dir.z -= (movement.Zoom - 1) * pluginConfig.ZoomSensitivity.Value;
            dir.z = Mathf.Min(dir.z, 0);
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
            camdat.Dir.z -= (movement.Zoom - 1) * pluginConfig.ZoomSensitivity.Value;
            camdat.Dir.z = Mathf.Min(camdat.Dir.z, 0);
            camdat.Pos += __instance.transform.TransformDirection(movement.ScreenSpaceTranslation * pluginConfig.TranslationSensitivity.Value * 0.01f);
            camdat.Rot += new Vector3(movement.ScreenSpaceRotation.y, -movement.ScreenSpaceRotation.x, 0) * 0.1f * pluginConfig.RotationSensitivity.Value;
            __instance.SetCameraData(camdat);
        }

        public static void TryPatchRealPov(Harmony harmony)
        {
            MethodInfo realPovUpdate;
            Type realPovCore;
            Assembly realPov;
            try
            {
                realPov = Assembly.Load("RealPOV.Koikatu");
                realPovCore = AccessTools.TypeByName("RealPOV.Koikatu.RealPOV");
                realPovLookRotationField = AccessTools.Field(AccessTools.TypeByName("RealPOV.Core.RealPOVCore"), "LookRotation");
                realPovUpdate =
                    AccessTools.Method("RealPOV.Core.RealPOVCore:LateUpdate")
                    ?? AccessTools.Method("RealPOV.Core.RealPOVCore:Update");
            }
            catch
            {
                Plugin.Instance.Logger.LogInfo("RealPOV not loaded.");
                return;
            }
            if (realPovCore == null || realPovUpdate == null || realPovLookRotationField == null)
            {
                Plugin.Instance.Logger.LogInfo("RealPOV Not loaded");
                return;
            }

            var dependencyAttr = realPovCore.GetCustomAttributes(false).Where(o => o.GetType() == typeof(BepInPlugin)).FirstOrDefault() as BepInPlugin;
            var realPovVersion = RealPovVersion.Unknown;

            if (dependencyAttr != null)
            {
                var version = dependencyAttr.Version;
                if (version.Major == 1)
                {
                    if (version.Minor <= 1)
                    {
                        realPovVersion = RealPovVersion._1_1_OrBelow;
                    }
                    else
                    {
                        realPovVersion = RealPovVersion._1_2_OrAbove;
                    }
                }
            }

            switch (realPovVersion)
            {
                case RealPovVersion._1_1_OrBelow:
                    harmony.Patch(
                        realPovUpdate,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(CameraHook), "RealPOVPostfix_ver1_1_AndEarlier")));
                    break;

                case RealPovVersion._1_2_OrAbove:
                    realPovLookCurrentCharaField = AccessTools.Field(AccessTools.TypeByName("RealPOV.Core.RealPOVCore"), "currentCharaGo");
                    harmony.Patch(
                      realPovUpdate,
                      postfix: new HarmonyMethod(AccessTools.Method(typeof(CameraHook), "RealPOVPostfix_ver1_2_AndLater")));
                    break;

                case RealPovVersion.Unknown:
                    Plugin.Instance.Logger.LogError($"Unknown RealPOV Version: {dependencyAttr?.Version}. Please open up an issue to get supported.");
                    break;
            }
        }

        private enum RealPovVersion
        {
            _1_1_OrBelow,
            _1_2_OrAbove,
            Unknown
        }

        private static FieldInfo realPovLookRotationField;
        private static FieldInfo realPovLookCurrentCharaField;

        /// <summary>
        /// Patch for RealPOV v1.1 and earlier, when <c>LookRotation</c> is just a Vector3
        /// </summary>
        public static void RealPOVPostfix_ver1_1_AndEarlier()
        {
            var movement = Plugin.Instance.Core.GetCameraMovement(shouldBeUntracked: IsPointerOverUI);
            var rotation = (Vector3)realPovLookRotationField.GetValue(null);
            rotation += new Vector3(movement.ScreenSpaceRotation.y, -movement.ScreenSpaceRotation.x, 0)
                * 0.1f
                * Plugin.Instance.PluginConfig.RotationSensitivity.Value;
            realPovLookRotationField.SetValue(null, rotation);
        }

        /// <summary>
        /// Patch for RealPOV v1.2 and later, when <c>LookRotation</c> is a Dictionary of character IDs
        /// </summary>
        public static void RealPOVPostfix_ver1_2_AndLater()
        {
            var lookRotation = (Dictionary<GameObject, Vector3>)realPovLookRotationField.GetValue(null);
            var currGameObject = (GameObject)realPovLookCurrentCharaField.GetValue(null);

            if (currGameObject != null && lookRotation.TryGetValue(currGameObject, out var rotation))
            {
                var movement = Plugin.Instance.Core.GetCameraMovement(shouldBeUntracked: IsPointerOverUI);
                rotation += new Vector3(movement.ScreenSpaceRotation.y, -movement.ScreenSpaceRotation.x, 0)
                    * 0.1f
                    * Plugin.Instance.PluginConfig.RotationSensitivity.Value;
                lookRotation[currGameObject] = rotation;
            }
        }
    }

    // FIXME: Controlling speed via sliding on related menu doesn't work yet
    public static class HTouchControlHook
    {
        private static int? speedControlLastClick = null;

        /*
         * When not in auto mode:
         *   Tap the pad to increase speed (no difference from before).
         *
         * When in auto mode:
         *   Drag in the pad to change speed.
         *
         * In either modes:
         *   Double tap the pad to switch to auto mode.
         *   Hold down one finger and tap the other on the pad to change motion.
         *
         */

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), "Start")]
        public static void AddSpeedControl(HSprite __instance)
        {
            var touchDownHandler = new EventTrigger.Entry() { eventID = EventTriggerType.PointerDown };
            touchDownHandler.callback.AddListener((_) => PointerDownHandler(__instance));

            var touchUpHandler = new EventTrigger.Entry() { eventID = EventTriggerType.PointerUp };
            touchUpHandler.callback.AddListener((_) => PointerUpHandler(__instance));

            var dragHandler = new EventTrigger.Entry() { eventID = EventTriggerType.Drag };
            dragHandler.callback.AddListener((data) => DragHandler(__instance, (PointerEventData)data));

            // Seems that `selectUIPads` is pre-defined in the scene.
            foreach (var pad in __instance.selectUIPads)
            {
                var trigger = pad.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) continue;

                trigger.triggers.Add(touchDownHandler);
                trigger.triggers.Add(touchUpHandler);
                trigger.triggers.Add(dragHandler);
            }
        }

        private static Vector2? dragStart = null;
        private static bool? isDragVertical = false;

        public static void DragHandler(HSprite instance, PointerEventData eventData)
        {
            if (dragStart == null) return;
            Vector2 deltaPos = eventData.position - dragStart.Value;
            if (isDragVertical == null && deltaPos.sqrMagnitude >= 10 * 10)
            {
                isDragVertical = Mathf.Abs(deltaPos.y) > Mathf.Abs(deltaPos.x);
            }

            if (isDragVertical.HasValue && isDragVertical.Value)
            {
                // Vertical drag,
                var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
                float ySize = 100f;
                if (canvas != null) ySize *= canvas.scaleFactor;

                float spd = eventData.delta.y / ySize;
                instance.flags.SpeedUpClick(spd, 1f);
            }
            else if (isDragVertical.HasValue && !isDragVertical.Value)
            {
                var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();

                float motionChangeThreshold = 50f;
                if (canvas != null) motionChangeThreshold *= canvas.scaleFactor;

                if (Mathf.Abs(deltaPos.x) > motionChangeThreshold)
                {
                    instance.flags.click = HFlag.ClickKind.motionchange;
                    dragStart = null;
                }
            }
        }

        public static void PointerDownHandler(HSprite __instance)
        {
            if (Input.touchCount == 1)
            {
                int fingerId = Input.GetTouch(0).fingerId;
                TouchPressDetector touchDetector = Plugin.Instance.TouchDetector;
                touchDetector.Update();

                var state = touchDetector.GetState(fingerId);
                Plugin.Instance.Logger.LogInfo(state.ToString() ?? "null");

                dragStart = Input.GetTouch(0).position;

                if (state != null)
                {
                    if (!state.hasMoved)
                    {
                        if (speedControlLastClick != fingerId)
                        {
                            //state.onLongPressCallback += (touch) =>
                            //{
                            //    __instance.flags.click = HFlag.ClickKind.motionchange;
                            //    Plugin.Instance.Logger.LogDebug("Changing motion");
                            //};
                            state.onDoubleClickCallback += (touch) =>
                            {
                                __instance.flags.click = HFlag.ClickKind.modeChange;
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
            else if (Input.touchCount == 2)
            {
                __instance.flags.click = HFlag.ClickKind.motionchange;
                Plugin.Instance.Logger.LogDebug("Changing motion");
            }
        }

        public static void PointerUpHandler(HSprite __instance)
        {
            Plugin.Instance.Logger.LogInfo("Pointer up");
            Plugin.Instance.TouchDetector.Update();
            dragStart = null;
            isDragVertical = null;
        }
    }
}

