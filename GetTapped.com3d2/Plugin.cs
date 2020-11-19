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

using static Karenia.GetTapped.Com3d2.MathfExt;

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
            Config = new PluginConfig();
            Config.BindConfig(base.Config);
            EyeConfig = new EyeMovementConfig();
            EyeConfig.Bind(base.Config);

            Logger = BepInEx.Logging.Logger.CreateLogSource("GetTapped");
            var harmony = new Harmony(id);
            Core = new PluginCore();

            harmony.PatchAll(typeof(Hook));
            harmony.PatchAll(typeof(EyeMovementHook));
        }


        public static Plugin Instance { get; private set; }
        public new PluginConfig Config { get; set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
        public IGetTappedPlugin Core { get; private set; }

        public ConfigEntry<bool> PluginEnabled { get => Config.PluginEnabled; }
        public ConfigEntry<bool> SingleTapTranslate { get => Config.SingleTapTranslate; }
        public ConfigEntry<float> RotationSensitivity { get => Config.RotationSensitivity; }
        public ConfigEntry<float> TranslationSensitivity { get => Config.TranslationSensitivity; }
        public ConfigEntry<float> ZoomSensitivity { get => Config.ZoomSensitivity; }
    }

    public class EyeMovementConfig
    {
        /*
         * tremor_interval (float) – time interval between two tremors in seconds
         * tremor_interval_sd (float) – standard deviation of tremor_interval
         * tremor_amplitude (float) – amplitude of tremor in degree
         * drift_speed (float) – speed of drift in degree / sec
         * drift_speed_sd (float) – standard deviation of drift
         * msaccade_interval (float) – time interval between two micro-saccade in seconds
         * msaccade_interval_sd (float) – standard deviation of micro-saccade interval
         * msaccade_direction_sd (float) – deviation of direction of movement towards fixation point
         * msaccade_speed (float) – micro-saccade speed in degree/second
         */

        //public ConfigEntry<float> TremorInterval { get; private set; }

        //public ConfigEntry<float> TremorStdDev { get; private set; }

        //public ConfigEntry<float> TremorAmplitude { get; private set; }

        public ConfigEntry<float> DriftSpeed { get; private set; }

        public ConfigEntry<float> DriftSpeedStdDev { get; private set; }

        public ConfigEntry<float> DriftDirectionRange { get; private set; }

        public ConfigEntry<float> MSaccadeInterval { get; private set; }

        public ConfigEntry<float> MSaccadeIntervalStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeDirectionDev { get; private set; }

        public ConfigEntry<float> MSaccadeSpeed { get; private set; }

        public ConfigEntry<float> MSaccadeSpeedStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeOvershootDev { get; private set; }

        public ConfigEntry<bool> DebugLog { get; private set; }

        public ConfigEntry<bool> Enabled { get; private set; }

        public void Bind(ConfigFile config)
        {
            const string section = "EyeMovement";
            Enabled = config.Bind(section, nameof(Enabled), true);

            //TremorInterval = config.Bind(section, nameof(TremorInterval), 0.012f);
            //TremorStdDev = config.Bind(section, nameof(TremorStdDev), 0.001f);
            //TremorAmplitude = config.Bind(section, nameof(TremorAmplitude), 0.05f);
            DriftSpeed = config.Bind(section, nameof(DriftSpeed), 800f, "Eye drift mean speed (degrees/second)");
            DriftSpeedStdDev = config.Bind(section, nameof(DriftSpeedStdDev), 200f, "Eye drift speed standard deviation");
            DriftDirectionRange = config.Bind(section, nameof(DriftDirectionRange), 100f, "Eye drift direction change range");
            MSaccadeInterval = config.Bind(section, nameof(MSaccadeInterval), 0.8f, "Micro-saccade mean interval (seconds)");
            MSaccadeIntervalStdDev = config.Bind(section, nameof(MSaccadeIntervalStdDev), 0.6f, "Micro-saccade mean interval");
            MSaccadeDirectionDev = config.Bind(section, nameof(MSaccadeDirectionDev), 40f, "Micro-saccade direction standard deviation (degrees)");
            MSaccadeSpeed = config.Bind(section, nameof(MSaccadeSpeed), 10000f, "Micro-saccade mean speed (degrees/second)");
            MSaccadeSpeedStdDev = config.Bind(section, nameof(MSaccadeSpeedStdDev), 300f, "Micro-saccade speed standard deviation");
            MSaccadeOvershootDev = config.Bind(section, nameof(MSaccadeOvershootDev), 0.6f, "Micro-saccade overshooting standard deviation");

            DebugLog = config.Bind(section, nameof(DebugLog), true);
        }
    }

    public static class Hook
    {

    }

    public static class MathfExt
    {
        public static float GaussianRandom(float mean, float stdDev)
        {
            //uniform [0,1] random doubles
            var u1 = UnityEngine.Random.value;
            var u2 = UnityEngine.Random.value;
            //random normal(0,1)
            var randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            //random normal(mean,stdDev^2)
            return mean + stdDev * randStdNormal;
        }
    }

    /// <summary>
    /// State and algorithm of fixational eye simulator.
    /// 
    /// <para>
    /// The original algorithm comes from https://github.com/hjiang36/pyEyeBall/
    /// </para>
    /// </summary>
    public class EyeMovementState
    {
        public EyeMovementState(EyeMovementConfig config)
        {
            this.config = config;
        }

        private readonly EyeMovementConfig config;

        Vector2 curDelta = Vector2.zero;
        float curDriftDirection = UnityEngine.Random.Range(0f, Mathf.PI * 2);
        float curDriftSpeed = 0;

        //float timeTillNextTremor = 0;
        float timeTillNextSaccade = 0;
        float remainingTimeOfThisSaccade = 0;
        float mSaccadeSpeed;
        float mSaccadeAxis;

        /// <summary>
        /// Generate information for the upcoming micro-saccade
        /// </summary>
        private void SetNextSaccade()
        {
            // Set time according to config
            var time = GaussianRandom(config.MSaccadeInterval.Value, config.MSaccadeIntervalStdDev.Value);
            if (time < config.MSaccadeInterval.Value / 2) time = config.MSaccadeInterval.Value;
            timeTillNextSaccade = time;

            // Micro-saccade is toward center point, but with deviation.
            // Note: this angle calculated is **away from center**, reverse is done afterwards.
            mSaccadeAxis = Mathf.Atan2(curDelta.y, curDelta.x);
            mSaccadeAxis += GaussianRandom(0, config.MSaccadeDirectionDev.Value * Mathf.Deg2Rad);

            // Generate saccade speed
            mSaccadeSpeed = GaussianRandom(config.MSaccadeSpeed.Value, config.MSaccadeSpeedStdDev.Value) * Mathf.Deg2Rad;
            if (mSaccadeSpeed < 0) mSaccadeSpeed = config.MSaccadeSpeed.Value * Mathf.Deg2Rad;

            // Generate move angle with deviation
            var angleToCenter = curDelta.magnitude;
            angleToCenter *= GaussianRandom(1f, config.MSaccadeOvershootDev.Value);

            // Set remaining time
            remainingTimeOfThisSaccade = angleToCenter / mSaccadeSpeed;

            if (Plugin.Instance.EyeConfig.DebugLog.Value) Plugin.Instance.Logger.LogInfo($"Saccade: {angleToCenter} @ {mSaccadeAxis * Mathf.Rad2Deg}deg");
        }

        /// <summary>
        /// Update movement of micro-saccade if needed
        /// </summary>
        /// <param name="deltaTime"></param>
        private void UpdateSaccade(float deltaTime)
        {
            if (remainingTimeOfThisSaccade > 0)
            {
                var saccadeTime = Mathf.Min(deltaTime, remainingTimeOfThisSaccade);
                remainingTimeOfThisSaccade -= deltaTime;

                // Generate direction vector.
                var quat = new Vector2(Mathf.Cos(mSaccadeAxis), Mathf.Sin(mSaccadeAxis)) * (saccadeTime * mSaccadeSpeed);
                curDelta -= quat;

                if (Plugin.Instance.EyeConfig.DebugLog.Value) Plugin.Instance.Logger.LogInfo($"Performing Saccade: {saccadeTime * mSaccadeSpeed * Mathf.Rad2Deg} @ {mSaccadeAxis * Mathf.Rad2Deg}deg");
            }
        }

        /// <summary>
        /// Update movement by random drifting
        /// </summary>
        /// <param name="deltaTime"></param>
        private void UpdateDrift(float deltaTime)
        {
            // Drift direction is current drift direction plus some deviation
            curDriftDirection += (UnityEngine.Random.value * 2 - 1) * config.DriftDirectionRange.Value;
            curDriftDirection %= 2 * Mathf.PI;
            curDriftSpeed = GaussianRandom(config.DriftSpeed.Value, config.DriftSpeedStdDev.Value) * Mathf.Deg2Rad;

            // Calculate drift delta
            curDelta += new Vector2(Mathf.Cos(curDriftDirection), Mathf.Sin(curDriftDirection)) * curDriftSpeed * deltaTime;

            if (Plugin.Instance.EyeConfig.DebugLog.Value) Plugin.Instance.Logger.LogInfo($"Drift: {curDriftSpeed} @ {curDriftDirection * Mathf.Rad2Deg}deg");
        }

        //private void SetNextTremor()
        //{
        //    var time = GaussianRandom(config.TremorInterval.Value, config.TremorStdDev.Value);
        //    if (time < 0.001f) time = 0.001f;

        //    var direction = UnityEngine.Random.Range(0f, 360f);
        //    var axis = new Vector3(Mathf.Cos(direction), 0f, Mathf.Sin(direction));
        //    var quat = Quaternion.AngleAxis(config.TremorAmplitude.Value, axis);

        //    timeTillNextTremor = time;
        //    curDelta *= quat;
        //}

        /// <summary>
        /// Update model and return calculated delta on this frame
        /// </summary>
        /// <param name="deltaTime">Time passed (in seconds) after last <c>Tick()</c></param>
        /// <returns>Euler angle delta in <c>x</c> and <c>z</c> direction relative to idle position</returns>
        public Vector2 Tick(float deltaTime)
        {
            timeTillNextSaccade -= deltaTime;
            if (timeTillNextSaccade <= 0) SetNextSaccade();
            UpdateSaccade(deltaTime);

            UpdateDrift(deltaTime);

            //timeTillNextTremor -= deltaT;
            //if (timeTillNextTremor <= 0) SetNextTremor();

            return this.curDelta;
        }
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

            if (!eyeInfoRepo.TryGetValue(__instance, out var info))
            {
                Plugin.Instance.Logger.LogWarning($"Here's a female without init! name: {FormatMaidName(__instance.maid)}");
                return;
            }

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


        public static void FixationalEyeMovementPatch2(TBody instance, ref Vector3 eulerAngles)
        {
            // Men have no eyes so nothing to see
            if (instance.boMAN) return;
            if (!Plugin.Instance.EyeConfig.Enabled.Value) return;

            if (!eyeInfoRepo.TryGetValue(instance, out var info))
            {
                Plugin.Instance.Logger.LogWarning($"Here's a female without init! name: {FormatMaidName(instance.maid)}");
                return;
            }

            var delta = info.Tick(Time.deltaTime);
            eulerAngles += new Vector3(delta.x, 0, delta.y);
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

        private static bool patched = false;

        // obsolete method
        //[HarmonyTranspiler, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static IEnumerable<CodeInstruction> AddFixationalEyeMovement(IEnumerable<CodeInstruction> instructions)
        {
            if (patched) return instructions;
            patched = true;

            AssertNotNull(instructions, nameof(instructions));

            var instList = instructions.ToList();
            var targetField_trsEyeL = AccessTools.Field(typeof(TBody), nameof(TBody.trsEyeL));
            var targetField_quaDefEyeL = AccessTools.Field(typeof(TBody), "quaDefEyeL");

            AssertNotNull(targetField_trsEyeL, nameof(targetField_trsEyeL));
            AssertNotNull(targetField_quaDefEyeL, nameof(targetField_quaDefEyeL));
            AssertNotNull(instList, nameof(instList));

            int targetInstruction = -1;

            for (int i = 1; i < instList.Count - 5; i++)
            {
                if (instList[i].IsLdarg(0)
                    && instList[i + 1].LoadsField(targetField_trsEyeL)
                    && instList[i + 2].IsLdarg(0)
                    && instList[i + 3].LoadsField(targetField_quaDefEyeL)
                    && instList[i + 4].Is(OpCodes.Ldc_R4, (float)0)
                    && instList[i + 5].IsLdarg(0))
                {
                    targetInstruction = i;
                    break;
                }
            }

            if (targetInstruction > 0)
            {
                var eulerAngleField = AccessTools.Field(typeof(TBody), "EyeEulerAngle");
                var hookFunction = AccessTools.Method(typeof(EyeMovementHook), nameof(FixationalEyeMovementPatch2));

                AssertNotNull(eulerAngleField, nameof(eulerAngleField));
                AssertNotNull(hookFunction, nameof(hookFunction));
                AssertNotNull(targetInstruction, nameof(targetInstruction));

                var labels = instList[targetInstruction].labels;
                instList[targetInstruction].labels = new List<Label>();

                instList.InsertRange(targetInstruction, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0) {labels = labels},
                    new CodeInstruction(OpCodes.Ldarg_0) { },
                    new CodeInstruction(OpCodes.Ldflda, eulerAngleField),
                    new CodeInstruction(OpCodes.Call, hookFunction)
                });

                Plugin.Instance.Logger.LogInfo($"Patched at TBody#MoveHeadAndEye+{targetInstruction}");
            }
            else
            {
                Plugin.Instance.Logger.LogInfo("For some reason patching failed");
            }

            return instList;
        }

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
