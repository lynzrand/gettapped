using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;
using Karenia.FixEyeMov.Core.Poi;

namespace Karenia.FixEyeMov.Com3d2.Poi
{
    public class OffsetTarget : IPoiTarget
    {
        private TBody body;

        public OffsetTarget(TBody body)
        {
            this.body = body;
        }

        public string? Name => "OffsetTarget";

        public bool CanGuideTargetSearching => true;

        public bool ShouldBeSelected(Transform baseTransform, Vector3 directionVector) => true;

        public Vector3 WorldPosition(Transform baseTransform)
        {
            return baseTransform.TransformPoint(this.body.offsetLookTarget);
        }

        public bool IsStillValid => this.body != null;
    }

    internal static class InterestedTransform
    {
        private static readonly FieldInfo jiggleBoneSubject = AccessTools.Field(typeof(jiggleBone), "m_trSub");
        private static readonly PropertyInfo dbMuneLProperty = AccessTools.Property(typeof(TBody), "dbMuneL");
        private static readonly PropertyInfo dbMuneRProperty = AccessTools.Property(typeof(TBody), "dbMuneR");
        private static readonly FieldInfo dynamicMuneBoneSubject = null;

        static InterestedTransform()
        {
            if (dbMuneLProperty != null)
            {
                dynamicMuneBoneSubject = AccessTools.Field(dbMuneLProperty.PropertyType, "MuneSub");
            }
        }

        public static Transform? LeftBreast(TBody body)
        {
            if (!body.maid.IsCrcBody)
            {
                return (Transform)jiggleBoneSubject.GetValue(body.jbMuneL);
            }
            else
            {
                var dbMuneL = dbMuneLProperty.GetValue(body, null);
                if (dbMuneL != null)
                {
                    return (Transform)dynamicMuneBoneSubject.GetValue(dbMuneL);
                }
            }

            return null;
        }

        public static Transform? RightBreast(TBody body)
        {
            if (!body.maid.IsCrcBody)
            {
                return (Transform)jiggleBoneSubject.GetValue(body.jbMuneR);
            }
            else
            {
                var dbMuneR = dbMuneRProperty.GetValue(body, null);
                if(dbMuneR != null)
                {
                    return (Transform)dynamicMuneBoneSubject.GetValue(dbMuneR);
                }
            }

            return null;
        }

        public static Transform? Genitalia(TBody body) => body.Pelvis.transform;

        public static Transform? Face(TBody body) => body.trsHead;

        public static Transform? LeftEye(TBody body) => body.trsEyeL;

        public static Transform? RightEye(TBody body) => body.trsEyeR;

        private static readonly FieldInfo tBodyHandL = AccessTools.Field(typeof(TBody), "HandL");

        public static Transform LeftHand(TBody body) => (Transform)tBodyHandL.GetValue(body);

        private static readonly FieldInfo tBodyHandR = AccessTools.Field(typeof(TBody), "HandR");

        public static Transform? RightHand(TBody body) => (Transform)tBodyHandR.GetValue(body);

        public static Transform? Camera() => GameMain.Instance.VRMode ?
            UnityEngine.Camera.main.transform : GameMain.Instance.OvrMgr.EyeAnchor;
    }

    public static class PoiHook
    {
        private static Dictionary<TBody, PointOfInterestManager> poiRepo = new Dictionary<TBody, PointOfInterestManager>();
        private static bool enableByScene = true;

        public static void Init()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (oldScene, newScene) =>
            {
                if (newScene.name.Contains("Dance"))
                    enableByScene = false;
                else
                    enableByScene = true;
            };
        }

