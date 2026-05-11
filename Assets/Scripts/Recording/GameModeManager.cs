using System.Collections.Generic;
using UnityEngine;

public enum GameMode
{
    BuildMode,
    RecordMode,
    PlaybackMode,
    ResultMode
}

public sealed class GameModeManager : MonoBehaviour
{
    [SerializeField] private PlayerRecorder recorder;
    [SerializeField] private DroneRouteFollower drone;
    [SerializeField] private CenterOfMassCalculator centerOfMass;
    [SerializeField] private LevelResultManager resultManager;
    [SerializeField] private bool showUi = true;

    private readonly List<CargoPlacementItem> attachedCargo = new List<CargoPlacementItem>();
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle smallStyle;

    public GameMode CurrentMode { get; private set; }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();
        CurrentMode = DetermineMode();
    }

    private void ResolveReferences()
    {
        if (recorder == null)
        {
            recorder = FindAnyObjectByType<PlayerRecorder>();
        }

        if (drone == null)
        {
            drone = FindAnyObjectByType<DroneRouteFollower>();
        }

        if (centerOfMass == null && drone != null)
        {
            centerOfMass = drone.GetComponent<CenterOfMassCalculator>();
        }

        if (resultManager == null)
        {
            resultManager = FindAnyObjectByType<LevelResultManager>();
        }
    }

    private GameMode DetermineMode()
    {
        if (resultManager != null
            && (resultManager.State == LevelResultState.Success || resultManager.State == LevelResultState.Failure))
        {
            return GameMode.ResultMode;
        }

        if (drone != null && drone.IsPlaying)
        {
            return GameMode.PlaybackMode;
        }

        if (recorder != null && recorder.IsRecording)
        {
            return GameMode.RecordMode;
        }

        return GameMode.BuildMode;
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        EnsureStyles();

        GUILayout.BeginArea(new Rect(14f, 14f, 340f, 260f), panelStyle);
        GUILayout.Label("Mode: " + GetModeLabel(CurrentMode), titleStyle);
        GUILayout.Space(8f);

        DrawControls();
        GUILayout.Space(8f);
        DrawBalance();
        DrawCargoStatus();
        DrawRouteTime();
        DrawResult();

        GUILayout.EndArea();
    }

    private void DrawControls()
    {
        GUILayout.BeginHorizontal();

        bool canRecord = CurrentMode == GameMode.BuildMode || CurrentMode == GameMode.RecordMode;
        GUI.enabled = canRecord && recorder != null;
        string recordLabel = recorder != null && recorder.IsRecording ? "Stop Recording" : "Record";
        if (GUILayout.Button(recordLabel, GUILayout.Height(32f)))
        {
            recorder.ToggleRecording();
        }

        bool hasRoute = recorder != null && recorder.FrameCount > 0;
        bool hasCargo = GetAttachedCargoCount() > 0;
        GUI.enabled = CurrentMode == GameMode.BuildMode && drone != null && hasRoute && hasCargo;
        if (GUILayout.Button("Launch Drone", GUILayout.Height(32f)))
        {
            drone.StartPlayback();
        }

        GUI.enabled = drone != null;
        if (GUILayout.Button("Reset", GUILayout.Height(32f)))
        {
            drone.ResetDrone();
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawBalance()
    {
        if (centerOfMass == null)
        {
            GUILayout.Label("Balance: unavailable", smallStyle);
            return;
        }

        string balanceState = centerOfMass.IsBalanceWarning ? "WARNING" : "OK";
        GUILayout.Label("Balance: " + balanceState + " | offset " + centerOfMass.BalanceOffset.ToString("0.00") + " m", smallStyle);
    }

    private void DrawCargoStatus()
    {
        attachedCargo.Clear();
        if (drone != null)
        {
            drone.GetAttachedCargo(attachedCargo);
        }

        int damaged = 0;
        for (int i = 0; i < attachedCargo.Count; i++)
        {
            if (attachedCargo[i].IsDamaged)
            {
                damaged++;
            }
        }

        string cargoState = damaged > 0 ? "damaged " + damaged : "intact";
        GUILayout.Label("Cargo: " + attachedCargo.Count + " attached | " + cargoState, smallStyle);
    }

    private int GetAttachedCargoCount()
    {
        attachedCargo.Clear();
        if (drone != null)
        {
            drone.GetAttachedCargo(attachedCargo);
        }

        return attachedCargo.Count;
    }

    private void DrawRouteTime()
    {
        if (recorder == null)
        {
            GUILayout.Label("Route time: 0.0 s", smallStyle);
            return;
        }

        float time = drone != null && drone.IsPlaying ? drone.PlaybackTime : recorder.RouteDuration;
        GUILayout.Label("Route time: " + time.ToString("0.0") + " s | frames " + recorder.FrameCount, smallStyle);
    }

    private void DrawResult()
    {
        if (resultManager == null)
        {
            return;
        }

        GUILayout.Space(6f);
        GUILayout.Label("Result: " + resultManager.ResultMessage, smallStyle);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 10, 10)
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };

        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true
        };
    }

    private static string GetModeLabel(GameMode mode)
    {
        switch (mode)
        {
            case GameMode.RecordMode:
                return "RecordMode";
            case GameMode.PlaybackMode:
                return "PlaybackMode";
            case GameMode.ResultMode:
                return "ResultMode";
            default:
                return "BuildMode";
        }
    }
}
