using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Karenia.FixEyeMov.Core;
using BepInEx;

namespace Karenia.FixEyeMov.Com3d2
{
    [BepInPlugin(id, projectName, version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string id = "cc.karenia.fixeyemov.com3d2";
        public const string projectName = "FixationalEyeMovements.COM3D2";
        public const string version = "0.1.0";

        public Plugin()
        {
            Instance = this;
            EyeConfig = new EyeMovementConfig();
            EyeConfig.Bind(base.Config);

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