        //[HarmonyPrefix, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static Vector3? SetPoi(TBody __instance)
        {
            if (!Plugin.Instance?.PoiConfig.Enabled.Value ?? false) return null;
            if (!enableByScene) return null;
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
                        if (!kv.Value.target.IsStillValid) continue;
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

        // This method patches the original `MoveHeadAndEye` method.
        [HarmonyTranspiler, HarmonyPatch(typeof(TBody), "MoveHeadAndEye")]
        public static IEnumerable<CodeInstruction> AddEyeControlling(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();

            /*
                if (!this.boMAN && this.trsEyeL != null && this.trsEyeR != null)
	            {
                    // <--- We want to insert code here...
                    // ...and change the value of this variable `a`
                    //              --v--
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
                436	05E4	ldloc.1   // <- We need to change this variable
                437	05E5	ldarg.0
                438	05E6	ldfld	class [UnityEngine]UnityEngine.Transform TBody::trsHead
                439	05EB	callvirt	instance valuetype [UnityEngine]UnityEngine.Vector3 [UnityEngine]UnityEngine.Transform::get_position()
                440	05F0	call	valuetype [UnityEngine]UnityEngine.Vector3 [UnityEngine]UnityEngine.Vector3::op_Subtraction(valuetype [UnityEngine]UnityEngine.Vector3, valuetype [UnityEngine]UnityEngine.Vector3)
                441	05F5	stloc.s	V_11 (11)
             *
             * So we need to find the pattern from 436 through 441.
             */
            var targetField_trsHead = AccessTools.Field(typeof(TBody), "trsHead");
            var targetField_trsNeck = AccessTools.Field(typeof(TBody), "trsNeck");
            var targetMethod_getPosition = AccessTools.Method(typeof(Transform), "get_position");
            var targetMethod_getRotation = AccessTools.Method(typeof(Transform), "get_rotation");
            var targetMethod_vecSubtract = AccessTools.Method(typeof(Vector3), "op_Subtraction", parameters: new Type[] { typeof(Vector3), typeof(Vector3) });
            var targetMethod_quaternionInverse = AccessTools.Method(typeof(Quaternion), "Inverse");
            var targetMethod_quaternionMultiply = AccessTools.Method(typeof(Quaternion), "op_Multiply", parameters: new Type[] { typeof(Quaternion), typeof(Vector3) });
            //var targetMethod_setLocalRotation = AccessTools.Method(typeof(Transform), "set_localRotation");
            //var targetMethod_quaternionSlerp = AccessTools.Method(typeof(Quaternion), "Slerp");
            //var targetMethod_coss = AccessTools.Method(typeof(UTY), "COSS");
            //var targetField_headToCam = AccessTools.Field(typeof(TBody), "HeadToCamPer");

            // Find insertion target
            int target = -1;
            for (int i = 6; i < code.Count; i++)
            {
                if (
                    //code[i].IsLdloc()
                    //&& code[i + 1].IsLdarg(0)
                    //&& code[i + 2].LoadsField(targetField_trsHead)
                    //&& code[i + 3].Calls(targetMethod_getPosition)
                    //&& code[i + 4].Calls(targetMethod_vecSubtract)
                    //&& code[i + 5].IsStloc()
                    //&& code[i + 6].IsLdarg(0)
                    //&& code[i + 7].LoadsField(targetField_trsHead)
                    //&& code[i + 8].Calls(targetMethod_getRotation)
                    //&& code[i + 9].Calls(targetMethod_quaternionInverse)
                    //&& code[i + 10].IsLdloc()
                    //&& code[i + 11].Calls(targetMethod_quaternionMultiply)
                    //&& code[i + 12].IsStloc()
                    code[i - 6].IsLdloc()
                    && code[i - 5].IsLdarg(0)
                    && code[i - 4].LoadsField(targetField_trsNeck)
                    && code[i - 3].Calls(targetMethod_getPosition)
                    && code[i - 2].Calls(targetMethod_vecSubtract)
                    && code[i - 1].IsStloc()
                    )
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
                var localVariableTarget = code[target - 6];
                var insertedInstructions = new CodeInstruction[]
                {
                    // TBody __instance
                    new CodeInstruction(OpCodes.Ldarg_0),
                    // Vector3 targetPosition
                    localVariableTarget.Clone(),
                    // call ChangeEyeTarget(__instance, targetPosition)
                    new CodeInstruction(OpCodes.Call, detourFunction),
                    // restore target
                    StoreLoc(localVariableTarget)
                };
                code.InsertRange(target, insertedInstructions);
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

        /// <summary>
        /// This callback is used to patch MaidVoicePitch.
        /// </summary>
        /// <param name="harmony"></param>
        public static void PatchMaidVoicePitch(Harmony harmony)
        {
            ManualLogSource? logger = Plugin.Instance?.Logger;

            try
            {
                 var assembly = System.Reflection.Assembly.Load("COM3D2.MaidVoicePitch.Plugin");
            }
            catch (System.IO.FileNotFoundException e)
            {
                logger?.LogInfo("MaidVoicePitch not found. Exiting.");
                return;
            }

            var ty = AccessTools.TypeByName("TBodyMoveHeadAndEye");
            // MaidVoicePitch does not exist. Phew!
            if (ty == null)
            {
                logger?.LogInfo("MaidVoicePitch not found. Exiting.");
                return;
            }
            var targeMethod = AccessTools.Method(ty, "newTbodyMoveHeadAndEyeCallback2");
            var otherTargeMethod = AccessTools.Method(ty, "originalTbodyMoveHeadAndEyeCallback2");
            if (targeMethod == null || otherTargeMethod == null)
            {
                logger?.LogWarning("MaidVoicePitch type found but not method. Exiting.");
                var methods = AccessTools.GetDeclaredMethods(ty);
                logger?.LogInfo("Methods found:");
                foreach (var m in methods)
                {
                    logger?.LogInfo(m.FullDescription());
                }
                return;
            }

            logger?.LogInfo("Patching the naughty MaidVoicePitch!");

            harmony.Patch(targeMethod, transpiler: new HarmonyMethod(AccessTools.Method(typeof(PoiHook), nameof(MaidVoicePitchTranspiler))));

            harmony.Patch(otherTargeMethod, transpiler: new HarmonyMethod(AccessTools.Method(typeof(PoiHook), nameof(MaidVoicePitchTranspiler))));

            harmony.Patch(targeMethod, transpiler: new HarmonyMethod(AccessTools.Method(typeof(PoiHook), nameof(MaidVoicePitch_PatchEyeTrackLimit))));
        }

        public static IEnumerable<CodeInstruction> MaidVoicePitchTranspiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            /*
            Inside New callback:
                46	0079	ldarg.0
                47	007A	ldarg.1
                48	007B	ldarg.2
                49	007C	ldarg.3
                50	007D	ldarg.s	thatHeadEulerAngleG (4)
                51	007F	ldarg.s	thatEyeEulerAngle (5)
                52	0081	ldloc.3                         <- We also need to edit this local variable
                53	0082	call	instance void TBodyMoveHeadAndEye::newMoveHead(...)
            --------> INSERT CODE HERE!

            Inside Old callback:
                43	0072	ldarg.0
                44	0073	ldarg.1
                45	0074	ldarg.2
                46	0075	ldarg.3
                47	0076	ldarg.s	thatEyeEulerAngle (4)
                48	0078	ldloc.2                         <- We also need to edit this local variable
                49	0079	call	instance void TBodyMoveHeadAndEye::originalMoveHead(...)
            ---------> INSERT CODE HERE!
             */

            int target = -1;
            var ty = AccessTools.TypeByName("TBodyMoveHeadAndEye");
            var targetMethod_newMoveHead = AccessTools.Method(ty, "newMoveHead");
            var targetMethod_originalMoveHead = AccessTools.Method(ty, "originalMoveHead");
            for (int i = 2; i < code.Count; i++)
            {
                if (code[i - 2].IsLdloc() && (
                        code[i - 1].Calls(targetMethod_newMoveHead) ||
                        code[i - 1].Calls(targetMethod_originalMoveHead)
                    )
                )
                {
                    target = i;
                    break;
                }
            }

            var detourFunction = AccessTools.Method(typeof(PoiHook), nameof(MaidVoicePitch_ChangeEyeTarget));
            bool alreadyPatched = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(detourFunction))
                {
                    alreadyPatched = true; break;
                }
            }

            if (target > 0 && !alreadyPatched)
            {
                var localVariableTarget = code[target - 2];
                code.InsertRange(target, new CodeInstruction[]
                {
                    // TBody __instance
                    new CodeInstruction(OpCodes.Ldarg_1),
                    // Vector3 targetPosition
                    localVariableTarget.Clone(),
                    // call ChangeEyeTarget(__instance, targetPosition)
                    new CodeInstruction(OpCodes.Call, detourFunction),
                    // restore target
                    StoreLoc(localVariableTarget)
                });
            }

            return code;
        }

