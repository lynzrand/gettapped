using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;
using static Karenia.FixEyeMov.Com3d2.CharaExt;
using System.Reflection.Emit;

namespace Karenia.FixEyeMov.Com3d2.Poi
{
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

        public Transform? Transform(Transform baseTransform);
    }

    public class TransformTarget : IPoiTarget
    {
        public Transform transform;

        public TransformTarget(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform), "Transform must not be null inside a TransformTarget");
            this.transform = transform;
        }

        public string? Name => $"{transform.name}#{transform.GetHashCode()}";

        public bool CanGuideTargetSearching { get; set; } = true;

        public Transform? Transform(Transform _) => transform;

        public Vector3 WorldPosition(Transform _) => transform.position;
    }

    public class CameraTarget : IPoiTarget
    {
        public string? Name => "Camera";

        public bool CanGuideTargetSearching => false;

        public Transform? Transform(Transform _) => Camera.main.transform;

        public Vector3 WorldPosition(Transform _) => Camera.main.transform.position;
    }

    public class OffsetTarget : IPoiTarget
    {
        private TBody body;

        public OffsetTarget(TBody body)
        {
            this.body = body;
        }

        public string? Name => "OffsetTarget";

        public bool CanGuideTargetSearching => true;

        public Transform? Transform(Transform baseTransform)
        {
            return null;
        }

        public Vector3 WorldPosition(Transform baseTransform)
        {
            return baseTransform.TransformPoint(this.body.offsetLookTarget);
        }
    }

    public class PointOfInterest
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
    }

    public class PoiConfig
    {
        private const string section = "PointOfInterest";

        public PoiConfig(ConfigFile config, Func<Transform, Vector3, float>? targetWeightFactorFunction = null, ManualLogSource? logger = null)
        {
            TransferCheckInterval = config.Bind(section, nameof(TransferCheckInterval), 0.4f, "Average interval (seconds) between transfers");
            TransferCheckStdDev = config.Bind(section, nameof(TransferCheckStdDev), 0.07f, "Standard deviation between transfer checks");
            NearClip = config.Bind(section, nameof(NearClip), 0.1f, "POI Near clip distance");
            FarClip = config.Bind(section, nameof(FarClip), 50f, "POI Far clip distance");
            DebugRay = config.Bind(section, nameof(DebugRay), false, "Show debug ray and stuff");
            Enabled = config.Bind(section, nameof(Enabled), true, "Enable POI system (changing requires scene reload)");
            if (targetWeightFactorFunction != null) TargetWeightFactorFunction = targetWeightFactorFunction;
            Logger = logger;
        }

        public ConfigEntry<float> TransferCheckInterval { get; private set; }
        public ConfigEntry<float> TransferCheckStdDev { get; private set; }
        public ConfigEntry<float> NearClip { get; private set; }
        public ConfigEntry<float> FarClip { get; private set; }
        public ConfigEntry<bool> DebugRay { get; private set; }
        public ConfigEntry<bool> Enabled { get; private set; }

        public Func<Transform, Vector3, float> TargetWeightFactorFunction { get; set; } = (_, _) => 1.0f;
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
            var nearSquared = config.NearClip.Value * config.NearClip.Value;
            var farSquared = config.FarClip.Value * config.FarClip.Value;

            Vector3 viewDirection = DirectionVector();

            foreach (var kv in pointOfInterest)
            {
                Vector3 targetPosition = kv.Value.target.WorldPosition(baseTransform);
                Vector3 targetDirection = targetPosition - baseTransform.position;

                var targetDistSquared = targetDirection.sqrMagnitude;
                if (targetDistSquared >= farSquared || targetDistSquared <= nearSquared) continue;

                var viewAngle = Quaternion.FromToRotation(viewDirection, targetDirection);

                if (viewport.Contains(viewAngle) || kv.Value.alwaysPresent)
                {
                    Plugin.Instance?.Logger.LogDebug($"{GetHashCode()}: Candidates: {kv.Key}:{kv.Value.target.Name}");

                    var weight = kv.Value.weight * config.TargetWeightFactorFunction(baseTransform, targetPosition);
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

            if (MainTarget != null && MainTarget.CanGuideTargetSearching)
            {
                viewDirection = MainTarget.WorldPosition(baseTransform) - baseTransform.position;
            }
            else
            {
                viewDirection = baseTransform.up;
            }

            return viewDirection;
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
                if (i.HasValue)
                    Plugin.Instance?.Logger.LogInfo($"{GetHashCode()}: Transfered target to {i.Value.Key}:{i.Value.Value.target.Name}");
                else
                    Plugin.Instance?.Logger.LogInfo($"{GetHashCode()}: Cleared target");
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

    internal static class InterestedTransform
    {
        private static readonly FieldInfo jiggleBoneSubject = AccessTools.Field(typeof(jiggleBone), "m_trSub");

        public static Transform? LeftBreast(TBody body) => (Transform)jiggleBoneSubject.GetValue(body.jbMuneL);

        public static Transform? RightBreast(TBody body) => (Transform)jiggleBoneSubject.GetValue(body.jbMuneR);

        public static Transform? Genitalia(TBody body) => body.Pelvis.transform;

        public static Transform? Face(TBody body) => body.trsHead;

        public static Transform? LeftEye(TBody body) => body.trsEyeL;

        public static Transform? RightEye(TBody body) => body.trsEyeR;

        public static Transform? LeftHand(TBody body) => body.m_trHandHitL;

        public static Transform? RightHand(TBody body) => body.m_trHandHitR;

        public static Transform? Camera() => GameMain.Instance.VRMode ?
            UnityEngine.Camera.main.transform : GameMain.Instance.OvrMgr.EyeAnchor;
    }

    internal class DebugLineRenderer : MonoBehaviour
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

    public static class PoiHook
    {
        private static Dictionary<TBody, PointOfInterestManager> poiRepo = new Dictionary<TBody, PointOfInterestManager>();

        //[HarmonyPrefix, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static Vector3? SetPoi(TBody __instance)
        {
            if (!Plugin.Instance?.PoiConfig.Enabled.Value ?? false) return null;
            if (!poiRepo.TryGetValue(__instance, out var poi)) return null;
            if (__instance.boLockHeadAndEye) return null;
            var target = poi.Tick(Time.deltaTime);

            // TODO: I don't want to move head here, but only the eyes.

            if (Plugin.Instance!.PoiConfig.DebugRay.Value)
            {
                var lineRenderer = __instance.gameObject.GetComponent<DebugLineRenderer>();
                if (lineRenderer != null)
                {
                    var list = new List<Vector3>();
                    list.Add(poi.baseTransform.position);
                    foreach (var kv in poi.PointOfInterest)
                    {
                        if (kv.Value.target is CameraTarget) continue;
                        list.Add(kv.Value.target.WorldPosition(poi.baseTransform));
                        list.Add(poi.baseTransform.position);
                        //Debug.DrawRay(poi.baseTransform.position, kv.Value.target.position - poi.baseTransform.position, Color.blue);
                    }

                    lineRenderer.SetPoints("candidate", list, Color.blue, 0.003f);
                    var enabled = Plugin.Instance?.PoiConfig.DebugRay.Value ?? false;
                    lineRenderer.enabled = enabled;

                    if (target != null)
                    {
                        lineRenderer.SetPoints("target", new List<Vector3> { poi.baseTransform.position, target.WorldPosition(poi.baseTransform) }, Color.yellow, 0.005f);
                    }

                    lineRenderer.SetPoints("up", new List<Vector3> { poi.baseTransform.position, poi.baseTransform.position + poi.DirectionVector().normalized }, Color.red, 0.002f);
                }
            }

            if (target != null)
            {
                //__instance.boHeadToCam = true;
                //__instance.trsLookTarget = target.Transform(poi.baseTransform);
                return target.WorldPosition(poi.baseTransform);
            }
            else
            {
                //__instance.boHeadToCam = false;
                //__instance.trsLookTarget = poi.MainTarget?.Transform(poi.baseTransform);
                return null;
            }
        }

        public static Vector3 ChangeEyeTarget(TBody __instance, Vector3 targetPosition)
        {
            Console.WriteLine("detour");
            var target = SetPoi(__instance);
            if (target != null)
            {
                return target.Value;
            }
            else
            {
                return targetPosition;
            }
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static IEnumerable<CodeInstruction> AddEyeControlling(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();

            /*
                if (!this.boMAN && this.trsEyeL != null && this.trsEyeR != null)
	            {
                    // <--- We want to insert code here
		            Vector3 vector4 = a - this.trsHead.position;
                    ...
             *
             * in IL code, it corresponding to...
             *
                423	05B7	ldarg.0
                424	05B8	ldfld	bool TBody::boMAN
                425	05BD	brtrue	572 (07C1) ret
                426	05C2	ldarg.0
                427	05C3	ldfld	class [UnityEngine]UnityEngine.Transform TBody::trsEyeL
                428	05C8	ldnull
                429	05C9	call	bool [UnityEngine]UnityEngine.Object::op_Inequality(class [UnityEngine]UnityEngine.Object, class [UnityEngine]UnityEngine.Object)
                430	05CE	brfalse	572 (07C1) ret
                431	05D3	ldarg.0
                432	05D4	ldfld	class [UnityEngine]UnityEngine.Transform TBody::trsEyeR
                433	05D9	ldnull
                434	05DA	call	bool [UnityEngine]UnityEngine.Object::op_Inequality(class [UnityEngine]UnityEngine.Object, class [UnityEngine]UnityEngine.Object)
                435	05DF	brfalse	572 (07C1) ret
            ----------------------------------------> HERE!
                436	05E4	ldloc.1
                437	05E5	ldarg.0
                438	05E6	ldfld	class [UnityEngine]UnityEngine.Transform TBody::trsHead
                439	05EB	callvirt	instance valuetype [UnityEngine]UnityEngine.Vector3 [UnityEngine]UnityEngine.Transform::get_position()
                440	05F0	call	valuetype [UnityEngine]UnityEngine.Vector3 [UnityEngine]UnityEngine.Vector3::op_Subtraction(valuetype [UnityEngine]UnityEngine.Vector3, valuetype [UnityEngine]UnityEngine.Vector3)
                441	05F5	stloc.s	V_11 (11)
             *
             * So we need to find the pattern from 436 through 441.
             */
            var targetField_trsHead = AccessTools.Field(typeof(TBody), "trsHead");
            var targetMethod_getPosition = AccessTools.Method(typeof(Transform), "get_position");
            var targetMethod_getRotation = AccessTools.Method(typeof(Transform), "get_rotation");
            var targetMethod_vecSubtract = AccessTools.Method(typeof(Vector3), "op_Subtraction", parameters: new Type[] { typeof(Vector3), typeof(Vector3) });
            var targetMethod_quaternionInverse = AccessTools.Method(typeof(Quaternion), "Inverse");
            var targetMethod_quaternionMultiply = AccessTools.Method(typeof(Quaternion), "op_Multiply", parameters: new Type[] { typeof(Quaternion), typeof(Vector3) });

            // Find insertion target
            int target = -1;
            for (int i = 0; i < code.Count - 13; i++)
            {
                if (code[i].IsLdloc()
                    && code[i + 1].IsLdarg(0)
                    && code[i + 2].LoadsField(targetField_trsHead)
                    && code[i + 3].Calls(targetMethod_getPosition)
                    && code[i + 4].Calls(targetMethod_vecSubtract)
                    && code[i + 5].IsStloc()
                    && code[i + 6].IsLdarg(0)
                    && code[i + 7].LoadsField(targetField_trsHead)
                    && code[i + 8].Calls(targetMethod_getRotation)
                    && code[i + 9].Calls(targetMethod_quaternionInverse)
                    && code[i + 10].IsLdloc()
                    && code[i + 11].Calls(targetMethod_quaternionMultiply)
                    && code[i + 12].IsStloc())
                {
                    target = i; break;
                }
            }

            // Make sure this function isn't patched before, since HarmonyTranspiler
            // re-patches the function every time it is changed
            var detourFunction = AccessTools.Method(typeof(PoiHook), nameof(ChangeEyeTarget));
            bool alreadyPatched = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(detourFunction))
                {
                    alreadyPatched = true; break;
                }
            }

            // patch code
            if (target > 0 && !alreadyPatched)
            {
                var localVariableTarget = code[target];
                var insertedInstructions = new CodeInstruction[]
                {
                    // TBody __instance
                    new CodeInstruction(OpCodes.Ldarg_0),
                    // ref Vector3 targetPosition
                    localVariableTarget,
                    // call ChangeEyeTarget(__instance, targetPosition)
                    new CodeInstruction(OpCodes.Call, detourFunction),
                    // restore target
                    StoreLoc(localVariableTarget)
                };
                code.InsertRange(target, insertedInstructions);

                var result = new StringBuilder("After patch inspect:");
                result.AppendLine();
                foreach (var inst in code)
                {
                    result.AppendLine(inst.ToString());
                }
                Plugin.Instance?.Logger.LogInfo(result.ToString());
            }

            return code;
        }

        private static CodeInstruction StoreLoc(CodeInstruction code)
        {
            if (code.opcode == OpCodes.Ldloc_0)
            {
                return new CodeInstruction(OpCodes.Stloc_0);
            }
            else if (code.opcode == OpCodes.Ldloc_1)
            {
                return new CodeInstruction(OpCodes.Stloc_1);
            }
            else if (code.opcode == OpCodes.Ldloc_2)
            {
                return new CodeInstruction(OpCodes.Stloc_2);
            }
            else if (code.opcode == OpCodes.Ldloc_3)
            {
                return new CodeInstruction(OpCodes.Stloc_3);
            }
            else if (code.opcode == OpCodes.Ldloc_S)
            {
                return new CodeInstruction(OpCodes.Stloc_S, code.operand);
            }
            else if (code.opcode == OpCodes.Ldloc)
            {
                return new CodeInstruction(OpCodes.Stloc, code.operand);
            }
            else
            {
                throw new Exception("Code is not LdLoc");
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "EyeToTarget")]
        public static void AddTarget(Maid __instance, Maid f_maidTarget, string f_strBoneName)
        {
            if (!poiRepo.TryGetValue(__instance.body0, out var poi)) return;
            // Chances are the null target is intentional, as seen in the source code
            if (__instance.body0.trsLookTarget == null)
            {
                RemoveTarget(__instance);
            }
            else
            {
                TransformTarget target = new TransformTarget(__instance.body0.trsLookTarget);
                // main targets should be replacing each other, thus the same name
                poi.AddPoi("target", new PointOfInterest(target, 2, true));
                poi.MainTarget = target;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "EyeToCamera")]
        public static void AddCameraTarget(Maid __instance)
        {
            if (!poiRepo.TryGetValue(__instance.body0, out var poi)) return;
            CameraTarget target = new CameraTarget();
            // main targets should be replacing each other, thus the same name
            poi.AddPoi("target", new PointOfInterest(target, 2, true));
            poi.MainTarget = target;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Maid), "EyeToPosition")]
        public static void RemoveTarget(Maid __instance)
        {
            if (!poiRepo.TryGetValue(__instance.body0, out var poi)) return;
            poi.RemovePoi("target");
            poi.MainTarget = null;
        }

        /// <summary>
        /// Recalculate POI transforms around the scene.
        ///
        /// <para>
        ///     This is a naive method that recalculates the whole scene without any kind of memory
        ///     or something similar. Since most scenes are under 5 characters, this method should
        ///     not cause perceivable lag.
        /// </para>
        /// </summary>
        /// <returns>A collection of transforms to be added</returns>
        public static ICollection<PointOfInterest> RecalculateSceneTargets(
            float faceWeight = 2f, float handWeight = 1f, float genitalWeight = 2f, float breastWeight = 1f,
            bool includeGenital = false, bool includeBreast = false)
        {
            var targetTransforms = new HashSet<PointOfInterest>();
            var charMgr = GameMain.Instance?.CharacterMgr;
            if (charMgr != null)
            {
                var maidCount = charMgr.GetMaidCount();
                for (var i = 0; i < maidCount; i++)
                {
                    var maid = charMgr.GetMaid(i);

                    // Not sure why we need this, but apparently the game has it in many functions
                    if (maid == null) continue;
                    TBody body0 = maid.body0;
                    if (!maid.Visible || !body0.isLoadedBody) continue;

                    var breastVisible = body0.boVisible_NIP;
                    var genitalVisible = !body0.GetSlotVisible(TBody.SlotID.panz);

                    var face = InterestedTransform.Face(body0);
                    if (face != null) targetTransforms.Add(PointOfInterest.Transform(face, faceWeight));

                    var leftHand = InterestedTransform.LeftHand(body0);
                    if (leftHand != null) targetTransforms.Add(PointOfInterest.Transform(leftHand, handWeight));

                    var rightHand = InterestedTransform.RightHand(body0);
                    if (rightHand != null) targetTransforms.Add(PointOfInterest.Transform(rightHand, handWeight));

                    if (includeBreast && breastVisible)
                    {
                        var leftBreast = InterestedTransform.LeftBreast(body0);
                        if (leftBreast != null) targetTransforms.Add(PointOfInterest.Transform(leftBreast, breastWeight));

                        var rightBreast = InterestedTransform.RightBreast(body0);
                        if (rightBreast != null) targetTransforms.Add(PointOfInterest.Transform(rightBreast, breastWeight));
                    }
                    if (includeGenital && genitalVisible)
                    {
                        var genital = InterestedTransform.Genitalia(body0);
                        if (genital != null) targetTransforms.Add(PointOfInterest.Transform(genital, genitalWeight));
                    }
                }

                var manCount = charMgr.GetManCount();
                for (var i = 0; i < manCount; i++)
                {
                    var man = charMgr.GetMan(i);

                    // Not sure why we need this, but apparently the game has it in many functions
                    if (man == null) continue;
                    var body0 = man.body0;

                    if (!man.Visible || !body0.isLoadedBody) continue;

                    var genitalVisible = body0.GetSlotVisible(TBody.SlotID.underhair);

                    var face = InterestedTransform.Face(body0);
                    if (face != null) targetTransforms.Add(PointOfInterest.Transform(face, faceWeight));

                    var leftHand = InterestedTransform.LeftHand(body0);
                    if (leftHand != null) targetTransforms.Add(PointOfInterest.Transform(leftHand, handWeight));

                    var rightHand = InterestedTransform.RightHand(body0);
                    if (rightHand != null) targetTransforms.Add(PointOfInterest.Transform(rightHand, handWeight));

                    if (includeGenital && genitalVisible)
                    {
                        var genital = InterestedTransform.Genitalia(body0);
                        if (genital != null) targetTransforms.Add(PointOfInterest.Transform(genital, genitalWeight));
                    }
                }
            }
            return targetTransforms;
        }

        public static void CleanSceneTargets(PointOfInterestManager poi)
        {
            poi.RemovePoiPrefix("scene:");
        }

        public static void AddSceneTargets(PointOfInterestManager poi, ICollection<PointOfInterest> targets)
        {
            foreach (var target in targets)
            {
                var key = $"scene:{target.target?.Name ?? "NULL?"}";
                poi.AddPoi(key, target);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterMgr), "Deactivate")]
        [HarmonyPatch(typeof(CharacterMgr), "Activate")]
        [HarmonyPatch(typeof(CharacterMgr), "SetActive")]
        [HarmonyPatch(typeof(CharacterMgr), "CharaVisible")]
        [HarmonyPatch(typeof(CharacterMgr), "PresetSet")]
        [HarmonyPatch(typeof(CharacterMgr), "AddProp")]
        [HarmonyPatch(typeof(CharacterMgr), "SetProp")]
        [HarmonyPatch(typeof(CharacterMgr), "ResetProp")]
        [HarmonyPatch(typeof(CharacterMgr), "SetChinkoVisible")]
        [HarmonyPatch(typeof(BaseKagManager), "TagItemMaskMode")]
        [HarmonyPatch(typeof(BaseKagManager), "TagAddAllOffset")]
        [HarmonyPatch(typeof(BaseKagManager), "TagAddPrefabChara")]
        [HarmonyPatch(typeof(BaseKagManager), "TagConCharaActivate1stRanking")]
        [HarmonyPatch(typeof(BaseKagManager), "TagCompatibilityCharaActivate")]
        [HarmonyPatch(typeof(BaseKagManager), "TagConCharaActivateLeader")]
        [HarmonyPatch(typeof(BaseKagManager), "Initialize")]
        [HarmonyPatch(typeof(TBody), "UnInit")]
        [HarmonyPatch(typeof(TBody), "SetMask")]
        public static void RegenerateTargetsInKagScene()
        {
            foreach (var kv in poiRepo)
            {
                CleanSceneTargets(kv.Value);
            }
            var targets = RecalculateSceneTargets(includeBreast: true, includeGenital: true);
            foreach (var kv in poiRepo)
            {
                AddSceneTargets(kv.Value, targets);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "LoadBody_R")]
        public static void InitPoiInfo(TBody __instance)
        {
            if (__instance.boMAN || __instance.trsHead == null) return;
            if (Plugin.Instance == null) return;
            Plugin.Instance.Logger.LogDebug($"Initialized POI at {__instance.maid.name}#{__instance.maid.GetHashCode()}");
            Plugin.Instance.Logger.LogDebug($"Head transform: {__instance.trsHead.name}@{__instance.trsHead.position}");
            poiRepo.Add(
                __instance,
                new PointOfInterestManager(
                    Plugin.Instance.PoiConfig,
                    __instance.trsHead,
                    new PointOfInterestManager.ViewAngle { xNeg = -60, xPos = 60, zNeg = -50, zPos = 40 }));
            __instance.gameObject.AddComponent<DebugLineRenderer>();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "UnInit")]
        public static void DeInitPoiInfo(TBody __instance)
        {
            poiRepo.Remove(__instance);
        }
    }
}

