using HarmonyLib;
using Illusion.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Karenia.SocialForces.KK
{
    using MapMove = NodeCanvas.Tasks.Actions.MapMove;
    using NPC = ActionGame.Chara.NPC;
    using CharaBase = ActionGame.Chara.Base;
    using Player = ActionGame.Chara.Player;

    /// <summary>
    /// Modifies the original game code to adapt to the algorithm.
    /// </summary>
    internal static class Hook
    {
        private static readonly FieldInfo mapMove_npc = AccessTools.Field(typeof(MapMove), "npc");
        private static readonly FieldInfo mapMove_routeIDList = AccessTools.Field(typeof(MapMove), "routeIDList");
        private static readonly MethodInfo mapMove_arrive = AccessTools.Method(typeof(MapMove), "Arrive");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapMove), "NaviMeshCalclater")]
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
            MapMove __instance)
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();
            var npc = (NPC)mapMove_npc.GetValue(__instance);

            /// Original position of this NPC character
            var originalPos = npc.position;
            var targetPos = npc.agent.steeringTarget;
            var destination = npc.agent.destination;

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

            var otherPedestrians = charaList
                .Where(chara => chara.mapNo == npc.mapNo || chara is Player)
                .Select(x => new Neighbor<CharaBase>()
                {
                    id = x,
                    position = x.position,
                    velocity = (x as NPC)?.agent?.velocity ?? Vector3.zero
                });

            var attractions = playerEnumerable.Select(
                x => new Neighbor<CharaBase>()
                {
                    id = player,
                    position = player.position,
                    velocity = player.agent.velocity
                });

            var self = new Neighbor<CharaBase>()
            {
                id = npc,
                position = originalPos,
                velocity = speedVector
            };

            var forces = Algorithm.CalculateAcceleration(
                self,
                targetPos,
                desiredSpeed,
                otherPedestrians,
                (x, p) => x != p ? 1 : 0,
                closestEdge,
                attractions,
                (x, p) => x.heroine.relation >= 2 ? .7f : 0,
                destination == targetPos && npc.mapNo == __instance.targetMapNo.value,
                true
            );

            var force = forces.Sum();
            var newSpeedVector = speedVector + force * deltaT;

            if (newSpeedVector.magnitude > maxSpeed) newSpeedVector *= (maxSpeed / newSpeedVector.magnitude);

            var newPosition = originalPos + newSpeedVector * deltaT;
            newPosition.y = npc.agent.nextPosition.y;
            npc.position = newPosition;
            if (newSpeedVector.x != 0 && newSpeedVector.z != 0)
                npc.transform.forward = new Vector3(newSpeedVector.x, 0, newSpeedVector.z);
            npc.agent.velocity = newSpeedVector;
            npc.agent.nextPosition = npc.calcPosition;

            npc.AgentVelocityMoveAnimeUpdate();

            //VisualizeForces(forces, npc);

            //Plugin.Instance.Logger.LogInfo($"NPC: {npc.charaData.Name}\n\tspeed: {speedVector}->{newSpeedVector}\n\tforce: {force}\n\tposition: {originalPos}->{npc.position} (target {targetPos})");

            var routeIdList = (List<int>)mapMove_routeIDList.GetValue(__instance);
            if (Vector3.Distance(npc.position, destination) < 0.3f
                || (!routeIdList.IsNullOrEmpty<int>() && routeIdList[0] == npc.hitGateID))
            {
                mapMove_arrive.Invoke(__instance, null);
            }

            startTime.Stop();
            Plugin.Instance.Logger.LogInfo($"elapsed: {startTime.Elapsed.TotalMilliseconds}ms");
        }

        private static Dictionary<ActionGame.Chara.Base, DebugLineRenderer> repo = new Dictionary<ActionGame.Chara.Base, DebugLineRenderer>();
        private static Player player;
        private static Player[] playerEnumerable;

        private static void VisualizeForces(Forces forces, NPC npc)
        {
            if (!repo.TryGetValue(npc, out var renderer)) return;
            Plugin.Instance.Logger.LogInfo($"npc {npc.charaData.Name}");
            var v3up = new Vector3(0, 0.1f, 0);
            forces.destination += npc.position;
            forces.repulsion += npc.position;
            forces.attraction += npc.position;
            forces.border += npc.position;
            var positionUp = npc.position + v3up;

            renderer.SetPoints("destination", new Vector3[] { positionUp, forces.destination });
            renderer.SetPoints("repulsion", new Vector3[] { positionUp, forces.repulsion });
            renderer.SetPoints("attraction", new Vector3[] { positionUp, forces.attraction });
            renderer.SetPoints("border", new Vector3[] { positionUp, forces.border });
            renderer.SetPoints("total", new Vector3[] { positionUp, forces.Sum() + npc.position });
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NPC), "Awake")]
        public static void OnNPCAwake(NPC __instance)
        {
            //__instance.agent.updatePosition = false;

            //var lineRenderer = __instance.gameObject.GetOrAddComponent<DebugLineRenderer>();
            //lineRenderer.SetupChild("destination", Color.blue, 0.1f);
            //lineRenderer.SetupChild("repulsion", Color.red, 0.1f);
            //lineRenderer.SetupChild("attraction", Color.yellow, 0.1f);
            //lineRenderer.SetupChild("border", Color.cyan, 0.1f);
            //lineRenderer.SetupChild("total", Color.white, 0.1f);
            //repo.Add(__instance, lineRenderer);
        }

        private static IEnumerable<ActionGame.Chara.Base> charaList;

        private static Func<ActionGame.Cycle, ActionScene> cycleScene =
            AccessTools.MethodDelegate<Func<ActionGame.Cycle, ActionScene>>(
                AccessTools.Method(typeof(ActionGame.Cycle), "get_actScene"));

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionGame.Cycle), "Next")]
        public static void SetupList(ActionGame.Cycle __instance)
        {
            var scene = cycleScene(__instance);
            if (scene.fixChara != null)
            {
                charaList = scene.npcList.OfType<CharaBase>()
                    .Concat(new CharaBase[] { scene.Player, scene.fixChara });
            }
            else
            {
                charaList = scene.npcList.OfType<CharaBase>()
                    .Concat(new CharaBase[] { scene.Player });
            }
            player = scene.Player;
            playerEnumerable = new Player[] { player };
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionScene), "OnDestroy")]
        public static void ClearRepo()
        {
            repo.Clear();
            player = null;
            playerEnumerable = null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapMove), "OnExecute")]
        public static void DisableAgent(MapMove __instance)
        {
            var npc = mapMove_npc.GetValue(__instance) as NPC;
            npc.agent.updatePosition = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapMove), "OnStop")]
        public static void EnableAgent(MapMove __instance)
        {
            var npc = mapMove_npc.GetValue(__instance) as NPC;
            npc.agent.updatePosition = true;
        }
    }

    internal class Instrumentation
    {
        private static MethodInfo npcField = AccessTools.Method(typeof(ActionGame.Chara.AI), "get_npc");
        private static FieldInfo npcFieldMapMove = AccessTools.Field(typeof(MapMove), "npc");

        private static void LogStacktrace(string message)
        {
            Plugin.Instance.Logger.LogInfo($"{message}\n{new System.Diagnostics.StackTrace()}");
        }

        private static void Log(string message)
        {
            Plugin.Instance.Logger.LogInfo(message);
        }

        private static NPC GetNPC(object o)
        {
            if (o is NPC npc)
            {
                return npc;
            }
            if (o is ActionGame.Chara.AI ai)
            {
                return ((NPC)npcField.Invoke(ai, new object[0]));
            }
            else if (o is MapMove mm)
            {
                return ((NPC)npcFieldMapMove.GetValue(o));
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

            public Snapshot(NPC npc)
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

        public GameObject SetupChild(string key, Color color, float width)
        {
            var child = new GameObject($"DebugLineRenderer:Child:{key}");
            child.transform.SetParent(lineChild.transform, false);

            lineRenderers.Add(key, child);

            var lineRenderer = child.AddComponent<LineRenderer>();
            lineRenderer.numCapVertices = 5;
            lineRenderer.numCornerVertices = 7;
            lineRenderer.useWorldSpace = true;

            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.sortingOrder = 1;

            return child;
        }

        public void SetPoints(string key, ICollection<Vector3> points)
        {
            if (!lineRenderers.TryGetValue(key, out var obj))
            {
                throw new ArgumentOutOfRangeException(nameof(key), $"No such renderer as {key}");
            }

            var lineRenderer = obj.GetComponent<LineRenderer>();
            lineRenderer.positionCount = points.Count;
            var iterator = points.GetEnumerator();

            for (var i = 0; i < points.Count; i++)
            {
                iterator.MoveNext();
                lineRenderer.SetPosition(i, iterator.Current);
            }

            ;
        }

        public void Destroy()
        {
            foreach (var child in lineRenderers)
                GameObject.Destroy(child.Value);
            GameObject.Destroy(lineChild);
        }
    }
}
