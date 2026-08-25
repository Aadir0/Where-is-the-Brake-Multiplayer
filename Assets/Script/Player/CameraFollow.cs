using Unity.Netcode;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Target Settings")]
    public string targetTag = "Player";
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Follow Settings")]
    public float smoothSpeed = 5f;
    public bool lookAtTarget = false;

    [Header("Camera Confiner Bounds")]
    public bool enableConfiner = true;
    public Collider2D confinerCollider;
    public bool confineScreenEdges = true;
    public float minX = -50f; // x1
    public float maxX = 50f;  // x2
    public float minY = -50f; // y1
    public float maxY = 50f;  // y2

    [Header("Camera Shake Settings")]
    [SerializeField] private float defaultShakeDuration = 0.35f;
    [SerializeField] private float defaultShakeIntensity = 0.4f;

    private Transform target;
    private Camera cam;

    private float shakeTimer = 0f;
    private float currentShakeIntensity = 0f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        FindTargetByTag();
        UpdateBoundsFromCollider();
    }

    public void TriggerShake()
    {
        TriggerShake(defaultShakeDuration, defaultShakeIntensity);
    }

    public void TriggerShake(float duration, float intensity)
    {
        shakeTimer = duration;
        currentShakeIntensity = intensity;
        Debug.Log($"[CameraFollow SUCCESS] Camera Shake Triggered! Duration: {duration}s, Intensity: {intensity}");
    }

    public void UpdateBoundsFromCollider()
    {
        if (confinerCollider != null)
        {
            Bounds b = confinerCollider.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
            enableConfiner = true;
        }
    }

    private void FindTargetByTag()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(targetTag);
        foreach (GameObject player in players)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                target = player.transform;
                return;
            }
        }

        if (players.Length > 0 && target == null)
        {
            target = players[0].transform;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            FindTargetByTag();
            return;
        }

        Vector3 desiredPosition = target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        if (enableConfiner)
        {
            smoothedPosition = ClampPosition(smoothedPosition);
        }

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            Vector2 randomOffset = Random.insideUnitCircle * currentShakeIntensity;
            smoothedPosition.x += randomOffset.x;
            smoothedPosition.y += randomOffset.y;
        }

        transform.position = smoothedPosition;

        if (lookAtTarget)
        {
            transform.LookAt(target);
        }
    }

    public Vector3 ClampPosition(Vector3 pos)
    {
        if (!enableConfiner) return pos;

        if (cam == null) cam = GetComponent<Camera>();

        UpdateBoundsFromCollider();

        float effectiveMinX = minX;
        float effectiveMaxX = maxX;
        float effectiveMinY = minY;
        float effectiveMaxY = maxY;

        if (confineScreenEdges && cam != null && cam.orthographic)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            effectiveMinX += halfWidth;
            effectiveMaxX -= halfWidth;
            effectiveMinY += halfHeight;
            effectiveMaxY -= halfHeight;
        }

        // Handle bounds smaller than camera viewport size by centering
        if (effectiveMinX > effectiveMaxX)
        {
            float centerX = (minX + maxX) * 0.5f;
            effectiveMinX = centerX;
            effectiveMaxX = centerX;
        }

        if (effectiveMinY > effectiveMaxY)
        {
            float centerY = (minY + maxY) * 0.5f;
            effectiveMinY = centerY;
            effectiveMaxY = centerY;
        }

        pos.x = Mathf.Clamp(pos.x, effectiveMinX, effectiveMaxX);
        pos.y = Mathf.Clamp(pos.y, effectiveMinY, effectiveMaxY);
        return pos;
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableConfiner) return;

        if (cam == null) cam = GetComponent<Camera>();
        UpdateBoundsFromCollider();

        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
        Vector3 size = new Vector3(Mathf.Abs(maxX - minX), Mathf.Abs(maxY - minY), 0.1f);
        Gizmos.DrawWireCube(center, size);

        if (confineScreenEdges && cam != null && cam.orthographic)
        {
            Gizmos.color = Color.yellow;
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            float effMinX = minX + halfWidth;
            float effMaxX = maxX - halfWidth;
            float effMinY = minY + halfHeight;
            float effMaxY = maxY - halfHeight;

            if (effMinX < effMaxX && effMinY < effMaxY)
            {
                Vector3 innerCenter = new Vector3((effMinX + effMaxX) * 0.5f, (effMinY + effMaxY) * 0.5f, transform.position.z);
                Vector3 innerSize = new Vector3(effMaxX - effMinX, effMaxY - effMinY, 0.1f);
                Gizmos.DrawWireCube(innerCenter, innerSize);
            }
        }
    }
}