using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;

namespace Karenia.SocialForces.KK
{
    [BepInPlugin(id, "Social Forces", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        private const string id = "cc.karenia.socialforces.kk";

        public void Start()
        {
            Instance = this;
            var harmony = new HarmonyLib.Harmony(id);
            harmony.PatchAll(typeof(Hook));
            //Instrumentation.ApplyInstrumentation(harmony);
        }

        public new BepInEx.Logging.ManualLogSource Logger { get => base.Logger; }

        public static Plugin Instance;
    }
}
