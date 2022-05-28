using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;

namespace Karenia.FixEyeMov.Core.Poi
{
    public static class PoiStaticConfig
    {
        public static ManualLogSource? Logger = null;
        public static bool DebugLog = false;
    }

    /*
     * Point of interest is an alternative for the naive "stare at camera" feature.
     *
     * Using this module characters can look around different interested targets,
     * making them more life-like.
     *
     * ---
     *
     * Note:
     *
     * I tried to avoid excess allocations throughout the project, but allocation
     * is explicitly needed when coding around interfaces and stuff so do expect some
     * GC lag when using this plug-in.
     */

    /// <summary>
    /// A target position for POI use.
    /// </summary>
    public interface IPoiTarget
    {
        public string? Name { get; }

        public bool CanGuideTargetSearching { get; }

        public Vector3 WorldPosition(Transform baseTransform);

        public bool ShouldBeSelected(Transform baseTransform, Vector3 directionVector);

        public bool IsStillValid { get;  }
    }

    public class TransformTarget : IPoiTarget
    {
        public Transform transform { get; private set; }
        GameObject go;

        public TransformTarget(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform), "Transform must not be null inside a TransformTarget");
            this.transform = transform;
            this.go = transform.gameObject;
        }

        public string? Name => $"{transform.name}#{transform.GetHashCode()}";

        public bool CanGuideTargetSearching { get; set; } = true;

        public bool ShouldBeSelected(Transform baseTransform, Vector3 directionVector)
        {
            var ray = new Ray(baseTransform.position, directionVector);
            Physics.Raycast(ray, out var hitInfo, float.PositiveInfinity);
            var hit = hitInfo.transform;

            PoiStaticConfig.Logger?.LogDebug($"{Name}: Raycast target is {hit}");

            // Avoid occlusion
            if (hit == null || hit.IsChildOf(transform) || transform.IsChildOf(hit)) return true;
            else return false;
        }

        public Vector3 WorldPosition(Transform _) => transform.position;

        public bool IsStillValid => go != null;
    }

    public class CameraTarget : IPoiTarget
    {
        public string? Name => "Camera";

        public bool CanGuideTargetSearching => false;

        public bool ShouldBeSelected(Transform baseTransform, Vector3 directionVector) => true;

        public Vector3 WorldPosition(Transform _) => Camera.main.transform.position;

        public bool IsStillValid => true;
    }

    public class PointOfInterest : ICloneable
    {
        public PointOfInterest(IPoiTarget target, float weight, bool alwaysPresent = false)
        {
            this.target = target;
            this.weight = weight;
            this.alwaysPresent = alwaysPresent;
        }

        /// <summary>
        /// The target object's transform.
        /// </summary>
        public IPoiTarget target;

        /// <summary>
        /// A measurement of interest in this target. A weight of 1.0 means for transfer probability
        /// would be 50% if triggered at 1s after staring at this target.
        /// </summary>
        public float weight;

        /// <summary>
        /// Whether the target should be viewed even if it is out of view range
        /// </summary>
        public bool alwaysPresent;

        public static PointOfInterest Camera(float weight) => new PointOfInterest(new CameraTarget(), weight);

        public static PointOfInterest Transform(Transform transform, float weight) => new PointOfInterest(new TransformTarget(transform), weight);

        public PointOfInterest Clone() => new PointOfInterest(target, weight, alwaysPresent);

        object ICloneable.Clone() => Clone();
    }

    public class PoiConfig
    {
        private const string section = "PointOfInterest";

        public PoiConfig(ConfigFile config, Func<Transform, Vector3, Vector3?, float>? targetWeightFactorFunction = null, ManualLogSource? logger = null)
        {
            TransferCheckInterval = config.Bind(section, nameof(TransferCheckInterval), 0.3f, "Average interval (seconds) between transfers");
            TransferCheckStdDev = config.Bind(section, nameof(TransferCheckStdDev), 0.07f, "Standard deviation between transfer checks");
            NearClip = config.Bind(section, nameof(NearClip), 0.1f, "POI Near clip distance");
            FarClip = config.Bind(section, nameof(FarClip), 50f, "POI Far clip distance");
            DebugRay = config.Bind(section, nameof(DebugRay), false, "Show debug ray and stuff");
            DebugLog = config.Bind(section, nameof(DebugLog), false, "Log debug information in console");
            Enabled = config.Bind(section, nameof(Enabled), false, "Enable POI system (changing requires scene reload)");
            if (targetWeightFactorFunction != null) TargetWeightFactorFunction = targetWeightFactorFunction;
            Logger = logger;
        }

        public ConfigEntry<float> TransferCheckInterval { get; private set; }
        public ConfigEntry<float> TransferCheckStdDev { get; private set; }
        public ConfigEntry<float> NearClip { get; private set; }
        public ConfigEntry<float> FarClip { get; private set; }
        public ConfigEntry<bool> DebugRay { get; private set; }
        public ConfigEntry<bool> Enabled { get; private set; }
        public ConfigEntry<bool> DebugLog { get; private set; }

        /// <summary>
        /// Target weight function in the form of (baseTransform, targetPosition, originalTargetPosition) -> factor
        /// </summary>
        public Func<Transform, Vector3, Vector3?, float> TargetWeightFactorFunction { get; set; } = (_, _, _) => 1.0f;

        public ManualLogSource? Logger { get; }
    }

    public class PointOfInterestManager
    {
        private readonly PoiConfig config;

        public PointOfInterestManager(PoiConfig config, Transform baseTransform, ViewAngle viewport)
        {
            this.config = config;
            this.baseTransform = baseTransform;
            this.viewport = viewport;
        }

        private readonly SortedDictionary<string, PointOfInterest> pointOfInterest = new SortedDictionary<string, PointOfInterest>();
        private KeyValuePair<string, PointOfInterest>? currentTarget = null;

        private float stareTime = 0;
        private float timeTillNextTransfer = 0;

        public Transform baseTransform;
        public ViewAngle viewport;

        public SortedDictionary<string, PointOfInterest> PointOfInterest => pointOfInterest;
        public KeyValuePair<string, PointOfInterest>? CurrentTarget => currentTarget;

        public IPoiTarget? MainTarget { get; set; }

        private struct PoiListItem
        {
            public KeyValuePair<string, PointOfInterest> kv;
            public float realWeight;
        }

        public struct ViewAngle
        {
            public float xPos;
            public float xNeg;
            public float zPos;
            public float zNeg;

            public bool Contains(float x, float z)
            {
                return x > xNeg && x < xPos && z > zNeg && z < zPos;
            }

            public bool Contains(Quaternion rot)
            {
                rot.ToAngleAxis(out var angle, out var axis);
                var axisXZ = Mathf.Atan2(axis.z, axis.x);
                var xAngle = (axisXZ > 90 && axisXZ <= 270) ? xNeg : xPos;
                var zAngle = (axisXZ > 180) ? zNeg : zPos;
                return angle <= Mathf.Sqrt(xAngle * angle + zAngle * zAngle);
            }
        }

        private KeyValuePair<string, PointOfInterest>? GetNextInterestPoint()
        {
            float totalSum = 0;
            var poiList = new List<PoiListItem>();

            Vector3 viewDirection = DirectionVector();

            foreach (var kv in pointOfInterest)
            {
                if (!kv.Value.target.IsStillValid) continue;

                Vector3 targetPosition = kv.Value.target.WorldPosition(baseTransform);

                if (ViewportContains(viewDirection, targetPosition) || kv.Value.alwaysPresent)
                {
                    if (PoiStaticConfig.DebugLog)
                    {
                        var shouldBeSelected = kv.Value.target.ShouldBeSelected(baseTransform, targetPosition - baseTransform.position);
                        PoiStaticConfig.Logger?.LogDebug($"{GetHashCode()}: Candidates: {kv.Key}:{kv.Value.target.Name}; ShouldBeSelected = {shouldBeSelected}");
                    }

                    var weight = kv.Value.weight * config.TargetWeightFactorFunction(baseTransform, targetPosition, currentTarget?.Value.target.WorldPosition(baseTransform));
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

        public Vector3 DirectionVector()
        {
            Vector3 viewDirection;

            if (MainTarget != null && MainTarget.IsStillValid && MainTarget.CanGuideTargetSearching)
            {
                viewDirection = MainTarget.WorldPosition(baseTransform) - baseTransform.position;
            }
            else
            {
                viewDirection = baseTransform.up;
            }

            return viewDirection;
        }

        public bool ViewportContains(Vector3 viewDirection, Vector3 targetPosition)
        {
            Vector3 targetDirection = targetPosition - baseTransform.position;
            var viewAngle = Quaternion.FromToRotation(viewDirection, targetDirection);

            var nearSquared = config.NearClip.Value * config.NearClip.Value;
            var farSquared = config.FarClip.Value * config.FarClip.Value;

            var targetDistSquared = targetDirection.sqrMagnitude;
            if (targetDistSquared >= farSquared || targetDistSquared <= nearSquared) return false;

            return viewport.Contains(viewAngle);
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

            return randomVar < x;
        }

        private void ResetTransferInterval()
        {
            timeTillNextTransfer = LogNormalRandom(config.TransferCheckInterval.Value, config.TransferCheckStdDev.Value);
        }

        public IPoiTarget? Tick(float deltaT)
        {
            if (pointOfInterest.Count == 0) return null;

            if (!currentTarget?.Value.target.IsStillValid ?? false)
            {
                currentTarget = null;
            }

            stareTime += deltaT;
            timeTillNextTransfer -= deltaT;
            bool shouldTransfer = false;
            if (timeTillNextTransfer < 0)
            {
                ResetTransferInterval();
                shouldTransfer = true;
            }

            if (currentTarget.HasValue)
            {
                shouldTransfer |= !ViewportContains(DirectionVector(), currentTarget.Value.Value.target.WorldPosition(baseTransform));
            }

            if (shouldTransfer && TriggerTransfer())
            {
                stareTime = 0;
                var i = GetNextInterestPoint();
                currentTarget = i;
                if (PoiStaticConfig.DebugLog)
                {
                    if (i.HasValue)
                        PoiStaticConfig.Logger?.LogInfo($"{GetHashCode()}: Transfered target to {i.Value.Key}:{i.Value.Value.target.Name}");
                    else
                        PoiStaticConfig.Logger?.LogInfo($"{GetHashCode()}: Cleared target");
                }
                return i?.Value.target;
            }
            else
            {
                return currentTarget?.Value.target;
            }
        }

        public void AddPoi(string key, PointOfInterest val)
        {
            config.Logger?.LogInfo($"Add POI: {key} -> {val.target.Name} {val.weight}");
            pointOfInterest.Remove(key);
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

        public int RemovePoiPrefix(string prefix)
        {
            var indicies = pointOfInterest.Keys.Where((k) => k.StartsWith(prefix)).ToList();
            foreach (var idx in indicies) pointOfInterest.Remove(idx);
            return indicies.Count;
        }

        public void CleatPoi()
        {
            currentTarget = null;
            pointOfInterest.Clear();
        }
    }

    public class DebugLineRenderer : MonoBehaviour
    {
        private GameObject? lineChild;
        private Dictionary<string, GameObject> lineRenderers = new Dictionary<string, GameObject>();

        public void Awake()
        {
            lineChild = new GameObject("DebugLineRenderer-LineChild");
            lineChild.transform.SetParent(gameObject.transform, false);
        }

        public GameObject SetupChild(string key)
        {
            var child = new GameObject($"DebugLineRenderer:Child:{key}");
            child.transform.SetParent(lineChild!.transform, false);

            lineRenderers.Add(key, child);

            var lineRenderer = child.AddComponent<LineRenderer>();
            lineRenderer.numCapVertices = 5;
            lineRenderer.numCornerVertices = 7;

            return child;
        }

        public void SetPoints(string key, IList<Vector3> points, Color color, float width)
        {
            if (!lineRenderers.TryGetValue(key, out var obj))
            {
                obj = SetupChild(key);
            }
            var lineRenderer = obj.GetComponent<LineRenderer>();

            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            lineRenderer.material = new Material(Shader.Find("Particles/Additive"));

            lineRenderer.positionCount = points.Count;
            for (var i = 0; i < points.Count; i++)
            {
                lineRenderer.SetPosition(i, points[i]);
            }
        }

        public void Destroy()
        {
            foreach (var child in lineRenderers)
                GameObject.Destroy(child.Value);
            GameObject.Destroy(lineChild);
        }
    }
}
