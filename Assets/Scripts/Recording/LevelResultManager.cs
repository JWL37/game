using System.Collections.Generic;
using UnityEngine;

public enum LevelResultState
{
    Build,
    Playback,
    Success,
    Failure
}

public sealed class LevelResultManager : MonoBehaviour
{
    [SerializeField] private DroneRouteFollower drone;
    [SerializeField] private DeliveryZone deliveryZone;
    [SerializeField] private float cargoFallY = -0.5f;
    [SerializeField] private bool showResultHud;

    private readonly List<CargoPlacementItem> requiredCargo = new List<CargoPlacementItem>();
    private LevelResultState state = LevelResultState.Build;
    private string resultMessage = "Place cargo, record route, then press P.";

    public LevelResultState State => state;
    public string ResultMessage => resultMessage;

    public void ReportLaunchBlocked(string reason)
    {
        state = LevelResultState.Build;
        resultMessage = reason;
    }

    private void Awake()
    {
        if (drone == null)
        {
            drone = FindAnyObjectByType<DroneRouteFollower>();
        }

        if (deliveryZone == null)
        {
            GameObject deliveryObject = GameObject.Find("Delivery Zone");
            if (deliveryObject != null)
            {
                deliveryZone = deliveryObject.GetComponent<DeliveryZone>();
                if (deliveryZone == null)
                {
                    deliveryZone = deliveryObject.AddComponent<DeliveryZone>();
                }
            }
        }
    }

    private void OnEnable()
    {
        if (drone == null)
        {
            return;
        }

        drone.PlaybackStarted += HandlePlaybackStarted;
        drone.PlaybackStopped += HandlePlaybackStopped;
    }

    private void OnDisable()
    {
        if (drone == null)
        {
            return;
        }

        drone.PlaybackStarted -= HandlePlaybackStarted;
        drone.PlaybackStopped -= HandlePlaybackStopped;
        UnsubscribeCargoDamage();
    }

    private void Update()
    {
        if (state != LevelResultState.Playback)
        {
            return;
        }

        for (int i = 0; i < requiredCargo.Count; i++)
        {
            CargoPlacementItem cargo = requiredCargo[i];
            if (cargo == null)
            {
                Fail("Cargo missing.");
                return;
            }

            if (cargo.IsDamaged)
            {
                Fail("Cargo damaged.");
                return;
            }

            if (!cargo.IsAttached)
            {
                Fail("Cargo detached from the drone.");
                return;
            }

            if (cargo.Position.y < cargoFallY)
            {
                Fail("Cargo fell from the delivery route.");
                return;
            }
        }
    }

    public void RestartAttempt()
    {
        state = LevelResultState.Build;
        resultMessage = "Attempt reset. Adjust cargo or record a new route.";
        UnsubscribeCargoDamage();
        requiredCargo.Clear();

        CargoPlacementItem[] allCargo = FindObjectsByType<CargoPlacementItem>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allCargo.Length; i++)
        {
            allCargo[i].ResetToStart();
        }

        PlayerRecorder planner = FindAnyObjectByType<PlayerRecorder>();
        if (planner != null)
        {
            planner.ResetPlanner();
        }

        CenterOfMassCalculator centerOfMass = FindAnyObjectByType<CenterOfMassCalculator>();
        if (centerOfMass != null)
        {
            centerOfMass.Recalculate();
        }
    }

    private void HandlePlaybackStarted()
    {
        requiredCargo.Clear();
        drone.GetAttachedCargo(requiredCargo);
        SubscribeCargoDamage();

        if (requiredCargo.Count == 0)
        {
            Fail("Attach at least one package before launch.");
            return;
        }

        state = LevelResultState.Playback;
        resultMessage = "Delivery in progress.";
    }

    private void HandlePlaybackStopped()
    {
        if (state != LevelResultState.Playback)
        {
            return;
        }

        EvaluateDelivery();
    }

    private void EvaluateDelivery()
    {
        if (deliveryZone == null)
        {
            Fail("Delivery zone is missing.");
            return;
        }

        for (int i = 0; i < requiredCargo.Count; i++)
        {
            CargoPlacementItem cargo = requiredCargo[i];
            if (cargo == null)
            {
                Fail("Cargo missing.");
                return;
            }

            if (cargo.IsDamaged)
            {
                Fail("Cargo damaged.");
                return;
            }

            if (!cargo.IsAttached)
            {
                Fail("Cargo detached from the drone.");
                return;
            }

            if (!deliveryZone.Contains(cargo.Position))
            {
                Fail("Cargo is not inside the delivery zone.");
                return;
            }
        }

        state = LevelResultState.Success;
        resultMessage = "Delivery complete. Cargo intact.";
        Debug.Log("Delivery success.");
        UnsubscribeCargoDamage();
    }

    private void Fail(string reason)
    {
        if (state == LevelResultState.Failure)
        {
            return;
        }

        state = LevelResultState.Failure;
        resultMessage = "Delivery failed: " + reason;
        Debug.LogWarning(resultMessage, this);
        if (drone != null)
        {
            drone.StopPlayback();
        }

        UnsubscribeCargoDamage();
    }

    private void SubscribeCargoDamage()
    {
        for (int i = 0; i < requiredCargo.Count; i++)
        {
            requiredCargo[i].Damaged += HandleCargoDamaged;
        }
    }

    private void UnsubscribeCargoDamage()
    {
        for (int i = 0; i < requiredCargo.Count; i++)
        {
            if (requiredCargo[i] != null)
            {
                requiredCargo[i].Damaged -= HandleCargoDamaged;
            }
        }
    }

    private void HandleCargoDamaged(CargoPlacementItem cargo)
    {
        Fail("Cargo damaged.");
    }

    private void OnGUI()
    {
        if (!showResultHud)
        {
            return;
        }

        GUI.color = state == LevelResultState.Success
            ? Color.green
            : state == LevelResultState.Failure ? Color.red : Color.white;

        GUI.Label(new Rect(16f, 72f, 520f, 26f), "Result: " + resultMessage);
        GUI.color = Color.white;
    }
}