        public static IEnumerable<CodeInstruction> MaidVoicePitch_PatchEyeTrackLimit(IEnumerable<CodeInstruction> code)
        {
            return new CodeMatcher(code)
                .MatchForward(true, new CodeMatch(OpCodes.Ldstr, "EYE_TRACK.inside"))
                .Advance(1)
                .SetAndAdvance(OpCodes.Ldc_R4, 40f)
                .MatchForward(true, new CodeMatch(OpCodes.Ldstr, "EYE_TRACK.outside"))
                .Advance(1)
                .SetAndAdvance(OpCodes.Ldc_R4, 40f)
                .InstructionEnumeration();
        }

        public static Vector3 MaidVoicePitch_ChangeEyeTarget(TBody __instance, Vector3 targetPosition)
        {
            try
            {
                var target = SetPoi(__instance);
                if (target != null)
                {
                    __instance.boEyeToCam = true;
                    __instance.boChkEye = true;
                    return target.Value;
                }
                else
                {
                    return targetPosition;
                }
            }
            catch(Exception e)
            {
                Plugin.Instance?.Logger.LogError(e);
                return targetPosition;
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
            float faceWeight = 2f,
            float handWeight = 0.3f,
            float genitalWeight = 2f,
            float breastWeight = 1f,
            float breastInactiveWeight = 0.4f,
            bool includeGenital = false,
            bool includeBreast = false)
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

                    var leftBreast = InterestedTransform.LeftBreast(body0);
                    var rightBreast = InterestedTransform.RightBreast(body0);
                    if (!includeBreast || !breastVisible)
                    {
                        breastWeight = breastInactiveWeight;
                    }
                    if (leftBreast != null) targetTransforms.Add(PointOfInterest.Transform(leftBreast, breastWeight));
                    if (rightBreast != null) targetTransforms.Add(PointOfInterest.Transform(rightBreast, breastWeight));

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
            if (poi.baseTransform == null) return;
            foreach (var usedTarget in targets)
            {
                var target = usedTarget;
                var key = $"scene:{target.target.Name ?? "NULL?"}";
                if (target.target is TransformTarget transformTarget)
                {
                    if (transformTarget?.transform == null) continue;

                    target = target.Clone();
                    if (transformTarget.transform.IsChildOf(poi.baseTransform))
                    { target.weight /= 2; }
                    else
                    {
                        target.weight *= 1.2f;
                    }
                }
                poi.AddPoi(key, target);
            }
        }

