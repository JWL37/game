using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class CenterOfMassCalculator : MonoBehaviour
{
    [Header("Cargo")]
    [SerializeField] private DroneCargoPlatform cargoPlatform;
    [SerializeField] private float warningDistance = 0.28f;
    [SerializeField] private bool updateContinuously;

    [Header("Visualization")]
    [SerializeField] private bool showRuntimeMarkers = true;
    [SerializeField] private float markerSize = 0.12f;
    [SerializeField] private float markerLift = 0.18f;
    [SerializeField] private bool showBalanceHud;
    [SerializeField] private Color platformCenterColor = new Color(0.1f, 0.55f, 1f, 1f);
    [SerializeField] private Color centerOfMassSafeColor = new Color(0.1f, 1f, 0.35f, 1f);
    [SerializeField] private Color centerOfMassWarningColor = new Color(1f, 0.2f, 0.08f, 1f);

    private readonly List<CargoPlacementItem> attachedCargo = new List<CargoPlacementItem>();
    private Rigidbody body;
    private Vector3 baseCenterOfMass;
    private Transform platformCenterMarker;
    private Transform centerOfMassMarker;
    private Renderer centerOfMassRenderer;
    private LineRenderer balanceLine;
    private float attachedCargoMass;
    private float totalEffectiveMass;

    public Vector3 CurrentLocalCenterOfMass { get; private set; }
    public Vector3 PlatformLocalCenter => cargoPlatform != null
        ? transform.InverseTransformPoint(cargoPlatform.PlatformCenterWorld)
        : baseCenterOfMass;

    public float BalanceOffset => Vector3.Distance(
        new Vector3(CurrentLocalCenterOfMass.x, 0f, CurrentLocalCenterOfMass.z),
        new Vector3(PlatformLocalCenter.x, 0f, PlatformLocalCenter.z));

    public bool IsBalanceWarning => BalanceOffset > warningDistance;
    public float AttachedCargoMass => attachedCargoMass;
    public float TotalEffectiveMass => totalEffectiveMass;
    public Vector3 BalanceOffsetLocal => CurrentLocalCenterOfMass - PlatformLocalCenter;
    public Vector3 BalanceOffsetWorld => transform.TransformDirection(BalanceOffsetLocal);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        baseCenterOfMass = body.centerOfMass;
        CurrentLocalCenterOfMass = baseCenterOfMass;

        if (cargoPlatform == null)
        {
            cargoPlatform = GetComponentInChildren<DroneCargoPlatform>();
        }

        EnsureMarkers();
    }

    private void Start()
    {
        Recalculate();
    }

    private void LateUpdate()
    {
        if (updateContinuously)
        {
            Recalculate();
            return;
        }

        UpdateMarkers();
    }

    public void Recalculate()
    {
        attachedCargo.Clear();
        if (cargoPlatform != null)
        {
            cargoPlatform.GetAttachedCargo(attachedCargo);
        }

        attachedCargoMass = 0f;
        totalEffectiveMass = Mathf.Max(0.01f, body.mass);
        Vector3 weightedCenter = baseCenterOfMass * totalEffectiveMass;

        for (int i = 0; i < attachedCargo.Count; i++)
        {
            CargoPlacementItem cargo = attachedCargo[i];
            float cargoMass = Mathf.Max(0.01f, cargo.PackageMass);
            Vector3 cargoLocalCenter = transform.InverseTransformPoint(cargo.WorldCenterOfMass);

            weightedCenter += cargoLocalCenter * cargoMass;
            attachedCargoMass += cargoMass;
            totalEffectiveMass += cargoMass;
        }

        CurrentLocalCenterOfMass = weightedCenter / totalEffectiveMass;
        body.centerOfMass = CurrentLocalCenterOfMass;
        UpdateMarkers();
    }

    private void EnsureMarkers()
    {
        if (!showRuntimeMarkers)
        {
            return;
        }

        platformCenterMarker = CreateMarker("Platform Center Marker", platformCenterColor);
        centerOfMassMarker = CreateMarker("Center Of Mass Marker", centerOfMassSafeColor);
        centerOfMassRenderer = centerOfMassMarker.GetComponent<Renderer>();

        GameObject lineObject = new GameObject("Center Of Mass Offset Line");
        lineObject.transform.SetParent(transform, false);
        balanceLine = lineObject.AddComponent<LineRenderer>();
        balanceLine.useWorldSpace = false;
        balanceLine.positionCount = 2;
        balanceLine.startWidth = 0.035f;
        balanceLine.endWidth = 0.035f;
        balanceLine.material = CreateMarkerMaterial(centerOfMassSafeColor);
    }

    private Transform CreateMarker(string markerName, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = markerName;
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * markerSize;

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        markerRenderer.material = CreateMarkerMaterial(color);

        return marker.transform;
    }

    private static Material CreateMarkerMaterial(Color color)
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

    private void UpdateMarkers()
    {
        if (!showRuntimeMarkers || platformCenterMarker == null || centerOfMassMarker == null)
        {
            return;
        }

        Vector3 platformCenter = PlatformLocalCenter;
        Vector3 centerOfMassProjection = new Vector3(
            CurrentLocalCenterOfMass.x,
            platformCenter.y,
            CurrentLocalCenterOfMass.z);

        Vector3 markerOffset = Vector3.up * markerLift;
        platformCenterMarker.localPosition = platformCenter + markerOffset;
        centerOfMassMarker.localPosition = centerOfMassProjection + markerOffset;

        if (centerOfMassRenderer != null)
        {
            centerOfMassRenderer.material.color = IsBalanceWarning ? centerOfMassWarningColor : centerOfMassSafeColor;
        }

        if (balanceLine != null)
        {
            Color lineColor = IsBalanceWarning ? centerOfMassWarningColor : centerOfMassSafeColor;
            balanceLine.startColor = lineColor;
            balanceLine.endColor = lineColor;
            balanceLine.SetPosition(0, platformCenter + markerOffset);
            balanceLine.SetPosition(1, centerOfMassProjection + markerOffset);
        }
    }

    private void OnGUI()
    {
        if (!showBalanceHud)
        {
            return;
        }

        string state = IsBalanceWarning ? "ОПАСНО" : "НОРМА";
        GUI.color = IsBalanceWarning ? centerOfMassWarningColor : centerOfMassSafeColor;
        GUI.Label(new Rect(16f, 16f, 360f, 26f), "Баланс: " + state + " | смещение " + BalanceOffset.ToString("0.00") + " м");
        GUI.color = Color.white;
    }

    private void OnDrawGizmosSelected()
    {
        Rigidbody gizmoBody = body != null ? body : GetComponent<Rigidbody>();
        if (gizmoBody == null)
        {
            return;
        }

        Vector3 platformCenter = transform.TransformPoint(PlatformLocalCenter);
        Vector3 centerOfMass = transform.TransformPoint(Application.isPlaying ? CurrentLocalCenterOfMass : gizmoBody.centerOfMass);

        Gizmos.color = platformCenterColor;
        Gizmos.DrawWireSphere(platformCenter, markerSize);
        Gizmos.color = IsBalanceWarning ? centerOfMassWarningColor : centerOfMassSafeColor;
        Gizmos.DrawSphere(centerOfMass, markerSize);
        Gizmos.DrawLine(platformCenter, centerOfMass);
    }
}
