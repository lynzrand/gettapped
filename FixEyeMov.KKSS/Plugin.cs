using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Karenia.FixEyeMov.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Karenia.FixEyeMov.KKSS
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixeyemov.kkss";
        public const string projectName = "FixationalEyeMovements.KKSS";
        public const string version = "0.1.0";

        public Plugin()
        {
            Instance = this;
            EyeConfig = new EyeMovementConfig(
                base.Config, new EyeMovementDefaultConfig()
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

            BindHookConfig(Config);

            EyeMovementHook.SetupHooks(harmony);
        }

        internal ConfigEntry<bool> followMouseInEditor;
        internal ConfigEntry<bool> followMouseInTalkScene;
        internal ConfigEntry<bool> followMouse;
        internal ConfigEntry<bool> neckFollowMouse;

        public static Plugin Instance { get; private set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }

        public void BindHookConfig(ConfigFile config)
        {
            followMouse = config.Bind<bool>("MouseTrackng", "EyesFollowMouse", false);
            neckFollowMouse = config.Bind<bool>("MouseTracking", "NeckFollowMouse", false);
            followMouseInEditor = config.Bind<bool>("MouseTracking", "FollowMouseInEditor", true);
            followMouseInTalkScene = config.Bind<bool>("MouseTracking", "FollowMouseInTalkScene", false);

            config.SettingChanged += (f, v) =>
            {
                EyeMovementHook.shouldEyesFollowMouse = followMouse.Value;
                EyeMovementHook.shouldNeckFollowMouse = neckFollowMouse.Value;
            };
        }
    }

    public static class EyeMovementHook
    {
        private static readonly Dictionary<EyeLookCalc, EyeMovementState> states = new Dictionary<EyeLookCalc, EyeMovementState>();

        public static void SetupHooks(Harmony harmony)
        {
            harmony.PatchAll(typeof(EyeMovementHook));
            SceneManager.activeSceneChanged += (Scene curr, Scene next) =>
            {
                // Enable mouse look when customizing characters
                sceneCanFollowMouse = false;
                if (Plugin.Instance.followMouseInEditor.Value)
                {
                    sceneCanFollowMouse |= next.name.Contains("CustomScene");
                }
                if (Plugin.Instance.followMouseInTalkScene.Value)
                {
                    sceneCanFollowMouse |= false;
                }
                //Plugin.Instance.Logger.LogInfo($"Set CanLookAtMouse = {canLookAtMouse}");
            };
        }

        public static bool sceneCanFollowMouse = false;

        public static bool shouldEyesFollowMouse = false;
        public static bool shouldNeckFollowMouse = false;

        public static bool eyesFollowingMouse = false;
        public static bool neckFollowingMouse = false;

        [HarmonyPatch(typeof(ChaControl), "ChangeLookEyesTarget")]
        [HarmonyPostfix]
        public static void HookChangeEyesLookTarget(ChaControl __instance, int targetType)
        {
            if (sceneCanFollowMouse && shouldEyesFollowMouse)
            {
                // Is looking at camera
                if (targetType == 0 && Camera.main && __instance.objEyesLookTarget && __instance.objEyesLookTargetP)
                {
                    // we dont need to reset target because we directly sets its position
                    eyesFollowingMouse = true;
                    __instance.eyeLookCtrl.target = __instance.objEyesLookTarget.transform;
                }
                else
                {
                    eyesFollowingMouse = false;
                }
                //Plugin.Instance.Logger.LogInfo($"Set isLookingAtMouse = {isLookingAtMouse}");
            }
        }

        [HarmonyPatch(typeof(ChaControl), "ChangeLookNeckTarget")]
        [HarmonyPostfix]
        public static void HookChangeNeckLookTarget(ChaControl __instance, int targetType)
        {
            if (sceneCanFollowMouse && shouldNeckFollowMouse)
            {
                // Is looking at camera
                if (targetType == 0 && Camera.main && __instance.objNeckLookTarget && __instance.objNeckLookTargetP)
                {
                    // we dont need to reset target because we directly sets its position
                    neckFollowingMouse = true;
                    __instance.neckLookCtrl.target = __instance.objNeckLookTarget.transform;
                }
                else
                {
                    neckFollowingMouse = false;
                }
                //Plugin.Instance.Logger.LogInfo($"Set isLookingAtMouse = {isLookingAtMouse}");
            }
        }

        [HarmonyPatch(typeof(EyeLookController), "LateUpdate")]
        [HarmonyPrefix]
        public static void HookEyesLookingAtMouse(EyeLookController __instance)
        {
            if (eyesFollowingMouse && shouldEyesFollowMouse)
            {
                var mousePos = Input.mousePosition;
                var isMouseInsideScreenSpace = mousePos.x >= 0 && mousePos.y >= 0 &&
                    mousePos.x < Screen.width && mousePos.y < Screen.height;

                if (isMouseInsideScreenSpace)
                {
                    var cam = Camera.main;

                    var monitorPlaneDistance = (cam.transform.position - __instance.transform.position).magnitude / 1.5f;

                    mousePos.z = monitorPlaneDistance;
                    var rayHit = cam.ScreenToWorldPoint(mousePos);

                    __instance.target.position = rayHit;

                    //Plugin.Instance.Logger.LogInfo($"Looking at mouse @ {rayHit}");
                }
                else
                {
                    __instance.target.position = Camera.main.transform.position;
                }
            }
        }

        [HarmonyPatch(typeof(NeckLookControllerVer2), "LateUpdate")]
        [HarmonyPrefix]
        public static void HookNeckLookingAtMouse(NeckLookControllerVer2 __instance)
        {
            if (neckFollowingMouse && shouldNeckFollowMouse)
            {
                var mousePos = Input.mousePosition;
                var isMouseInsideScreenSpace = mousePos.x >= 0 && mousePos.y >= 0 &&
                    mousePos.x < Screen.width && mousePos.y < Screen.height;

                if (isMouseInsideScreenSpace)
                {
                    var cam = Camera.main;

                    var monitorPlaneDistance = (cam.transform.position - __instance.transform.position).magnitude / 1.5f;

                    mousePos.z = monitorPlaneDistance;
                    var rayHit = cam.ScreenToWorldPoint(mousePos);

                    __instance.target.position = rayHit;

                    //Plugin.Instance.Logger.LogInfo($"Looking at mouse @ {rayHit}");
                }
                else
                {
                    __instance.target.position = Camera.main.transform.position;
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

            //// Static eyes
            //if (lookType == EYE_LOOK_TYPE.TARGET)
            //{
            //}

            return true;
        }

        //[HarmonyPatch(typeof(EyeLookCalc), "EyeUpdateCalc")]
        //[HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileEyeLookCalc(IEnumerable<CodeInstruction> code)
        {
            var eyeTypeStates = AccessTools.Field(typeof(EyeLookCalc), "eyeTypeStates");
            var hAngleLimit = AccessTools.Field(typeof(EyeTypeState), "hAngleLimt");
            var vAngleLimit = AccessTools.Field(typeof(EyeTypeState), "vAngleLimt");

            /*
             * 185	0214	ldarg.0
             * 186	0215	ldfld	class EyeTypeState[] EyeLookCalc::eyeTypeStates
             * 187	021A	ldarg.2
             * 188	021B	ldelem.ref
             * 189	021C	ldfld	float32 EyeTypeState::hAngleLimit
             * 190	0221	bgt.s	198 (0234) ldc.i4.3
             * 191	0223	ldloc.s	V_5 (5)
             * 192	0225	ldarg.0
             * 193	0226	ldfld	class EyeTypeState[] EyeLookCalc::eyeTypeStates
             * 194	022B	ldarg.2
             * 195	022C	ldelem.ref
             * 196	022D	ldfld	float32 EyeTypeState::vAngleLimit
             * 197	0232	ble.un.s	200 (0236) ldloc.2
             * -------------
             * 198	0234	ldc.i4.
             * 199	0235	stloc.2
             * -------------
             * 200	0236	ldloc.2

             */

            return new CodeMatcher(code)
                .MatchForward(true, new[]
                {
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld,eyeTypeStates),
                    new CodeMatch(OpCodes.Ldarg_2),
                    new CodeMatch(OpCodes.Ldelem_Ref),
                    new CodeMatch(OpCodes.Ldfld,vAngleLimit),
                    new CodeMatch(OpCodes.Ble_Un_S),
                })
                .Advance(1)
                .SetAndAdvance(OpCodes.Nop, null)
                .SetAndAdvance(OpCodes.Nop, null)
                .InstructionEnumeration();
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