        /// <summary>
        /// Patch whichever method exists for updating targets in POI mode.
        /// </summary>
        /// <param name="harmony"></param>
        public static void PatchKagSceneTags(Harmony harmony)
        {
            var targets = new MethodInfo[]
            {
                AccessTools.Method(typeof(CharacterMgr), "Deactivate",new Type[]{ typeof(int), typeof(bool) }),
                AccessTools.Method(typeof(CharacterMgr), "Activate"),
                AccessTools.Method(typeof(CharacterMgr), "SetActive"),
                AccessTools.Method(typeof(CharacterMgr), "CharaVisible"),
                //AccessTools.Method(typeof(CharacterMgr), "AddProp"),
                //AccessTools.Method(typeof(CharacterMgr), "SetProp"),
                //AccessTools.Method(typeof(CharacterMgr), "ResetProp"),
                //AccessTools.Method(typeof(CharacterMgr), "SetChinkoVisible"),
                AccessTools.Method(typeof(CharacterMgr), "PresetSet", new Type[]{typeof(Maid), typeof(CharacterMgr.Preset), typeof(bool) }),
                AccessTools.Method(typeof(CharacterMgr), "PresetSet", new Type[]{typeof(Maid), typeof(CharacterMgr.Preset) }),
                AccessTools.Method(typeof(BaseKagManager), "TagItemMaskMode"),
                AccessTools.Method(typeof(BaseKagManager), "TagAddAllOffset"),
                AccessTools.Method(typeof(BaseKagManager), "TagAddPrefabChara"),
                AccessTools.Method(typeof(BaseKagManager), "TagConCharaActivate1stRanking"),
                AccessTools.Method(typeof(BaseKagManager), "TagCompatibilityCharaActivate"),
                AccessTools.Method(typeof(BaseKagManager), "TagConCharaActivateLeader"),
                AccessTools.Method(typeof(BaseKagManager), "Initialize"),
                AccessTools.Method(typeof(TBody), "UnInit"),
                AccessTools.Method(typeof(TBody), "SetMask", new Type[]{typeof(TBody.SlotID), typeof(bool) }),
                AccessTools.Method(typeof(TBody), "SetMask", new Type[]{typeof(MPN), typeof(bool) }),
            };

            var regenerateKagFunction = AccessTools.Method(typeof(PoiHook), nameof(RegenerateTargetsInKagScene));

            // Just patch whichever function is valid.
            foreach (var target in targets)
            {
                if (target is null) continue;
                harmony.Patch(target, postfix: new HarmonyMethod(regenerateKagFunction));
            }
        }

