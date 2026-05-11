using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class DroneCargoPlatform : MonoBehaviour
{
    [SerializeField] private Camera placementCamera;
    [SerializeField] private Rigidbody droneBody;
    [SerializeField] private CenterOfMassCalculator centerOfMassCalculator;
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private float gridStep = 0.25f;
    [SerializeField] private float surfaceGap = 0.03f;
    [SerializeField] private float placementMargin = 0.08f;
    [SerializeField] private float raycastDistance = 100f;

    private CargoPlacementItem selectedItem;
    private int rotationSteps;

    public Vector3 PlatformCenterWorld => transform.TransformPoint(new Vector3(0f, 0.5f, 0f));

    private void Awake()
    {
        if (placementCamera == null)
        {
            placementCamera = Camera.main;
        }

        if (droneBody == null)
        {
            droneBody = GetComponentInParent<Rigidbody>();
        }

        if (centerOfMassCalculator == null && droneBody != null)
        {
            centerOfMassCalculator = droneBody.GetComponent<CenterOfMassCalculator>();
            if (centerOfMassCalculator == null)
            {
                centerOfMassCalculator = droneBody.gameObject.AddComponent<CenterOfMassCalculator>();
            }
        }
    }

    private void Update()
    {
        if (placementCamera == null)
        {
            return;
        }

        if (WasSelectPressed())
        {
            TrySelectCargo();
        }

        if (selectedItem == null)
        {
            if (WasRotatePressed())
            {
                TryDetachCargo();
            }

            return;
        }

        if (WasRotatePressed())
        {
            rotationSteps = (rotationSteps + 1) % 4;
        }

        UpdateSelectedPreview();

        if (WasSelectReleased())
        {
            selectedItem.AttachTo(droneBody);
            centerOfMassCalculator?.Recalculate();
            selectedItem = null;
        }
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
            if (cargoItems[i].IsAttached && cargoItems[i].AttachedBody == droneBody)
            {
                results.Add(cargoItems[i]);
            }
        }
    }

    private void TrySelectCargo()
    {
        Ray ray = placementCamera.ScreenPointToRay(ReadPointerPosition());
        if (!Physics.Raycast(ray, out RaycastHit hit, raycastDistance))
        {
            return;
        }

        CargoPlacementItem cargo = hit.collider.GetComponentInParent<CargoPlacementItem>();
        if (cargo == null)
        {
            return;
        }

        bool wasAttached = cargo.IsAttached;

        selectedItem = cargo;
        rotationSteps = Mathf.RoundToInt(cargo.transform.eulerAngles.y / 90f) % 4;
        selectedItem.BeginPlacement();
        if (wasAttached)
        {
            centerOfMassCalculator?.Recalculate();
        }

        UpdateSelectedPreview();
    }

    private void TryDetachCargo()
    {
        Ray ray = placementCamera.ScreenPointToRay(ReadPointerPosition());
        if (!Physics.Raycast(ray, out RaycastHit hit, raycastDistance))
        {
            return;
        }

        CargoPlacementItem cargo = hit.collider.GetComponentInParent<CargoPlacementItem>();
        if (cargo == null || !cargo.IsAttached)
        {
            return;
        }

        cargo.Detach();
        centerOfMassCalculator?.Recalculate();
    }

    private void UpdateSelectedPreview()
    {
        if (!TryGetPointerOnPlatform(out Vector3 platformPoint))
        {
            return;
        }

        Vector3 position = platformPoint + transform.up * (selectedItem.WorldHalfHeight + surfaceGap);
        Quaternion rotation = Quaternion.AngleAxis(rotationSteps * 90f, transform.up)
            * Quaternion.LookRotation(transform.forward, transform.up);

        selectedItem.MovePreview(position, rotation);
    }

    private bool TryGetPointerOnPlatform(out Vector3 platformPoint)
    {
        Ray ray = placementCamera.ScreenPointToRay(ReadPointerPosition());
        Plane platformPlane = new Plane(transform.up, transform.TransformPoint(new Vector3(0f, 0.5f, 0f)));

        if (!platformPlane.Raycast(ray, out float distance))
        {
            platformPoint = Vector3.zero;
            return false;
        }

        Vector3 worldPoint = ray.GetPoint(distance);
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float min = -0.5f + placementMargin;
        float max = 0.5f - placementMargin;

        localPoint.x = Mathf.Clamp(localPoint.x, min, max);
        localPoint.y = 0.5f;
        localPoint.z = Mathf.Clamp(localPoint.z, min, max);

        if (snapToGrid && gridStep > 0.001f)
        {
            localPoint.x = Mathf.Round(localPoint.x / gridStep) * gridStep;
            localPoint.z = Mathf.Round(localPoint.z / gridStep) * gridStep;
            localPoint.x = Mathf.Clamp(localPoint.x, min, max);
            localPoint.z = Mathf.Clamp(localPoint.z, min, max);
        }

        platformPoint = transform.TransformPoint(localPoint);
        return true;
    }

    private static Vector2 ReadPointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool WasSelectPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static bool WasSelectReleased()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    private static bool WasRotatePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }
}
