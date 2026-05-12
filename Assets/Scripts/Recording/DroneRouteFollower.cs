using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody))]
public sealed class DroneRouteFollower : MonoBehaviour
{
    public event System.Action PlaybackStarted;
    public event System.Action PlaybackStopped;

    [Header("Route")]
    [SerializeField] private PlayerRecorder routeSource;
    [SerializeField] private float flightHeightOffset = 1.25f;

    [Header("Position Control")]
    [SerializeField] private float positionSpring = 18f;
    [SerializeField] private float velocityDamping = 7f;
    [SerializeField] private float maxCorrectionForce = 55f;
    [SerializeField] private float cargoMassForcePenalty = 0.04f;
    [SerializeField] private float imbalanceForcePenalty = 0.65f;
    [SerializeField] private float imbalanceDeadZone = 0.12f;

    [Header("Stabilization")]
    [SerializeField] private float uprightTorque = 18f;
    [SerializeField] private float yawTorque = 7f;
    [SerializeField] private float angularDamping = 5f;
    [SerializeField] private float maxTorque = 32f;
    [SerializeField] private float imbalanceStabilityPenalty = 0.35f;

    [Header("Route Error From Balance")]
    [SerializeField] private float imbalanceTiltTorque = 8f;
    [SerializeField] private float imbalanceDriftForce = 0f;
    [SerializeField] private float tiltDriftForce = 0.25f;
    [SerializeField] private float routeErrorWarningDistance = 1.2f;
    [SerializeField] private bool showTargetMarker = true;
    [SerializeField] private bool showPlaybackHud;

    public bool IsPlaying { get; private set; }
    public int PlaybackFrameIndex => playbackIndex;
    public float PlaybackTime => playbackTime;
    public Vector3 CurrentTargetPosition { get; private set; }
    public Quaternion CurrentTargetRotation { get; private set; } = Quaternion.identity;