        /// <summary>
        /// This method is responsible for regenerating targets every time the scene restarts.
        /// <para>
        ///     The algorithm used here is of <c>O(n^2)</c> complexity, but since the game only
        ///     supports 40 characters at most, and 99% of the scenes are made of less than 5
        ///     characters, this should be fine.
        /// </para>
        /// <para>
        ///     One way this function could cause lag is when all points of interest from the previous
        ///     iteration is discarded. Maybe a lock-free object pool is needed if that happens. For
        ///     now, we'll just discard those targets.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// Patch whichever method exists for initializing POI information.
        /// </summary>
        /// <param name="harmony"></param>
        public static void PatchInitPoiInfo(Harmony harmony)
        {
            var targets = new MethodInfo[]
            {
                AccessTools.Method(typeof(TBody), "LoadBody_R", new Type[]{typeof(string), typeof(Maid), typeof(int), typeof(bool) }),
                AccessTools.Method(typeof(TBody), "LoadBody_R", new Type[]{typeof(string), typeof(Maid) }),
            };

            var regenerateKagFunction = AccessTools.Method(typeof(PoiHook), nameof(InitPoiInfo));

            // Just patch whichever function is valid.
            foreach (var target in targets)
            {
                if (target is null) continue;
                harmony.Patch(target, postfix: new HarmonyMethod(regenerateKagFunction));
            }
        }

        public static void InitPoiInfo(TBody __instance)
        {
            if (!enableByScene) return;
            if (!Plugin.Instance?.PoiConfig.Enabled.Value ?? false) return;
            if (__instance.boMAN || __instance.trsHead == null) return;
            if (Plugin.Instance == null) return;
            Plugin.Instance.Logger.LogDebug($"Initialized POI at {__instance.maid.name}#{__instance.maid.GetHashCode()}");
            Plugin.Instance.Logger.LogDebug($"Head transform: {__instance.trsHead.name}@{__instance.trsHead.position}");
            poiRepo.Add(
                __instance,
                new PointOfInterestManager(
                    Plugin.Instance.PoiConfig,
                    __instance.trsHead,
                    new PointOfInterestManager.ViewAngle { xNeg = -40, xPos = 40, zNeg = -30, zPos = 25 }));
            __instance.gameObject.AddComponent<DebugLineRenderer>();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TBody), "UnInit")]
        public static void DeInitPoiInfo(TBody __instance)
        {
            poiRepo.Remove(__instance);
        }
    }
}

