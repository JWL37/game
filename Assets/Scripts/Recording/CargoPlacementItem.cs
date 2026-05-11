using UnityEngine;

public enum CargoPackageKind
{
    LightSmallBox,
    HeavyBox,
    LongBox,
    FragilePackage
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public sealed class CargoPlacementItem : MonoBehaviour
{
    public event System.Action<CargoPlacementItem> Damaged;

    [Header("Package Profile")]
    [SerializeField] private CargoPackageKind packageKind = CargoPackageKind.LightSmallBox;
    [SerializeField] private string packageLabel = "Cargo";
    [SerializeField] private float packageMass = 1f;
    [SerializeField] private bool fragile;
    [SerializeField] private float maxImpactImpulse = 8f;
    [SerializeField] private float maxTiltAngle = 60f;
    [SerializeField] private Vector3 localCenterOfMass;

    private Rigidbody body;
    private FixedJoint fixedJoint;
    private Collider itemCollider;
    private bool isDamaged;
    private Vector3 resetPosition;
    private Quaternion resetRotation;

    public float WorldHalfHeight => itemCollider != null ? itemCollider.bounds.extents.y : 0.25f;
    public bool IsAttached => fixedJoint != null;
    public bool IsDamaged => isDamaged;
    public float PackageMass => packageMass;
    public bool IsFragile => fragile;
    public CargoPackageKind PackageKind => packageKind;
    public Vector3 WorldCenterOfMass => transform.TransformPoint(localCenterOfMass);
    public Rigidbody AttachedBody => fixedJoint != null ? fixedJoint.connectedBody : null;
    public Vector3 Position => transform.position;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        itemCollider = GetComponent<Collider>();
        fixedJoint = GetComponent<FixedJoint>();
        resetPosition = transform.position;
        resetRotation = transform.rotation;
        ApplyPhysicsProfile();
    }

    private void FixedUpdate()
    {
        if (!fragile || isDamaged || !IsAttached)
        {
            return;
        }

        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle > maxTiltAngle)
        {
            MarkDamaged("tilt " + tiltAngle.ToString("0.0") + " deg exceeded " + maxTiltAngle.ToString("0.0") + " deg");
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!fragile || isDamaged)
        {
            return;
        }

        float impactImpulse = collision.impulse.magnitude;
        if (impactImpulse > maxImpactImpulse)
        {
            MarkDamaged("impact impulse " + impactImpulse.ToString("0.0") + " exceeded " + maxImpactImpulse.ToString("0.0"));
        }
    }

    public void BeginPlacement()
    {
        Detach();
        body.isKinematic = true;
        body.useGravity = false;
        ResetVelocitiesIfDynamic();
    }

    public void MovePreview(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }

    public bool BeginConnectedBodyTeleport(Rigidbody connectedBody, out bool wasKinematic)
    {
        wasKinematic = body.isKinematic;
        if (AttachedBody != connectedBody)
        {
            return false;
        }

        body.isKinematic = true;
        body.useGravity = false;
        return true;
    }

    public void CompleteConnectedBodyTeleport(
        Vector3 oldBodyPosition,
        Quaternion bodyRotationDelta,
        Vector3 newBodyPosition,
        bool restoreKinematic)
    {
        Vector3 localOffsetFromBody = transform.position - oldBodyPosition;
        Vector3 newPosition = newBodyPosition + bodyRotationDelta * localOffsetFromBody;
        Quaternion newRotation = bodyRotationDelta * transform.rotation;

        transform.SetPositionAndRotation(newPosition, newRotation);
        body.position = newPosition;
        body.rotation = newRotation;
        body.isKinematic = restoreKinematic;
        body.useGravity = !restoreKinematic;
        ResetVelocitiesIfDynamic();
    }

    public void AttachTo(Rigidbody connectedBody)
    {
        if (connectedBody == null)
        {
            return;
        }

        ApplyPhysicsProfile();
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        fixedJoint = gameObject.AddComponent<FixedJoint>();
        fixedJoint.connectedBody = connectedBody;
        fixedJoint.enableCollision = false;
        fixedJoint.breakForce = Mathf.Infinity;
        fixedJoint.breakTorque = Mathf.Infinity;
        body.WakeUp();
    }

    public void Detach()
    {
        fixedJoint = GetComponent<FixedJoint>();
        if (fixedJoint != null)
        {
            Destroy(fixedJoint);
            fixedJoint = null;
        }

        ResetVelocitiesIfDynamic();
        body.isKinematic = true;
        body.useGravity = false;
    }

    public void Repair()
    {
        isDamaged = false;
    }

    public void ResetToStart()
    {
        Detach();
        Repair();
        transform.SetPositionAndRotation(resetPosition, resetRotation);
        body.position = resetPosition;
        body.rotation = resetRotation;
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void ApplyPhysicsProfile()
    {
        body.mass = Mathf.Max(0.01f, packageMass);
        body.centerOfMass = localCenterOfMass;
    }

    private void ResetVelocitiesIfDynamic()
    {
        if (body.isKinematic)
        {
            return;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void MarkDamaged(string reason)
    {
        isDamaged = true;
        Debug.LogWarning(packageLabel + " damaged: " + reason + ".", this);
        Damaged?.Invoke(this);
    }
}
