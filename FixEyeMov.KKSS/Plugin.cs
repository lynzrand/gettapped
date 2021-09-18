using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using Karenia.FixEyeMov.Core;

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
            EyeConfig = new EyeMovementConfig(base.Config);

            Logger = BepInEx.Logging.Logger.CreateLogSource("FixEyeMov");
            var harmony = new Harmony(id);

            harmony.PatchAll(typeof(EyeMovementHook));
        }

        public static Plugin Instance { get; private set; }
        public EyeMovementConfig EyeConfig { get; private set; }
        public new BepInEx.Logging.ManualLogSource Logger { get; private set; }
    }

    public static class EyeMovementHook
    { }
}

