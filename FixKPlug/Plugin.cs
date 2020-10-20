using BepInEx;
using HarmonyLib;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace Karenia.FixKPlug
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixkplug";
        public const string projectName = "FixKPlug";
        public const string version = "0.1.0";

        public Plugin()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("FixKPlug");
            harmony = new Harmony(id);
            //harmony.PatchAll(typeof(Hook));
            Instance = this;
        }


        public void Start()
        {
            StartCoroutine(DeferredPatch());
        }

        public static Plugin Instance { get; private set; }

        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }

        private readonly Harmony harmony;


        public IEnumerator DeferredPatch()
        {
            Logger.LogInfo("Deferring patch to Title scene loaded...");
            yield return new WaitUntil(() =>
            {
                return SceneManager.GetActiveScene().name == "Title";
            });
            Logger.LogInfo("Patching kPlug...");
            harmony.PatchAll(typeof(DeferredHook));
        }
    }

    public static class Hook
    {
        //[HarmonyPatch(typeof(StrayTech.ThirdPersonCamera),)]
        //[HarmonyPostfix, HarmonyPatch(typeof(ChaFile), "LoadFile", typeof(BinaryReader), typeof(bool), typeof(bool))]
        //public static void TrackLoadFile()
        //{
        //    var trace = new StackTrace();
        //    Plugin.Instance.Logger.LogInfo(trace);
        //}
    }

    public static class DeferredHook
    {
        private static Dictionary<ChaFileControl, string> pathDic = new Dictionary<ChaFileControl, string>();

        [HarmonyPrefix, HarmonyPatch(typeof(kPlug.CmpH.InviteUI), "Start")]
        public static void GetCardListPrefix()
        {
            hackingKPlug = true;
            ExtensibleSaveFormat.ExtendedSave.LoadEventsEnabled = false;
        }


        [HarmonyPostfix, HarmonyPatch(typeof(kPlug.CmpH.InviteUI), "Start")]
        public static void GetCardListPostfix()
        {
            hackingKPlug = false;
            ExtensibleSaveFormat.ExtendedSave.LoadEventsEnabled = true;
        }


        [HarmonyPrefix, HarmonyPatch(typeof(kPlug.Tools.ToolCreate), "AddGirlFromCard")]
        public static void HackAddGirlFromCard(ref ChaFileControl fileCtrl)
        {
            if (pathDic.TryGetValue(fileCtrl, out var fullPath))
            {
                fileCtrl = new ChaFileControl();
                fileCtrl.LoadCharaFile(fullPath);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(kPlug.Tools.ToolCreate), "AddBuddyFromCard")]
        public static void HackAddBuddyFromCard(ref ChaFileControl fileCtrl)
        {
            if (pathDic.TryGetValue(fileCtrl, out var fullPath))
            {
                fileCtrl = new ChaFileControl();
                fileCtrl.LoadCharaFile(fullPath);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(kPlug.CmpBase.Dealer), "ExitingH")]
        public static void CleanupCardCache()
        {
            pathDic.Clear();
        }

        private static bool hackingKPlug = false;

        [HarmonyPrefix, HarmonyPatch(typeof(ChaFileControl), "LoadCharaFile", typeof(string), typeof(byte), typeof(bool), typeof(bool))]
        public static void SaveFullPath(ChaFileControl __instance, string filename)
        {
            if (hackingKPlug)
            {
                pathDic.Add(__instance, filename);
            }
        }
    }
}
