using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class MovingObstacle : MonoBehaviour
{
    [SerializeField] private Vector3 localDirection = Vector3.right;
    [SerializeField] private float travelDistance = 3f;
    [SerializeField] private float speed = 1.4f;
    [SerializeField] private float pauseDuration = 0.35f;

    private Rigidbody body;
    private Vector3 startPosition;
    private Vector3 endPosition;
    private Vector3 targetPosition;
    private float pauseTimer;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        startPosition = transform.position;
        Vector3 direction = transform.TransformDirection(localDirection.normalized);
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.right;
        }

        endPosition = startPosition + direction * Mathf.Max(0f, travelDistance);
        targetPosition = endPosition;
    }

    private void FixedUpdate()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector3 nextPosition = Vector3.MoveTowards(
            body.position,
            targetPosition,
            Mathf.Max(0f, speed) * Time.fixedDeltaTime);

        body.MovePosition(nextPosition);

        if ((targetPosition - nextPosition).sqrMagnitude > 0.0001f)
        {
            return;
        }

        targetPosition = (targetPosition - endPosition).sqrMagnitude < 0.0001f
            ? startPosition
            : endPosition;
        pauseTimer = Mathf.Max(0f, pauseDuration);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? startPosition : transform.position;
        Vector3 direction = transform.TransformDirection(localDirection.normalized);
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.right;
        }

        Vector3 end = start + direction * Mathf.Max(0f, travelDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireCube(start, transform.lossyScale);
        Gizmos.DrawWireCube(end, transform.lossyScale);
    }
}
