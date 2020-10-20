using System.Text;
using UnityEngine;
using BepInEx.Configuration;
using BepInEx;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.UI;
using System;

namespace Karenia.GetTapped.Core
{
    public class PluginConfig
    {
        public ConfigEntry<bool> PluginEnabled;
        public ConfigEntry<bool> SingleTapTranslate;
        public ConfigEntry<float> RotationSensitivity;
        public ConfigEntry<float> ZoomSensitivity;
        public ConfigEntry<float> TranslationSensitivity;

        public virtual void BindConfig(ConfigFile Config)
        {
            PluginEnabled = Config.Bind(new ConfigDefinition("default", "Enabled"), true);
            SingleTapTranslate = Config.Bind(new ConfigDefinition("default", "Translate using single tap"), false);
            RotationSensitivity = Config.Bind(new ConfigDefinition("default", "Rotation sensitivity"), 0.3f);
            ZoomSensitivity = Config.Bind(new ConfigDefinition("default", "Zoom sensitivity"), 0.1f);
            TranslationSensitivity = Config.Bind(new ConfigDefinition("default", "Translation sensitivity"), 0.1f);
        }
    }

    public class PluginCore : IGetTappedPlugin
    {
        CameraMovement? calculated = null;
        int lastFrame = -1;

        readonly HashSet<int> untrackedPointers = new HashSet<int>();
        readonly List<Touch> framePointers = new List<Touch>();

        public CameraMovement GetCameraMovement(bool singleTapPan = false, bool forceRecalcualte = false, Func<Touch, bool>? shouldBeUntracked = null)
        {
            var frame = Time.frameCount;
            if (frame == lastFrame
                && !forceRecalcualte
                && calculated.HasValue)
            {
                return calculated.Value;
            }
            lastFrame = frame;

            var cnt = Input.touchCount;

            for (int i = 0; i < cnt; i++)
            {
                var pointer = Input.GetTouch(i);
                if (!ManageUntrackedPointer(ref pointer, shouldBeUntracked))
                {
                    framePointers.Add(pointer);
                }
            }

            cnt = framePointers.Count;

            CameraMovement cameraMovement;
            if (singleTapPan)
            {
                cameraMovement = cnt switch
                {
                    0 => CameraMovement.Zero(),
                    1 => SingleTouchToCameraPan(framePointers[0]),
                    2 => DoubleTouchToCameraRotate(framePointers[0], framePointers[1]),
                    _ => CameraMovement.Zero(),
                };
            }
            else
            {
                cameraMovement = cnt switch
                {
                    0 => CameraMovement.Zero(),
                    1 => SingleTouchToCamera(framePointers[0]),
                    2 => DoubleTouchToCamera(framePointers[0], framePointers[1]),
                    _ => CameraMovement.Zero(),
                };
            }

            calculated = cameraMovement;

            // cleanup
            framePointers.Clear();

            return cameraMovement;
        }

        private bool ManageUntrackedPointer(ref Touch touch, Func<Touch, bool>? shouldBeUntracked)
        {
            if (touch.phase == TouchPhase.Began)
            {
                if (shouldBeUntracked != null && shouldBeUntracked(touch))
                {
                    untrackedPointers.Add(touch.fingerId);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                return untrackedPointers.Remove(touch.fingerId);
            }
            else
            {
                return untrackedPointers.Contains(touch.fingerId);
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

        private CameraMovement SingleTouchToCameraPan(Touch tap1)
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

    public interface IGetTappedPlugin
    {
        CameraMovement GetCameraMovement(bool singleTapTranslate = false, bool forceRecalculate = false, Func<Touch, bool>? shouldBeUntracked = null);
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

        public override string ToString()
        {
            return $"CameraMovement {{ Rot = {ScreenSpaceRotation}, Pan = {ScreenSpaceTranslation}, Zoom = {Zoom} }}";
        }

        public bool HasMoved()
        {
            return this.ScreenSpaceRotation != Vector3.zero || this.ScreenSpaceTranslation != Vector2.zero || this.Zoom != 1;
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
