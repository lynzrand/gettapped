using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Karenia.SocialForces.KK
{
    /// <summary>
    /// Modifies the original game code to adapt to the algorithm.
    /// </summary>
    internal static class Hook
    {
        private static readonly MethodInfo npcField = AccessTools.Method(typeof(ActionGame.Chara.AI), "get_npc");

        private static readonly FieldInfo npcFieldMapMove = AccessTools.Field(typeof(NodeCanvas.Tasks.Actions.MapMove), "npc");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NodeCanvas.Tasks.Actions.MapMove), "NaviMeshCalclater")]
        /// <summary>
        /// Replaces the original moving algorithm. Calculates the target route according to
        /// the Social Forces algorithm, and applies to the result.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="points"></param>
        /// <param name="timer"></param>
        /// <param name="__result"></param>
        /// <returns></returns>
        public static void MoveChara(
            NodeCanvas.Tasks.Actions.MapMove __instance,
            ref bool __result)
        {
            var npc = (ActionGame.Chara.NPC)npcFieldMapMove.GetValue(__instance);

            /// Original position of this NPC character
            var originalPos = npc.calcPosition;
            var desiredPos = npc.agent.nextPosition;

            /// Target speed of this NPC character
            var desiredSpeed = npc.AI.speeder.GetSpeed(npc.AI.speeder.mode);
            var speed = npc.agent.speed;
            var speedVector = npc.agent.velocity;
            var maxSpeed = 1.3f * speed;

            /// Time passed during this frame
            var deltaT = Time.deltaTime;

            var hasClosestEdge = UnityEngine.AI.NavMesh.FindClosestEdge(
                originalPos,
                out var hit,
                UnityEngine.AI.NavMesh.AllAreas);

            Vector3? closestEdge = null;
            if (hasClosestEdge) closestEdge = hit.position;

            var otherPedestrians = npc.otherPeopleList.Select(x => new Pedestrian()
            {
                id = x.GetInstanceID(),
                position = x.position,
                velocity = (x as ActionGame.Chara.NPC)?.agent?.velocity ?? Vector3.zero
            });

            var attractions = (new Pedestrian[0]).Select(x => x);

            var forces = Algorithm.CalculateAcceleration(
                npc.GetInstanceID(),
                originalPos,
                desiredPos - originalPos,
                speedVector,
                desiredSpeed,
                otherPedestrians,
                (x, p) => x == p ? 0 : 1,
                closestEdge,
                attractions,
                (x, p) => 0,
                true
                );

            var force = forces.Sum();
            var newSpeedVector = speedVector + force * deltaT;
            if (newSpeedVector.magnitude > maxSpeed) newSpeedVector *= (maxSpeed / newSpeedVector.magnitude);
            npc.calcPosition = originalPos + newSpeedVector;
            npc.position = npc.calcPosition;
            npc.transform.forward = new Vector3(newSpeedVector.x, 0, newSpeedVector.z);
            npc.agent.velocity = newSpeedVector;
            npc.agent.nextPosition = npc.calcPosition;

            VisualizeForces(forces, npc);

            Plugin.Instance.Logger.LogInfo($"NPC: {npc.charaData.Name}\n\tspeed: {speedVector}->{newSpeedVector}\n\tforce: {force}\n\tposition: {originalPos}->{npc.position}");
        }

        private static Dictionary<ActionGame.Chara.Base, DebugLineRenderer> repo = new Dictionary<ActionGame.Chara.Base, DebugLineRenderer>();

        private static void VisualizeForces(Forces forces, ActionGame.Chara.NPC npc)
        {
            if (!repo.TryGetValue(npc, out var renderer)) return;
            Plugin.Instance.Logger.LogInfo($"npc {npc.charaData.Name}");
            var v3up = new Vector3(0, 0.1f, 0);

            renderer.SetPoints("destination", new Vector3[] { v3up, forces.destination }, Color.blue, 0.5f);
            renderer.SetPoints("repulsion", new Vector3[] { v3up, forces.repulsion }, Color.red, 0.5f);
            renderer.SetPoints("attraction", new Vector3[] { v3up, forces.attraction }, Color.yellow, 0.5f);
            renderer.SetPoints("border", new Vector3[] { v3up, forces.border }, Color.cyan, 0.5f);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionGame.Chara.NPC), "Awake")]
        public static void OnNPCAwake(ActionGame.Chara.NPC __instance)
        {
            __instance.agent.updatePosition = false;

            var lineRenderer = __instance.gameObject.GetOrAddComponent<DebugLineRenderer>();
            lineRenderer.SetupChild("destination");
            lineRenderer.SetupChild("repulsion");
            lineRenderer.SetupChild("attraction");
            lineRenderer.SetupChild("border");
            repo.Add(__instance, lineRenderer);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionScene), "OnDestroy")]
        public static void ClearRepo()
        {
            repo.Clear();
        }
    }

    internal class Instrumentation
    {
        private static MethodInfo npcField = AccessTools.Method(typeof(ActionGame.Chara.AI), "get_npc");
        private static FieldInfo npcFieldMapMove = AccessTools.Field(typeof(NodeCanvas.Tasks.Actions.MapMove), "npc");

        private static void LogStacktrace(string message)
        {
            Plugin.Instance.Logger.LogInfo($"{message}\n{new System.Diagnostics.StackTrace()}");
        }

        private static void Log(string message)
        {
            Plugin.Instance.Logger.LogInfo(message);
        }

        private static ActionGame.Chara.NPC GetNPC(object o)
        {
            if (o is ActionGame.Chara.NPC npc)
            {
                return npc;
            }
            if (o is ActionGame.Chara.AI ai)
            {
                return ((ActionGame.Chara.NPC)npcField.Invoke(ai, new object[0]));
            }
            else if (o is NodeCanvas.Tasks.Actions.MapMove mm)
            {
                return ((ActionGame.Chara.NPC)npcFieldMapMove.GetValue(o));
            }
            else
            {
                return null;
            }
        }

        public static void ApplyInstrumentation(Harmony harmony)
        {
            var instrumentList = new MethodInfo[]
            {
                AccessTools.Method("NodeCanvas.Tasks.Actions.MapMove:NaviMeshCalclater"),
                AccessTools.Method("NodeCanvas.Tasks.Actions.MapMove:Arrive"),
                AccessTools.Method("ActionGame.Chara.NPC:AgentVelocityMoveAnimeUpdate"),
                AccessTools.Method("ActionGame.Chara.Mover.NPCMover:MoveState"),
                AccessTools.Method("UnityEngine.AI.NavMeshAgent:Warp"),
                AccessTools.Method("UnityEngine.AI.NavMeshAgent:SetPath"),
                AccessTools.Method("ActionGame.Chara.Mover.NPCMover:MoveUpdate"),
                AccessTools.Method("Illusion.Utils.Math:MinDistanceRouteIndex")
            };
            var prefix = AccessTools.Method(typeof(Instrumentation), "InstrumentBefore");
            var postfix = AccessTools.Method(typeof(Instrumentation), "InstrumentAfter");

            foreach (var v in instrumentList)
            {
                Log($"Instrumented: {v?.FullDescription() ?? "null?!"}");
                if (v == null) continue;
                harmony.Patch(v,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
            }
        }

        public static void InstrumentBefore(object __instance, ref Snapshot __state)
        {
            var npc = GetNPC(__instance);
            if (npc == null) return;
            __state = new Snapshot(npc);
        }

        public static void InstrumentAfter(object __instance, ref Snapshot __state, MethodBase __originalMethod)
        {
            var npc = GetNPC(__instance);
            if (npc == null) return;
            var newSnapshot = new Snapshot(npc);
            newSnapshot.DoDiff(__state, __originalMethod.FullDescription());
        }

        public struct Snapshot
        {
            public Vector3 position;
            public Vector3 calcPosition;

            public Snapshot(ActionGame.Chara.NPC npc)
            {
                this.position = npc.position;
                this.calcPosition = npc.calcPosition;
            }

            public void DoDiff(Snapshot other, string environment)
            {
                bool different = false;
                if (other.position != this.position)
                {
                    different = true;
                    Log($"Position difference: {this.position} <- {other.position}");
                }
                if (other.calcPosition != this.calcPosition)
                {
                    different = true;
                    Log($"CalcPosition difference: {this.calcPosition} <- {other.calcPosition}");
                }
                if (!different)
                {
                    Log($"AT {environment}: No difference");
                }
                else
                {
                    Log($"AT {environment}:\n" +
                    new System.Diagnostics.StackTrace().ToString());
                }
            }
        }
    }

    internal class DebugLineRenderer : MonoBehaviour
    {
        private GameObject lineChild;
        private Dictionary<string, GameObject> lineRenderers = new Dictionary<string, GameObject>();

        public void Awake()
        {
            lineChild = new GameObject("DebugLineRenderer-LineChild");
            lineChild.transform.SetParent(gameObject.transform, false);
        }

        public GameObject SetupChild(string key)
        {
            var child = new GameObject($"DebugLineRenderer:Child:{key}");
            child.transform.SetParent(lineChild.transform, false);

            lineRenderers.Add(key, child);

            var lineRenderer = child.AddComponent<LineRenderer>();
            lineRenderer.numCapVertices = 5;
            lineRenderer.numCornerVertices = 7;
            lineRenderer.useWorldSpace = false;

            return child;
        }

        public void SetPoints(string key, ICollection<Vector3> points, Color color, float width)
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
            var iterator = points.GetEnumerator();
            for (var i = 0; i < points.Count; i++)
            {
                iterator.MoveNext();
                lineRenderer.SetPosition(i, iterator.Current);
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
