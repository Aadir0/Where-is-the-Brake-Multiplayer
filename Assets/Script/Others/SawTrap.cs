using UnityEngine;

public class SawTrap : MonoBehaviour
{
    public enum MovementAxis
    {
        BothAxes,
        XOnly,
        YOnly
    }

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 360f; // Degrees per second around Z axis

    [Header("Patrol Movement Settings")]
    [SerializeField] private bool canMove = false;
    [SerializeField] private MovementAxis movementAxis = MovementAxis.BothAxes;
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;
    [SerializeField] private float moveSpeed = 3.0f;
    [SerializeField] private float arrivalThreshold = 0.05f;

    private bool movingToB = true;

    private void Start()
    {
        if (canMove && pointA != null && pointB != null)
        {
            movingToB = true;
        }
    }

    private Vector3 GetTargetPosition(Vector3 rawTarget)
    {
        Vector3 currentPos = transform.position;

        switch (movementAxis)
        {
            case MovementAxis.XOnly:
                return new Vector3(rawTarget.x, currentPos.y, currentPos.z);
            case MovementAxis.YOnly:
                return new Vector3(currentPos.x, rawTarget.y, currentPos.z);
            default:
                return new Vector3(rawTarget.x, rawTarget.y, currentPos.z);
        }
    }

    private void Update()
    {
        // 1. Continuous Z-Axis Rotation
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        // 2. Point-to-Point Patrol Movement
        if (canMove && pointA != null && pointB != null)
        {
            Vector3 rawTarget = movingToB ? pointB.position : pointA.position;
            Vector3 targetPos = GetTargetPosition(rawTarget);

            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );

            if (Vector3.Distance(transform.position, targetPos) <= arrivalThreshold)
            {
                movingToB = !movingToB;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Continuous rotation preview ray in Gizmos
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, transform.up * 1f);

        // Draw patrol path between Point A and Point B based on MovementAxis
        if (canMove && pointA != null && pointB != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 startPos = GetTargetPosition(pointA.position);
            Vector3 endPos = GetTargetPosition(pointB.position);

            Gizmos.DrawLine(startPos, endPos);
            Gizmos.DrawWireSphere(startPos, 0.3f);
            Gizmos.DrawWireSphere(endPos, 0.3f);
        }
    }
}
