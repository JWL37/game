using UnityEngine;

public sealed class DeliveryZone : MonoBehaviour
{
    [SerializeField] private float horizontalPadding = 0.65f;
    [SerializeField] private float verticalTolerance = 5f;
    [SerializeField] private Color gizmoColor = new Color(0.1f, 1f, 0.45f, 0.35f);

    private Renderer zoneRenderer;
    private Collider zoneCollider;

    private void Awake()
    {
        zoneRenderer = GetComponent<Renderer>();
        zoneCollider = GetComponent<Collider>();
    }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3 halfSize = Vector3.one * 0.5f;

        if (zoneCollider is BoxCollider boxCollider)
        {
            localPosition -= boxCollider.center;
            halfSize = boxCollider.size * 0.5f;
        }

        Vector3 scale = transform.lossyScale;
        float horizontalX = halfSize.x + horizontalPadding / Mathf.Max(Mathf.Abs(scale.x), 0.001f);
        float horizontalZ = halfSize.z + horizontalPadding / Mathf.Max(Mathf.Abs(scale.z), 0.001f);
        float vertical = halfSize.y + verticalTolerance / Mathf.Max(Mathf.Abs(scale.y), 0.001f);

        return Mathf.Abs(localPosition.x) <= horizontalX
            && Mathf.Abs(localPosition.z) <= horizontalZ
            && Mathf.Abs(localPosition.y) <= vertical;
    }

    private Bounds GetWorldBounds()
    {
        if (zoneCollider != null)
        {
            return zoneCollider.bounds;
        }

        if (zoneRenderer != null)
        {
            return zoneRenderer.bounds;
        }

        return new Bounds(transform.position, transform.lossyScale);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;
        Vector3 size = new Vector3(1f + horizontalPadding * 2f, verticalTolerance * 2f, 1f + horizontalPadding * 2f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
