using System;
using System.Collections.Generic;
using UnityEngine;

namespace Karenia.GetTapped.Util
{
    public class TouchState
    {
        public bool hasMoved = false;
        public float startTime = 0f;

        public event Action<TouchState>? onLongPressCallback = null;

        public event Action<TouchState>? onShortPressCallback = null;

        public event Action<TouchState>? onPressCancelCallback = null;

        public event Action<TouchState>? onDoubleClickCallback = null;

        public void OnPressCancel()
        {
            if (onPressCancelCallback != null)
                onPressCancelCallback.Invoke(this);
            ClearCallbacks();
        }

        public void OnDoubleClick()
        {
            if (onDoubleClickCallback != null)
                onDoubleClickCallback.Invoke(this);
            ClearCallbacks();
        }

        private void ClearCallbacks()
        {
            onLongPressCallback = null;
            onShortPressCallback = null;
            onPressCancelCallback = null;
        }

        public void OnFinish(float currentTime, float longPressThreshold)
        {
            if ((currentTime - startTime) >= longPressThreshold)
            {
                if (onLongPressCallback != null)
                    onLongPressCallback.Invoke(this);
            }
            else
            {
                if (onShortPressCallback != null)
                    onShortPressCallback.Invoke(this);
            }
            ClearCallbacks();
        }

        public override string ToString()
        {
            return $"TouchState {{ pressStartTime={startTime}, hasMoved={hasMoved} }}";
        }
    }

    public enum EventState
    {
        LongPress, ShortPress, PressCancel
    }

    public class TouchPressDetector
    {
        private readonly Dictionary<int, TouchState> touchStates = new Dictionary<int, TouchState>();
        public float LongPressThreshold { get; set; } = 1f;

        private int lastRunFrame = -1;

        private float lastRelease = 0;

        /// <summary>
        /// Updates the internal state to the latest.
        ///
        /// <para>
        /// This method automatically debounces calls to 1/frame, and is not required to be
        /// called every frame.
        /// </para>
        ///
        /// </summary>
        public void Update()
        {
            if (lastRunFrame == Time.frameCount) return;
            else lastRunFrame = Time.frameCount;

            var touchCount = Input.touchCount;
            for (var i = 0; i < touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                int fingerId = touch.fingerId;
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        touchStates.Add(fingerId, new TouchState());
                        break;

                    case TouchPhase.Stationary:
                        touchStates[fingerId].startTime = Time.unscaledTime;
                        break;

                    case TouchPhase.Moved:
                        {
                            TouchState touchState = touchStates[fingerId];

                            touchState.hasMoved = true;
                            touchState.OnPressCancel();
                        }
                        break;

                    case TouchPhase.Canceled:
                        {
                            touchStates.Remove(fingerId);
                        }
                        break;

                    case TouchPhase.Ended:
                        {
                            var touchState = touchStates[fingerId];

                            var thisRelease = Time.unscaledTime;
                            if (thisRelease - lastRelease < 0.6f && !touchState.hasMoved)
                            {
                                touchState.OnDoubleClick();
                                lastRelease = float.NegativeInfinity;
                            }
                            else if (!touchState.hasMoved)
                            {
                                lastRelease = Time.unscaledTime;
                            }

                            touchState.OnFinish(thisRelease, LongPressThreshold);
                            touchStates.Remove(fingerId);
                        }
                        break;
                }
            }
        }

        public void SetCallback(int fingerId, EventState eventState, Action<TouchState> callback)
        {
            if (touchStates.TryGetValue(fingerId, out var state))
            {
                switch (eventState)
                {
                    case EventState.LongPress:
                        state.onLongPressCallback += callback;
                        break;

                    case EventState.PressCancel:
                        state.onPressCancelCallback += callback;
                        break;

                    case EventState.ShortPress:
                        state.onShortPressCallback += callback;
                        break;
                }
            }
        }

        public TouchState? GetState(int touchId)
        {
            if (touchStates.TryGetValue(touchId, out var state))
            {
                return state;
            }
            else
            {
                return null;
            }
        }
    }
}
