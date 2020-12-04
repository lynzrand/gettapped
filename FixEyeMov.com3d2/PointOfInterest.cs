using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;

namespace Karenia.FixEyeMov.Com3d2.Poi
{
    /*
     * Point of interest is an alternative for the naive "stare at camera" feature.
     * 
     * Using this module characters can look around different interested targets, 
     * making them more life-like.
     */

    public class PointOfInterest
    {
        public PointOfInterest(Transform target, float weight)
        {
            this.target = target;
            this.weight = weight;
        }

        /// <summary>
        /// The target object's transform.
        /// </summary>
        public Transform target;

        /// <summary>
        /// A measurement of interest in this target. A weight of 1.0 means for transfer probability 
        /// would be 50% if triggered at 1s after staring at this target.
        /// </summary>
        public float weight;
    }

    public class PoiConfig
    {
        private const string section = "PointOfInterest";

        public PoiConfig(ConfigFile config, Func<Transform, Transform, float>? targetWeightFactorFunction = null)
        {
            TransferCheckInterval = config.Bind(section, nameof(TransferCheckInterval), 0.4f, "Average interval (seconds) between transfers");
            TransferCheckStdDev = config.Bind(section, nameof(TransferCheckStdDev), 0.07f, "Standard deviation between transfer checks");
            if (targetWeightFactorFunction != null) TargetWeightFactorFunction = targetWeightFactorFunction;
        }

        public ConfigEntry<float> TransferCheckInterval { get; private set; }
        public ConfigEntry<float> TransferCheckStdDev { get; private set; }
        public Func<Transform, Transform, float> TargetWeightFactorFunction { get; set; } = (_, _) => 1.0f;
    }

    public class PointOfInterestManager
    {
        private readonly PoiConfig config;

        public PointOfInterestManager(PoiConfig config, Transform baseTransform, Rect viewport)
        {
            this.config = config;
            this.baseTransform = baseTransform;
            this.viewport = viewport;
        }

        readonly SortedDictionary<string, PointOfInterest> pointOfInterest = new SortedDictionary<string, PointOfInterest>();
        KeyValuePair<string, PointOfInterest>? currentTarget = null;
        float stareTime = 0;
        float timeTillNextTransfer = 0;

        public Transform baseTransform;
        public Rect viewport;

        struct PoiListItem
        {
            public KeyValuePair<string, PointOfInterest> kv;
            public float realWeight;
        }

        private KeyValuePair<string, PointOfInterest>? GetNextInterestPoint()
        {
            float totalSum = 0;
            var poiList = new List<PoiListItem>();

            foreach (var kv in pointOfInterest)
            {
                var viewAngle = Quaternion.FromToRotation(baseTransform.up, kv.Value.target.position - baseTransform.position).eulerAngles;
                var eulerAngle2D = new Vector2(viewAngle.x, viewAngle.z);
                if (viewport.Contains(eulerAngle2D))
                {
                    var weight = kv.Value.weight * config.TargetWeightFactorFunction(baseTransform, kv.Value.target);
                    poiList.Add(new PoiListItem() { kv = kv, realWeight = weight });
                    totalSum += weight;
                }
            }

            if (poiList.Count == 0) return null;

            float target = UnityEngine.Random.Range(0, totalSum);

            float partialSum = 0;
            foreach (var item in poiList)
            {
                partialSum += item.realWeight;
                if (partialSum > target)
                {
                    return item.kv;
                }
            }
            return poiList[poiList.Count - 1].kv;
        }

        private bool TriggerTransfer()
        {
            if (currentTarget == null) return true;
            if (stareTime == 0) return false;
            var val = currentTarget.Value;
            var w = val.Value.weight;
            if (w == 0) return true;

            var x = stareTime / w;
            var randomVar = Mathf.Exp(GaussianRandom(0f, 0.4f));

            return randomVar > x;
        }

        private void ResetTransferInterval()
        {
            timeTillNextTransfer = LogNormalRandom(config.TransferCheckInterval.Value, config.TransferCheckStdDev.Value);
        }

        public Transform? Tick(float deltaT)
        {
            if (pointOfInterest.Count == 0) return null;
            stareTime += deltaT;
            timeTillNextTransfer -= deltaT;
            bool shouldTransfer = false;
            if (timeTillNextTransfer < 0)
            {
                ResetTransferInterval();
                shouldTransfer = true;
            }
            if (shouldTransfer && TriggerTransfer())
            {
                stareTime = 0;
                var i = GetNextInterestPoint();
                currentTarget = i;
                return i?.Value.target;
            }
            else if (currentTarget != null)
            {
                return currentTarget.Value.Value.target;
            }
            else
            {
                return null;
            }
        }

        public void AddPoi(string key, PointOfInterest val)
        {
            pointOfInterest.Add(key, val);
        }

        public bool RemovePoi(string key)
        {
            if (pointOfInterest.Remove(key))
            {
                if (currentTarget?.Key == key)
                {
                    currentTarget = GetNextInterestPoint();
                }
                return true;
            }
            else { return false; }

        }

        public void CleatPoi()
        {
            currentTarget = null;
            pointOfInterest.Clear();
        }
    }

    static class InterestedTransform
    {
        private static readonly FieldInfo jiggleBoneSubject = AccessTools.Field(typeof(jiggleBone), "trSub");

        public static Transform LeftBreast(TBody body) => (Transform)jiggleBoneSubject.GetValue(body.jbMuneL);
        public static Transform RightBreast(TBody body) => (Transform)jiggleBoneSubject.GetValue(body.jbMuneR);

        public static Transform Genitalia(TBody body) => body.Pelvis.transform;
        public static Transform Face(TBody body) => body.Face.center_tr;
        public static Transform LeftEye(TBody body) => body.trsEyeL;
        public static Transform RightEye(TBody body) => body.trsEyeR;

        public static Transform? Camera() => GameMain.Instance.VRMode ?
            GameMain.Instance.MainCamera?.transform : GameMain.Instance.OvrMgr?.EyeAnchor;
    }

    public static class PoiHook
    {
        private static Dictionary<TBody, PointOfInterestManager> poiRepo = new Dictionary<TBody, PointOfInterestManager>();

        [HarmonyPrefix, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static void SetPoi(TBody __instance)
        {
            if (!poiRepo.TryGetValue(__instance, out var poi)) return;
            var transform = poi.Tick(Time.deltaTime);
            if (transform != null)
            {
                __instance.trsLookTarget = transform;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "EyeToTarget")]
        public static void AddTarget(Maid __instance)
        {
            if (!poiRepo.TryGetValue(__instance.body0, out var poi)) return;
            poi.AddPoi("", new PointOfInterest(__instance.body0.trsLookTarget, 1));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "Initialize")]
        public static void InitEyeInfo(Maid __instance)
        {
            if (__instance.boMAN) return;
            if (Plugin.Instance == null) return;
            poiRepo.Add(
                __instance.body0,
                new PointOfInterestManager(
                    Plugin.Instance.PoiConfig,
                    __instance.body0.trsHead,
                    new Rect(-40, -30, 80, 55)));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "OnDestroy")]
        public static void UnInitEyeInfo(TBody __instance)
        {
            poiRepo.Remove(__instance);
        }
    }
}
