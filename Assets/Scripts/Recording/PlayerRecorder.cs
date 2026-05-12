using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

[Flags]
public enum RecordedRouteAction
{
    None = 0,
    PrimaryActivate = 1 << 0,
    SecondaryActivate = 1 << 1,
    Waiting = 1 << 2
}

[Serializable]
public struct RecordedRouteFrame
{
    public float time;
    public Vector3 position;
    public Quaternion rotation;
    public RecordedRouteAction actions;
}

public sealed class PlayerRecorder : MonoBehaviour
{
    [Header("Planner Movement")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float turnSpeed = 135f;

    [Header("Recording")]
    [SerializeField] private bool startRecordingOnPlay;
    [SerializeField] private List<RecordedRouteFrame> routeFrames = new List<RecordedRouteFrame>();

    public bool IsRecording { get; private set; }
    public int FrameCount => routeFrames.Count;
    public IReadOnlyList<RecordedRouteFrame> RouteFrames => routeFrames;
    public float RouteDuration => routeFrames.Count > 0 ? routeFrames[routeFrames.Count - 1].time : 0f;

    private float recordingTime;
    private Vector3 resetPosition;
    private Quaternion resetRotation;

    private void Awake()
    {
        resetPosition = transform.position;
        resetRotation = transform.rotation;
    }

    private void Start()
    {
        if (startRecordingOnPlay)
        {
            StartRecording();
        }
    }

    private void Update()
    {
        if (WasToggleRecordingPressed())
        {
            ToggleRecording();
        }

        if (!IsRecording && WasClearRoutePressed())
        {
            ClearRoute();
        }
    }

    private void FixedUpdate()
    {
        Vector2 moveInput = ReadMoveInput();
        float turnInput = ReadTurnInput();

        MovePlanner(moveInput, turnInput);

        if (IsRecording)
        {
            RecordFrame(moveInput, turnInput);
        }
    }

    public void StartRecording()
    {
        routeFrames.Clear();
        recordingTime = 0f;
        IsRecording = true;
        Debug.Log("Запись маршрута начата.");
    }

    public void StopRecording()
    {
        IsRecording = false;
        Debug.Log($"Запись маршрута остановлена. Кадров: {routeFrames.Count}");
    }

    public void ToggleRecording()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        StartRecording();
    }

    public void ClearRoute()
    {
        routeFrames.Clear();
        recordingTime = 0f;
        Debug.Log("Записанный маршрут очищен.");
    }

    public void ResetPlanner()
    {
        StopRecordingIfNeeded();
        transform.SetPositionAndRotation(resetPosition, resetRotation);
    }

    private void StopRecordingIfNeeded()
    {
        if (IsRecording)
        {
            StopRecording();
        }
    }

    private void MovePlanner(Vector2 moveInput, float turnInput)
    {
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y);
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        transform.position += move * (moveSpeed * Time.fixedDeltaTime);

        if (Mathf.Abs(turnInput) > 0.01f)
        {
            transform.Rotate(Vector3.up, turnInput * turnSpeed * Time.fixedDeltaTime, Space.World);
        }
    }

    private void RecordFrame(Vector2 moveInput, float turnInput)
    {
        routeFrames.Add(new RecordedRouteFrame
        {
            time = recordingTime,
            position = transform.position,
            rotation = transform.rotation,
            actions = ReadRecordedActions(moveInput, turnInput)
        });

        recordingTime += Time.fixedDeltaTime;
    }

    private RecordedRouteAction ReadRecordedActions(Vector2 moveInput, float turnInput)
    {
        RecordedRouteAction actions = RecordedRouteAction.None;

        if (IsPrimaryActivatePressed())
        {
            actions |= RecordedRouteAction.PrimaryActivate;
        }

        if (IsSecondaryActivatePressed())
        {
            actions |= RecordedRouteAction.SecondaryActivate;
        }

        bool isWaiting = moveInput.sqrMagnitude < 0.0001f
            && Mathf.Abs(turnInput) < 0.0001f
            && actions == RecordedRouteAction.None;

        if (isWaiting)
        {
            actions |= RecordedRouteAction.Waiting;
        }

        return actions;
    }

    private static Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = ReadAxis(keyboard.aKey, keyboard.leftArrowKey, keyboard.dKey, keyboard.rightArrowKey);
        float y = ReadAxis(keyboard.sKey, keyboard.downArrowKey, keyboard.wKey, keyboard.upArrowKey);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private static float ReadTurnInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return 0f;
        }

        return ReadAxis(keyboard.qKey, null, keyboard.eKey, null);
#else
        float turn = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            turn -= 1f;
        }

        if (Input.GetKey(KeyCode.E))
        {
            turn += 1f;
        }

        return turn;
#endif
    }

    private static bool WasToggleRecordingPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    private static bool WasClearRoutePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.cKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.C);
#endif
    }

    private static bool IsPrimaryActivatePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.isPressed;
#else
        return Input.GetKey(KeyCode.Space);
#endif
    }

    private static bool IsSecondaryActivatePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.fKey.isPressed;
#else
        return Input.GetKey(KeyCode.F);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static float ReadAxis(KeyControl negativeA, KeyControl negativeB, KeyControl positiveA, KeyControl positiveB)
    {
        float value = 0f;

        if ((negativeA != null && negativeA.isPressed) || (negativeB != null && negativeB.isPressed))
        {
            value -= 1f;
        }

        if ((positiveA != null && positiveA.isPressed) || (positiveB != null && positiveB.isPressed))
        {
            value += 1f;
        }

        return value;
    }
#endif
}
