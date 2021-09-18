using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using HarmonyLib;
using ActionGame;

namespace GetTapped.KK
{
    /// <summary>
    /// Helper class for various stuff related to main action game.
    /// </summary>
    public class ActionGameHelper : MonoBehaviour
    {
        public void OnGUI()
        {
        }
    }

    /// <summary>
    /// Container class for hooking various methods.
    /// </summary>
    public static class ActionGameHook
    {
        private static bool walking = false;
        private static bool crouching = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionInput), "get_isWalk")]
        public static void AdditionalWalkKey(ref bool __result)
        {
            __result |= walking;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionInput), "get_isCrouch")]
        public static void AdditionalCrouchKey(ref bool __result)
        {
            __result |= crouching;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ActionGame.Chara.Mover.Main), "Update")]
        public static IEnumerable<CodeInstruction> HookMoveDirection(IEnumerable<CodeInstruction> code)
        {
            var matcher = new CodeMatcher(code);
            return matcher.InstructionEnumeration();
        }

        public static Vector2 MoveDirection()
        {
            throw new NotImplementedException();
        }
    }
}