    private Rigidbody body;
    private Vector3 resetPosition;
    private Quaternion resetRotation;
    private float playbackTime;
    private int playbackIndex;
    private CenterOfMassCalculator centerOfMassCalculator;
    private Transform targetMarker;
    private Renderer targetMarkerRenderer;
    private LineRenderer routeErrorLine;
    private readonly List<CargoPlacementItem> teleportCargo = new List<CargoPlacementItem>();
    private readonly List<bool> teleportCargoKinematicStates = new List<bool>();
    private readonly List<CargoPlacementItem> launchCargo = new List<CargoPlacementItem>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        centerOfMassCalculator = GetComponent<CenterOfMassCalculator>();
        resetPosition = transform.position;
        resetRotation = transform.rotation;
        EnsurePlaybackVisualization();
        EnsureLevelResultManager();
        EnsureGameModeManager();
    }

    private void Update()
    {
        if (WasStartPlaybackPressed())
        {
            StartPlayback();
        }

        if (WasResetPressed())
        {
            ResetDrone();
        }
    }

    private void FixedUpdate()
    {
        if (!IsPlaying)
        {
            return;
        }

        IReadOnlyList<RecordedRouteFrame> route = GetRoute();
        if (route == null || route.Count == 0)
        {
            StopPlayback();
            return;
        }

        SampleRoute(route);
        ApplyPositionControl();
        ApplyStabilization();
        UpdatePlaybackVisualization();

        playbackTime += Time.fixedDeltaTime;

        if (playbackTime > route[route.Count - 1].time)
        {
            StopPlayback();
        }
    }

    public void StartPlayback()
    {
        IReadOnlyList<RecordedRouteFrame> route = GetRoute();
        if (routeSource != null && routeSource.IsRecording)
        {
            Debug.Log("Остановите запись маршрута перед запуском дрона.");
            return;
        }

        if (route == null || route.Count == 0)
        {
            Debug.Log("Для запуска дрона сначала нужен записанный маршрут.");
            return;
        }

        launchCargo.Clear();
        GetAttachedCargo(launchCargo);
        if (launchCargo.Count == 0)
        {
            Debug.Log("Закрепите хотя бы одну посылку перед запуском дрона.");
            FindAnyObjectByType<LevelResultManager>()?.ReportLaunchBlocked("Закрепите хотя бы одну посылку перед запуском.");
            return;
        }

        playbackTime = 0f;
        playbackIndex = 0;
        centerOfMassCalculator = GetComponent<CenterOfMassCalculator>();
        centerOfMassCalculator?.Recalculate();
        IsPlaying = true;
        SnapToRouteStart(route[0]);
        PlaybackStarted?.Invoke();
        Debug.Log($"Запуск дрона начат. Кадров: {route.Count}");
    }

    public void StopPlayback()
    {
        if (IsPlaying)
        {
            Debug.Log("Запуск дрона остановлен.");
        }

        IsPlaying = false;
        UpdatePlaybackVisualization();
        PlaybackStopped?.Invoke();
    }

    public void ResetDrone()
    {
        IsPlaying = false;
        playbackTime = 0f;
        playbackIndex = 0;

        FindAnyObjectByType<LevelResultManager>()?.RestartAttempt();
        TeleportBodyWithAttachedCargo(resetPosition, resetRotation);
        Debug.Log("Дрон возвращен на стартовую позицию.");
    }

    private IReadOnlyList<RecordedRouteFrame> GetRoute()
    {
        return routeSource != null ? routeSource.RouteFrames : null;
    }

    private void SampleRoute(IReadOnlyList<RecordedRouteFrame> route)
    {
        if (route.Count == 1)
        {
            SetTarget(route[0]);
            return;
        }

        while (playbackIndex < route.Count - 2 && route[playbackIndex + 1].time <= playbackTime)
        {
            playbackIndex++;
        }

        RecordedRouteFrame from = route[playbackIndex];
        RecordedRouteFrame to = route[Mathf.Min(playbackIndex + 1, route.Count - 1)];
        float duration = Mathf.Max(to.time - from.time, 0.0001f);
        float t = Mathf.Clamp01((playbackTime - from.time) / duration);

        CurrentTargetPosition = Vector3.Lerp(from.position, to.position, t) + Vector3.up * flightHeightOffset;
        CurrentTargetRotation = Quaternion.Slerp(from.rotation, to.rotation, t);
    }

    private void SetTarget(RecordedRouteFrame frame)
    {
        CurrentTargetPosition = frame.position + Vector3.up * flightHeightOffset;
        CurrentTargetRotation = frame.rotation;
    }

    private void SnapToRouteStart(RecordedRouteFrame startFrame)
    {
        SetTarget(startFrame);
        TeleportBodyWithAttachedCargo(
            CurrentTargetPosition,
            Quaternion.Euler(0f, CurrentTargetRotation.eulerAngles.y, 0f));
    }

    private void TeleportBodyWithAttachedCargo(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3 oldPosition = body.position;
        Quaternion oldRotation = body.rotation;
        Quaternion rotationDelta = targetRotation * Quaternion.Inverse(oldRotation);

        CollectAttachedCargoForTeleport();

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = targetPosition;
        body.rotation = targetRotation;

        for (int i = 0; i < teleportCargo.Count; i++)
        {
            teleportCargo[i].CompleteConnectedBodyTeleport(
                oldPosition,
                rotationDelta,
                targetPosition,
                teleportCargoKinematicStates[i]);
        }

        Physics.SyncTransforms();
    }

    private void CollectAttachedCargoForTeleport()
    {
        teleportCargo.Clear();
        teleportCargoKinematicStates.Clear();

        CargoPlacementItem[] cargoItems = FindObjectsByType<CargoPlacementItem>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cargoItems.Length; i++)
        {
            if (cargoItems[i].BeginConnectedBodyTeleport(body, out bool wasKinematic))
            {
                teleportCargo.Add(cargoItems[i]);
                teleportCargoKinematicStates.Add(wasKinematic);
            }
        }
    }

    private void ApplyPositionControl()
    {
        Vector3 positionError = CurrentTargetPosition - body.position;
        Vector3 correctionForce = positionError * positionSpring - body.linearVelocity * velocityDamping;
        correctionForce = Vector3.ClampMagnitude(correctionForce * body.mass, GetEffectiveMaxCorrectionForce());

        Vector3 gravityCompensation = -Physics.gravity * body.mass;
        Vector3 tiltDrift = GetTiltDriftForce();
        Vector3 totalForce = gravityCompensation + correctionForce + tiltDrift;
        body.AddForce(totalForce, ForceMode.Force);
    }

    private void ApplyStabilization()
    {
        Vector3 uprightAxis = Vector3.Cross(transform.up, Vector3.up);
        Vector3 dampingTorque = -body.angularVelocity * angularDamping;

        Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(CurrentTargetRotation * Vector3.forward, Vector3.up).normalized;
        float yawError = 0f;

        if (currentForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
        {
            yawError = Vector3.SignedAngle(currentForward, targetForward, Vector3.up) * Mathf.Deg2Rad;
        }

        float stabilityFactor = GetStabilityFactor();
        Vector3 imbalanceTorque = GetImbalanceTiltTorque();
        Vector3 torque = uprightAxis * (uprightTorque * stabilityFactor)
            + Vector3.up * (yawError * yawTorque)
            + imbalanceTorque
            + dampingTorque;

        body.AddTorque(Vector3.ClampMagnitude(torque, Mathf.Max(maxTorque * stabilityFactor, maxTorque * 0.7f)), ForceMode.Force);
    }

    public void GetAttachedCargo(List<CargoPlacementItem> results)
    {
        if (results == null)
        {
            return;
        }

        CargoPlacementItem[] cargoItems = FindObjectsByType<CargoPlacementItem>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cargoItems.Length; i++)
        {
            if (cargoItems[i].AttachedBody == body)
            {
                results.Add(cargoItems[i]);
            }
        }
    }

    private void EnsurePlaybackVisualization()
    {
        if (!showTargetMarker)
        {
            return;
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Recorded Route Target Marker";
        marker.transform.localScale = Vector3.one * 0.18f;
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        targetMarker = marker.transform;
        targetMarkerRenderer = marker.GetComponent<Renderer>();
        targetMarkerRenderer.material = CreateDebugMaterial(new Color(1f, 0.86f, 0.1f, 0.9f));
        marker.SetActive(false);

        GameObject lineObject = new GameObject("Route Error Line");
        routeErrorLine = lineObject.AddComponent<LineRenderer>();
        routeErrorLine.useWorldSpace = true;
        routeErrorLine.positionCount = 2;
        routeErrorLine.startWidth = 0.025f;
        routeErrorLine.endWidth = 0.025f;
        routeErrorLine.material = CreateDebugMaterial(Color.white);
        routeErrorLine.enabled = false;
    }

    private void EnsureLevelResultManager()
    {
        if (FindAnyObjectByType<LevelResultManager>() == null)
        {
            gameObject.AddComponent<LevelResultManager>();
        }
    }

    private void EnsureGameModeManager()
    {
        if (FindAnyObjectByType<GameModeManager>() == null)
        {
            gameObject.AddComponent<GameModeManager>();
        }
    }

    private static Material CreateDebugMaterial(Color color)
    {
        Shader markerShader = Shader.Find("Universal Render Pipeline/Lit");
        if (markerShader == null)
        {
            markerShader = Shader.Find("Standard");
        }

        return new Material(markerShader)
        {
            color = color
        };
    }

    private void UpdatePlaybackVisualization()
    {
        if (!showTargetMarker || targetMarker == null || routeErrorLine == null)
        {
            return;
        }

        bool shouldShow = IsPlaying;
        targetMarker.gameObject.SetActive(shouldShow);
        routeErrorLine.enabled = shouldShow;

        if (!shouldShow)
        {
            return;
        }

        float routeError = Vector3.Distance(body.position, CurrentTargetPosition);
        Color lineColor = routeError > routeErrorWarningDistance ? Color.red : Color.yellow;

        targetMarker.position = CurrentTargetPosition;
        targetMarkerRenderer.material.color = lineColor;
        routeErrorLine.startColor = lineColor;
        routeErrorLine.endColor = lineColor;
        routeErrorLine.SetPosition(0, body.position);
        routeErrorLine.SetPosition(1, CurrentTargetPosition);
    }

    private Vector3 GetThrustApplicationPoint()
    {
        if (centerOfMassCalculator == null)
        {
            return body.worldCenterOfMass;
        }

        return transform.TransformPoint(centerOfMassCalculator.PlatformLocalCenter);
    }

    private Vector3 GetImbalanceTiltTorque()
    {
        if (centerOfMassCalculator == null)
        {
            return Vector3.zero;
        }

        float activeImbalance = Mathf.Max(0f, centerOfMassCalculator.BalanceOffset - imbalanceDeadZone);
        if (activeImbalance <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(centerOfMassCalculator.BalanceOffsetWorld, Vector3.up);
        if (horizontalOffset.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 tiltAxis = Vector3.Cross(horizontalOffset.normalized, Vector3.up);
        return tiltAxis * (activeImbalance * imbalanceTiltTorque * body.mass);
    }

    private float GetEffectiveMaxCorrectionForce()
    {
        if (centerOfMassCalculator == null)
        {
            return maxCorrectionForce;
        }

        float massPenalty = 1f + centerOfMassCalculator.AttachedCargoMass * cargoMassForcePenalty;
        float activeImbalance = Mathf.Max(0f, centerOfMassCalculator.BalanceOffset - imbalanceDeadZone);
        float balancePenalty = 1f + activeImbalance * imbalanceForcePenalty;
        return maxCorrectionForce / (massPenalty * balancePenalty);
    }

    private float GetStabilityFactor()
    {
        if (centerOfMassCalculator == null)
        {
            return 1f;
        }

        float activeImbalance = Mathf.Max(0f, centerOfMassCalculator.BalanceOffset - imbalanceDeadZone);
        return Mathf.Clamp(1f / (1f + activeImbalance * imbalanceStabilityPenalty), 0.75f, 1f);
    }

    private Vector3 GetImbalanceDriftForce()
    {
        if (centerOfMassCalculator == null)
        {
            return Vector3.zero;
        }

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(centerOfMassCalculator.BalanceOffsetWorld, Vector3.up);
        return horizontalOffset * (imbalanceDriftForce * body.mass);
    }

    private Vector3 GetTiltDriftForce()
    {
        Vector3 tiltDirection = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        return -tiltDirection * (tiltDriftForce * body.mass);
    }

    private void OnGUI()
    {
        if (!showPlaybackHud || !IsPlaying)
        {
            return;
        }

        float routeError = Vector3.Distance(body.position, CurrentTargetPosition);
        GUI.color = routeError > routeErrorWarningDistance ? Color.red : Color.white;
        GUI.Label(new Rect(16f, 44f, 420f, 26f), "Ошибка маршрута: " + routeError.ToString("0.00") + " м | лимит коррекции " + GetEffectiveMaxCorrectionForce().ToString("0"));
        GUI.color = Color.white;
    }

    private static bool WasStartPlaybackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.pKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.P);
#endif
    }

    private static bool WasResetPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.tKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }
}
