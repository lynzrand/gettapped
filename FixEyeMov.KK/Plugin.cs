using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Karenia.FixEyeMov.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Karenia.FixEyeMov.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixeyemov.kk";
        public const string projectName = "FixationalEyeMovements.KK";
        public const string version = "0.1.0";

        public Plugin()
        {
            Instance = this;
            EyeConfig = new EyeMovementConfig(base.Config, new EyeMovementDefaultConfig()
            {
                DriftSpeed = 200f,
                DriftSpeedStdDev = 100f,
                MSaccadeInterval = 0.3f,
                MSaccadeSpeed = 100000f,
                MSaccadeSpeedStdDev = 30000f,
                MaxOffsetAngle = 0.3f
            });

            Logger = BepInEx.Logging.Logger.CreateLogSource("FixEyeMov");
            var harmony = new Harmony(id);

            EyeMovementHook.SetupHooks(harmony);
        }

        public static Plugin Instance { get; private set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
    }

    public static class EyeMovementHook
    {
        public static void SetupHooks(Harmony harmony)
        {
            harmony.PatchAll(typeof(EyeMovementHook));
            SceneManager.activeSceneChanged += (Scene curr, Scene next) =>
            {
                // Enable mouse look when customizing characters
                canLookAtMouse = next.name.Contains("CustomScene");
            };
        }

        private static readonly Dictionary<EyeLookCalc, EyeMovementState> states = new Dictionary<EyeLookCalc, EyeMovementState>();
        public static bool canLookAtMouse = false;
        public static bool isLookingAtMouse = false;

        [HarmonyPatch(typeof(ChaControl), "ChangeLookEyesTarget")]
        [HarmonyPostfix]
        public static void HookAddLookAtMouse(ChaControl __instance, int targetType)
        {
            if (canLookAtMouse)
            {
                // Is looking at camera
                if (targetType == 0 && Camera.main && __instance.objEyesLookTarget && __instance.objEyesLookTargetP)
                {
                    // objEyesLookTarget is the real target; objEyesLookTargetP is its parent
                    __instance.objEyesLookTargetP.transform.SetParent(Camera.main.transform);
                    isLookingAtMouse = true;
                    __instance.objEyesLookTarget.transform.localPosition = new Vector3(0, 0, 0);
                }
                else
                {
                    // reset transform
                    if (__instance.objEyesLookTargetP.transform.parent == Camera.main.transform)
                    {
                        var referenceInfo2 = __instance.GetReferenceInfo(ChaReference.RefObjKey.HeadParent);
                        if (__instance.objEyesLookTargetP && referenceInfo2)
                        {
                            __instance.objEyesLookTargetP.transform.SetParent(referenceInfo2.transform, false);
                            __instance.objEyesLookTarget.transform.SetParent(__instance.objEyesLookTargetP.transform, false);
                        }
                    }
                    isLookingAtMouse = false;
                }
            }
        }

        [HarmonyPatch(typeof(EyeLookController), "LateUpdate")]
        [HarmonyPostfix]
        public static void HookLookingAtMouse(EyeLookController __instance)
        {
            if (isLookingAtMouse)
            {
                var mousePos = Input.mousePosition;
                var isInsideSceenSpace = mousePos.x >= 0 && mousePos.y >= 0 &&
                    mousePos.x < Screen.width && mousePos.y < Screen.height;

                if (isInsideSceenSpace)
                {
                    mousePos.Scale(new Vector3(1f / Screen.width, 1f / Screen.height, 0));
                    mousePos -= new Vector3(.5f, .5f, 0);
                    var mat = Camera.main.cameraToWorldMatrix;
                    var mouseCameraOffset = mat.MultiplyPoint(mousePos);
                    __instance.target.localPosition = mouseCameraOffset;
                }
                else
                {
                    __instance.target.localPosition = Vector3.zero;
                }
            }
        }

        [HarmonyPatch(typeof(EyeLookCalc), "EyeUpdateCalc")]
        [HarmonyPrefix]
        public static bool AddPoiMove(EyeLookCalc __instance, Vector3 target,
            bool ___initEnd, int ptnNo)
        {
            // Initial calculations, same as source
            if (!___initEnd || !EyeLookCalc.isEnabled || Time.deltaTime == 0f)
            {
                if (__instance.targetObj != null && __instance.targetObj.activeSelf)
                    __instance.targetObj.SetActive(false);
                return false;
            }

            // get the coreesponding eye type information
            EyeTypeState state = __instance.eyeTypeStates[ptnNo];
            EYE_LOOK_TYPE lookType = state.lookType;

            // Static eyes
            if (lookType == EYE_LOOK_TYPE.TARGET)
            {
            }

            return true;
        }

        private static readonly MethodInfo AngleHRateCalc = AccessTools.Method(typeof(EyeLookCalc), "AngleHRateCalc");
        private static readonly MethodInfo AngleVRateCalc = AccessTools.Method(typeof(EyeLookCalc), "AngleVRateCalc");

        [HarmonyPatch(typeof(EyeLookCalc), "Init")]
        [HarmonyPostfix]
        public static void RegisterEyeLookCalc(EyeLookCalc __instance)
        {
            if (states.TryGetValue(__instance, out var v)) return;
            states.Add(__instance, new EyeMovementState(Plugin.Instance.EyeConfig, Plugin.Instance.Logger));
            var deiniter = __instance.gameObject.AddComponent<Deiniter>();
            deiniter.ChainCallback(() => states.Remove(__instance));
        }

        public class Deiniter : MonoBehaviour
        {
            private Action callback;

            public Deiniter()
            {
                this.callback = null;
            }

            public void ChainCallback(Action a)
            {
                var original = this.callback;
                if (original == null) this.callback = a;
                else this.callback = () => { a(); original(); };
            }

            public void Destroy()
            {
                if (this.callback != null)
                    this.callback();
            }
        }

        private static readonly FieldInfo AngleH = AccessTools.Field(typeof(EyeObject), "angleH");
        private static readonly FieldInfo AngleV = AccessTools.Field(typeof(EyeObject), "angleV");

        [HarmonyPatch(typeof(EyeLookCalc), "EyeUpdateCalc")]
        [HarmonyPostfix]
        public static void AddFixationalMovements(EyeLookCalc __instance, Vector3 target,
             bool ___initEnd, int ptnNo)
        {
            if (!___initEnd || !EyeLookCalc.isEnabled || Time.deltaTime == 0f)
            {
                return;
            }

            var state = states[__instance];

            var delta = state.Tick(Time.deltaTime);

            for (var i = 0; i < __instance.eyeObjs.Length; i++)
            {
                var obj = __instance.eyeObjs[i];
                var h = (float)AngleH.GetValue(obj) + delta.x;
                AngleH.SetValue(obj, h);

                var v = (float)AngleV.GetValue(obj) + delta.y;
                AngleV.SetValue(obj, v);
            }

            AngleHRateCalc.Invoke(__instance, new object[] { });
            __instance.angleVRate = (float)AngleVRateCalc.Invoke(__instance, new object[] { });
        }
    }
}
