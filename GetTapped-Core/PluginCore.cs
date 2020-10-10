using System;
using HarmonyLib;
using BepInEx;
using UnityEngine;
using BepInEx.Configuration;
using UnityEngineInternal.Input;
using UnityEngine.Windows.Speech;

namespace Karenia.GetTapped.Core
{
    public static class Plugin
    {
        public static IGetTappedCore PluginCore { get; internal set; } = null;
    }

    [BepInPlugin(id, projectName, version)]
    class PluginCore : BaseUnityPlugin, IGetTappedCore
    {
        private const string projectName = "GetTapped-Core";
        private const string version = "0.1.0";
        private const string id = "cc.karenia.gettappedcore";

        public ConfigEntry<bool> pluginEnabled;
        public ConfigEntry<bool> singleTapTranslate;

        public PluginCore()
        {
            Plugin.PluginCore = this;
            BindConfig();
        }

        private void BindConfig()
        {
            pluginEnabled = Config.Bind(new ConfigDefinition("default", "Enabled"), true);
            singleTapTranslate = Config.Bind(new ConfigDefinition("default", "TranslateUsingSingleTap"), false);
        }

        public CameraMovement ParseTapIntoCameraMovement()
        {
            if (!pluginEnabled.Value) return CameraMovement.Zero();

            var cnt = Input.touchCount;

            if (singleTapTranslate.Value)
            {
                return cnt switch
                {
                    0 => CameraMovement.Zero(),
                    1 => SingleTouchToCameraTranslate(Input.GetTouch(0)),
                    2 => DoubleTouchToCameraRotate(Input.GetTouch(0), Input.GetTouch(1)),
                    _ => CameraMovement.Zero(),
                };
            }
            else
            {
                return cnt switch
                {
                    0 => CameraMovement.Zero(),
                    1 => SingleTouchToCamera(Input.GetTouch(0)),
                    2 => DoubleTouchToCamera(Input.GetTouch(0), Input.GetTouch(1)),
                    _ => CameraMovement.Zero(),
                };
            }
        }

        private CameraMovement SingleTouchToCamera(Touch tap1)
        {
            var delta = tap1.deltaPosition;
            return CameraMovement.Rotate(delta);
        }

        private CameraMovement DoubleTouchToCamera(Touch tap1, Touch tap2)
        {
            var centerDelta = (tap1.deltaPosition + tap2.deltaPosition) / 2;

            var tap2Last = tap2.position - tap2.deltaPosition;
            var tap1Last = tap1.position - tap1.deltaPosition;

            var vec = tap2.position - tap1.position;
            var vecLast = tap2Last - tap1Last;

            var thisMag = vec.sqrMagnitude;
            var lastMag = vecLast.sqrMagnitude;
            var zoomFactor = thisMag / lastMag;

            var angle = MathExt.SignedAngle(vecLast, vec);

            return new CameraMovement(new Vector3(0, 0, angle), centerDelta, zoomFactor);
        }

        private CameraMovement SingleTouchToCameraTranslate(Touch tap1)
        {
            var delta = tap1.deltaPosition;
            return CameraMovement.Translate(delta);
        }

        private CameraMovement DoubleTouchToCameraRotate(Touch tap1, Touch tap2)
        {
            var centerDelta = (tap1.deltaPosition + tap2.deltaPosition) / 2;

            var tap2Last = tap2.position - tap2.deltaPosition;
            var tap1Last = tap1.position - tap1.deltaPosition;

            var vec = tap2.position - tap1.position;
            var vecLast = tap2Last - tap1Last;

            var thisMag = vec.sqrMagnitude;
            var lastMag = vecLast.sqrMagnitude;
            var zoomFactor = thisMag / lastMag;

            var angle = MathExt.SignedAngle(vecLast, vec);

            return new CameraMovement(new Vector3(centerDelta.x, centerDelta.y, angle), Vector2.zero, zoomFactor);
        }
    }

    public interface IGetTappedCore
    {
        CameraMovement ParseTapIntoCameraMovement();
    }

    public struct CameraMovement
    {
        public Vector3 ScreenSpaceRotation;
        public Vector2 ScreenSpaceTranslation;
        public float Zoom;

        public CameraMovement(Vector3 globalRotation, Vector2 screenSpaceTranslation, float zoom)
        {
            ScreenSpaceRotation = globalRotation;
            ScreenSpaceTranslation = screenSpaceTranslation;
            Zoom = zoom;
        }

        public static CameraMovement Zero()
        {
            return new CameraMovement(Vector3.zero, Vector2.zero, 1.0f);
        }

        public static CameraMovement Translate(Vector2 translation)
        {
            return new CameraMovement(Vector3.zero, translation, 1.0f);
        }

        public static CameraMovement Rotate(Vector3 rotation)
        {
            return new CameraMovement(rotation, Vector2.zero, 1.0f);
        }
    }

    public static class MathExt
    {
        /// <summary>
        /// Signed angle in degrees from <c>from</c> to <c>to</c>
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public static float SignedAngle(Vector2 from, Vector2 to)
        {
            var dot = Vector2.Dot(from, to);
            var magProd = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            var cos = Mathf.Acos(dot / magProd);
            var dir = (from.x * to.y - from.y * to.x) > 0 ? 1 : -1;
            return cos * dir;
        }

        //public static float SignedAngle(Vector2 from, Vector2 to)
        //{
        //    return SignedAngleRadian(from, to) * (180 / Mathf.PI);
        //}
    }
}
