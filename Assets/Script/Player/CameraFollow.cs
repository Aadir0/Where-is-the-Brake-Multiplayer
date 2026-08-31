using Unity.Netcode;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Target Settings")]
    public string targetTag = "Player";
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Follow Settings")]
    public float smoothSpeed = 10f;
    public float lookaheadFactor = 0.15f;
    public bool lookAtTarget = false;

    [Header("Camera Confiner Bounds")]
    public bool enableConfiner = true;
    public Collider2D confinerCollider;
    public bool confineScreenEdges = true;
    public float minX = -50f; // x1 fallback
    public float maxX = 50f;  // x2 fallback
    public float minY = -50f; // y1 fallback
    public float maxY = 50f;  // y2 fallback

    [Header("Camera Shake Settings")]
    [SerializeField] private float defaultShakeDuration = 0.35f;
    [SerializeField] private float defaultShakeIntensity = 0.4f;

    private Transform target;
    private Rigidbody2D targetRb;
    private Camera cam;

    private float shakeTimer = 0f;
    private float currentShakeIntensity = 0f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
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
        FindConfinerInScene();
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
    }

    public void FindConfinerInScene()
    {
        if (confinerCollider != null) return;

        GameObject confinerObj = GameObject.FindGameObjectWithTag("Confiner");
        if (confinerObj != null)
        {
            confinerCollider = confinerObj.GetComponent<Collider2D>();
        }

        if (confinerCollider == null)
        {
            confinerCollider = Object.FindFirstObjectByType<PolygonCollider2D>();
        }

        if (confinerCollider == null)
        {
            confinerCollider = Object.FindFirstObjectByType<CompositeCollider2D>();
        }
    }

    public void UpdateBoundsFromCollider()
    {
        FindConfinerInScene();

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
                SetTarget(player.transform);
                return;
            }
        }

        if (players.Length > 0 && target == null)
        {
            SetTarget(players[0].transform);
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            FindTargetByTag();
            return;
        }

        if (targetRb == null && target != null)
        {
            targetRb = target.GetComponent<Rigidbody2D>();
        }

        Vector3 desiredPosition = target.position + offset;

        // Velocity lookahead for smooth dynamic camera anticipation
        if (targetRb != null && lookaheadFactor > 0f)
        {
            Vector2 vel = targetRb.linearVelocity;
            desiredPosition.x += vel.x * lookaheadFactor;
            desiredPosition.y += vel.y * lookaheadFactor;
        }

        // Framerate-independent exponential smoothing
        float blendFactor = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, blendFactor);

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

        FindConfinerInScene();

        float halfHeight = 0f;
        float halfWidth = 0f;
        if (cam != null && cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
            halfWidth = halfHeight * cam.aspect;
        }

        if (confinerCollider == null)
        {
            // Simple rectangular fallback bounds if no collider is assigned or found
            if (confineScreenEdges && cam != null && cam.orthographic)
            {
                pos.x = Mathf.Clamp(pos.x, minX + halfWidth, maxX - halfWidth);
                pos.y = Mathf.Clamp(pos.y, minY + halfHeight, maxY - halfHeight);
            }
            else
            {
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
            }
            return pos;
        }

        // --- UNIVERSAL COLLIDER SHAPE CONFINEMENT ---
        // Works with ANY Collider2D: PolygonCollider2D, CompositeCollider2D,
        // BoxCollider2D, CircleCollider2D, CapsuleCollider2D, EdgeCollider2D, etc.
        // Uses ClosestPoint on all 4 camera viewport corners to push the
        // camera frustum inside the exact collider geometry.

        // First pass: coarse AABB clamp to keep camera in the collider's bounding region
        Bounds b = confinerCollider.bounds;
        float boundMinX = b.min.x + (confineScreenEdges ? halfWidth : 0f);
        float boundMaxX = b.max.x - (confineScreenEdges ? halfWidth : 0f);
        float boundMinY = b.min.y + (confineScreenEdges ? halfHeight : 0f);
        float boundMaxY = b.max.y - (confineScreenEdges ? halfHeight : 0f);

        if (boundMinX > boundMaxX) boundMinX = boundMaxX = b.center.x;
        if (boundMinY > boundMaxY) boundMinY = boundMaxY = b.center.y;

        pos.x = Mathf.Clamp(pos.x, boundMinX, boundMaxX);
        pos.y = Mathf.Clamp(pos.y, boundMinY, boundMaxY);

        // Second pass: iterative 4-corner frustum push against exact collider shape
        // ClosestPoint returns the input point when inside, or the nearest surface
        // point when outside. A non-zero diff means the corner is outside the collider.
        if (confineScreenEdges && cam != null && cam.orthographic)
        {
            for (int iter = 0; iter < 8; iter++)
            {
                Vector2 bl = new Vector2(pos.x - halfWidth, pos.y - halfHeight);
                Vector2 br = new Vector2(pos.x + halfWidth, pos.y - halfHeight);
                Vector2 tl = new Vector2(pos.x - halfWidth, pos.y + halfHeight);
                Vector2 tr = new Vector2(pos.x + halfWidth, pos.y + halfHeight);

                Vector2 totalPush = Vector2.zero;
                int outsideCount = 0;

                // Check each corner and accumulate push vectors
                Vector2 cBL = confinerCollider.ClosestPoint(bl);
                Vector2 dBL = cBL - bl;
                if (dBL.sqrMagnitude > 0.0001f) { totalPush += dBL; outsideCount++; }

                Vector2 cBR = confinerCollider.ClosestPoint(br);
                Vector2 dBR = cBR - br;
                if (dBR.sqrMagnitude > 0.0001f) { totalPush += dBR; outsideCount++; }

                Vector2 cTL = confinerCollider.ClosestPoint(tl);
                Vector2 dTL = cTL - tl;
                if (dTL.sqrMagnitude > 0.0001f) { totalPush += dTL; outsideCount++; }

                Vector2 cTR = confinerCollider.ClosestPoint(tr);
                Vector2 dTR = cTR - tr;
                if (dTR.sqrMagnitude > 0.0001f) { totalPush += dTR; outsideCount++; }

                if (outsideCount == 0) break; // All corners inside — done

                // Use the maximum absolute push per axis for stable convergence
                Vector2 maxPush = Vector2.zero;
                Vector2[] diffs = { dBL, dBR, dTL, dTR };
                foreach (Vector2 d in diffs)
                {
                    if (Mathf.Abs(d.x) > Mathf.Abs(maxPush.x)) maxPush.x = d.x;
                    if (Mathf.Abs(d.y) > Mathf.Abs(maxPush.y)) maxPush.y = d.y;
                }

                if (maxPush.sqrMagnitude < 0.0001f) break;

                pos.x += maxPush.x;
                pos.y += maxPush.y;
            }
        }
        else
        {
            // No screen-edge confinement: just clamp the camera center point
            Vector2 center2D = new Vector2(pos.x, pos.y);
            Vector2 closestCenter = confinerCollider.ClosestPoint(center2D);
            Vector2 centerDiff = closestCenter - center2D;
            if (centerDiff.sqrMagnitude > 0.0001f)
            {
                pos.x = closestCenter.x;
                pos.y = closestCenter.y;
            }
        }

        return pos;
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableConfiner) return;

        if (cam == null) cam = GetComponent<Camera>();
        FindConfinerInScene();
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