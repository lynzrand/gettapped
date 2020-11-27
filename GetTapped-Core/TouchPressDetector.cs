using System;
using System.Collections.Generic;
using UnityEngine;

namespace Karenia.GetTapped.Util
{
    public class TouchState
    {
        public bool hasMoved = false;
        public float activeTime = 0f;
        public event Action<TouchState>? onLongPressCallback = null;
        public event Action<TouchState>? onShortPressCallback = null;
        public event Action<TouchState>? onPressCancelCallback = null;

        public void OnPressCancel()
        {
            if (onPressCancelCallback != null)
                onPressCancelCallback.Invoke(this);
        }

        public void OnFinish(float longPressThreshold)
        {
            if (activeTime >= longPressThreshold)
            {
                if (onLongPressCallback != null)
                    onLongPressCallback.Invoke(this);
            }
            else
            {
                if (onShortPressCallback != null)
                    onShortPressCallback.Invoke(this);
            }
        }
    }

    public enum EventState
    {
        LongPress, ShortPress, PressCancel
    }

    public class TouchPressDetector
    {

        private readonly Dictionary<int, TouchState> touchStates = new Dictionary<int, TouchState>();
        public float LongPressThreshold { get; set; } = 2f;

        public void LateUpdate()
        {
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
                        touchStates[fingerId].activeTime += touch.deltaTime;
                        break;
                    case TouchPhase.Moved:
                        {
                            TouchState touchState = touchStates[fingerId];
                            touchState.hasMoved = true;
                            touchState.activeTime += touch.deltaTime;
                            touchState.OnPressCancel();
                        }
                        break;
                    case TouchPhase.Canceled:
                    case TouchPhase.Ended:
                        {
                            var touchState = touchStates[fingerId];
                            touchState.OnFinish(LongPressThreshold);
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

        public TouchState? GetTouchTime(int touchId)
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
