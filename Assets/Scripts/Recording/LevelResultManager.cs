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
    private string resultMessage = "Разместите груз, запишите маршрут и нажмите P.";

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
                Fail("Груз отсутствует.");
                return;
            }

            if (cargo.IsDamaged)
            {
                Fail("Груз поврежден.");
                return;
            }

            if (!cargo.IsAttached)
            {
                Fail("Груз оторвался от дрона.");
                return;
            }

            if (cargo.Position.y < cargoFallY)
            {
                Fail("Груз упал с маршрута.");
                return;
            }
        }
    }

    public void RestartAttempt()
    {
        state = LevelResultState.Build;
        resultMessage = "Попытка сброшена. Переставьте груз или запишите новый маршрут.";
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
            Fail("Закрепите хотя бы одну посылку перед запуском.");
            return;
        }

        state = LevelResultState.Playback;
        resultMessage = "Доставка выполняется.";
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
            Fail("Зона доставки отсутствует.");
            return;
        }

        for (int i = 0; i < requiredCargo.Count; i++)
        {
            CargoPlacementItem cargo = requiredCargo[i];
            if (cargo == null)
            {
                Fail("Груз отсутствует.");
                return;
            }

            if (cargo.IsDamaged)
            {
                Fail("Груз поврежден.");
                return;
            }

            if (!cargo.IsAttached)
            {
                Fail("Груз оторвался от дрона.");
                return;
            }

            if (!deliveryZone.Contains(cargo.Position))
            {
                Fail("Груз не находится в зоне доставки.");
                return;
            }
        }

        state = LevelResultState.Success;
        resultMessage = "Доставка завершена. Груз цел.";
        Debug.Log("Доставка успешна.");
        UnsubscribeCargoDamage();
    }

    private void Fail(string reason)
    {
        if (state == LevelResultState.Failure)
        {
            return;
        }

        state = LevelResultState.Failure;
        resultMessage = "Доставка провалена: " + reason;
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
        Fail("Груз поврежден.");
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

        GUI.Label(new Rect(16f, 72f, 520f, 26f), "Результат: " + resultMessage);
        GUI.color = Color.white;
    }
}
