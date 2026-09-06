using System;
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
    public float lookaheadFactor = 0.25f;
    public float lookaheadDamping = 5f;
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
    private Vector2 currentLookahead = Vector2.zero;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
        currentLookahead = Vector2.zero;
        StopShake();
        if (target != null)
        {
            Vector3 desiredPos = target.position + offset;
            transform.position = new Vector3(desiredPos.x, desiredPos.y, transform.position.z);
        }
    }

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        StopShake();
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        StopShake();

        if (scene.name.Equals("Ending", StringComparison.OrdinalIgnoreCase) || scene.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            target = null;
            confinerCollider = null;
            enableConfiner = false;
            return;
        }

        confinerCollider = null;
        enableConfiner = false;
        FindTargetByTag();
        FindConfinerInScene();
        UpdateBoundsFromCollider();

        if (target != null)
        {
            Vector3 desiredPos = target.position + offset;
            transform.position = new Vector3(desiredPos.x, desiredPos.y, transform.position.z);
        }
    }

    private void Start()
    {
        StopShake();
        if (cam == null) cam = GetComponent<Camera>();
        FindTargetByTag();
        FindConfinerInScene();
        UpdateBoundsFromCollider();

        if (target != null)
        {
            Vector3 desiredPos = target.position + offset;
            transform.position = new Vector3(desiredPos.x, desiredPos.y, transform.position.z);
        }
    }

    public void StopShake()
    {
        shakeTimer = 0f;
        currentShakeIntensity = 0f;
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
        if (confinerCollider != null && confinerCollider.gameObject.scene.isLoaded) return;
        confinerCollider = null;

        GameObject confinerObj = GameObject.FindGameObjectWithTag("Confiner");
        if (confinerObj != null)
        {
            confinerCollider = confinerObj.GetComponent<Collider2D>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void UpdateBoundsFromCollider()
    {
        FindConfinerInScene();

        if (confinerCollider != null && confinerCollider.gameObject.scene.isLoaded)
        {
            Bounds b = confinerCollider.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
            enableConfiner = true;
        }
        else
        {
            enableConfiner = false;
        }
    }

    private void FindTargetByTag()
    {
        NetworkCarController[] cars = UnityEngine.Object.FindObjectsByType<NetworkCarController>(FindObjectsSortMode.None);
        foreach (var car in cars)
        {
            if (car.IsOwner || car.IsLocalPlayer)
            {
                SetTarget(car.transform);
                return;
            }
        }

        GameObject[] players = GameObject.FindGameObjectsWithTag(targetTag);
        foreach (GameObject player in players)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && (netObj.IsOwner || netObj.IsLocalPlayer))
            {
                SetTarget(player.transform);
                return;
            }
        }

        // Singleplayer only fallback (never lock onto other players in multiplayer)
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (cars.Length > 0 && target == null)
            {
                SetTarget(cars[0].transform);
            }
            else if (players.Length > 0 && target == null)
            {
                SetTarget(players[0].transform);
            }
        }
    }

    private void LateUpdate()
    {
        if (CameraZoom2D.Instance != null && CameraZoom2D.Instance.IsZooming)
        {
            return;
        }

        if (target == null)
        {
            FindTargetByTag();
            if (target == null) return;
        }

        // In multiplayer, ensure camera NEVER follows opponent's car
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkObject netObj = target.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsOwner && !netObj.IsLocalPlayer)
            {
                target = null;
                FindTargetByTag();
                if (target == null) return;
            }
        }

        if (targetRb == null && target != null)
        {
            targetRb = target.GetComponent<Rigidbody2D>();
        }

        Vector3 desiredPosition = target.position + offset;

        // Velocity lookahead for smooth dynamic camera anticipation
        if (targetRb != null && lookaheadFactor > 0f)
        {
            Vector2 targetLookahead = targetRb.linearVelocity * lookaheadFactor;
            currentLookahead = Vector2.Lerp(currentLookahead, targetLookahead, 1f - Mathf.Exp(-lookaheadDamping * Time.deltaTime));
        }
        else
        {
            currentLookahead = Vector2.Lerp(currentLookahead, Vector2.zero, 1f - Mathf.Exp(-lookaheadDamping * Time.deltaTime));
        }

        desiredPosition.x += currentLookahead.x;
        desiredPosition.y += currentLookahead.y;

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
            Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * currentShakeIntensity;
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
            if (confineScreenEdges && cam != null && cam.orthographic)
            {
                float fallbackMinX = minX + halfWidth;
                float fallbackMaxX = maxX - halfWidth;
                float fallbackMinY = minY + halfHeight;
                float fallbackMaxY = maxY - halfHeight;

                pos.x = fallbackMinX > fallbackMaxX ? (minX + maxX) * 0.5f : Mathf.Clamp(pos.x, fallbackMinX, fallbackMaxX);
                pos.y = fallbackMinY > fallbackMaxY ? (minY + maxY) * 0.5f : Mathf.Clamp(pos.y, fallbackMinY, fallbackMaxY);
            }
            else
            {
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
            }
            return pos;
        }

        // PolygonCollider2D / CompositeCollider2D geometric boundary solver
        if (confinerCollider is PolygonCollider2D poly || confinerCollider is CompositeCollider2D)
        {
            // Initial AABB clamp to prevent runaway extrapolation
            Bounds polyBounds = confinerCollider.bounds;
            pos.x = Mathf.Clamp(pos.x, polyBounds.min.x, polyBounds.max.x);
            pos.y = Mathf.Clamp(pos.y, polyBounds.min.y, polyBounds.max.y);

            // 1. Center containment check
            if (!confinerCollider.OverlapPoint(pos))
            {
                Vector2 closest = confinerCollider.ClosestPoint(pos);
                pos.x = closest.x;
                pos.y = closest.y;
            }

            // 2. Viewport Screen Edge confinement for polygon geometry
            if (confineScreenEdges && cam != null && cam.orthographic && halfWidth > 0f && halfHeight > 0f)
            {
                // Multi-pass directional boundary relaxation
                for (int pass = 0; pass < 5; pass++)
                {
                    float shiftLeft = 0f;
                    float shiftRight = 0f;
                    float shiftBottom = 0f;
                    float shiftTop = 0f;

                    // Sample along left edge
                    for (float f = -1f; f <= 1f; f += 0.5f)
                    {
                        Vector2 pt = new Vector2(pos.x - halfWidth, pos.y + (f * halfHeight));
                        if (!confinerCollider.OverlapPoint(pt))
                        {
                            Vector2 cl = confinerCollider.ClosestPoint(pt);
                            float diff = cl.x - pt.x;
                            if (diff > 0f) shiftLeft = Mathf.Max(shiftLeft, diff);
                        }
                    }

                    // Sample along right edge
                    for (float f = -1f; f <= 1f; f += 0.5f)
                    {
                        Vector2 pt = new Vector2(pos.x + halfWidth, pos.y + (f * halfHeight));
                        if (!confinerCollider.OverlapPoint(pt))
                        {
                            Vector2 cl = confinerCollider.ClosestPoint(pt);
                            float diff = pt.x - cl.x;
                            if (diff > 0f) shiftRight = Mathf.Max(shiftRight, diff);
                        }
                    }

                    // Sample along bottom edge
                    for (float f = -1f; f <= 1f; f += 0.5f)
                    {
                        Vector2 pt = new Vector2(pos.x + (f * halfWidth), pos.y - halfHeight);
                        if (!confinerCollider.OverlapPoint(pt))
                        {
                            Vector2 cl = confinerCollider.ClosestPoint(pt);
                            float diff = cl.y - pt.y;
                            if (diff > 0f) shiftBottom = Mathf.Max(shiftBottom, diff);
                        }
                    }

                    // Sample along top edge
                    for (float f = -1f; f <= 1f; f += 0.5f)
                    {
                        Vector2 pt = new Vector2(pos.x + (f * halfWidth), pos.y + halfHeight);
                        if (!confinerCollider.OverlapPoint(pt))
                        {
                            Vector2 cl = confinerCollider.ClosestPoint(pt);
                            float diff = pt.y - cl.y;
                            if (diff > 0f) shiftTop = Mathf.Max(shiftTop, diff);
                        }
                    }

                    // Apply horizontal correction
                    if (shiftLeft > 0f && shiftRight > 0f)
                    {
                        // Corridor narrower than camera: center between walls
                        pos.x += (shiftLeft - shiftRight) * 0.5f;
                    }
                    else if (shiftLeft > 0f)
                    {
                        pos.x += shiftLeft;
                    }
                    else if (shiftRight > 0f)
                    {
                        pos.x -= shiftRight;
                    }

                    // Apply vertical correction
                    if (shiftBottom > 0f && shiftTop > 0f)
                    {
                        // Corridor shorter than camera: center between walls
                        pos.y += (shiftBottom - shiftTop) * 0.5f;
                    }
                    else if (shiftBottom > 0f)
                    {
                        pos.y += shiftBottom;
                    }
                    else if (shiftTop > 0f)
                    {
                        pos.y -= shiftTop;
                    }

                    if (shiftLeft <= 0.001f && shiftRight <= 0.001f && shiftBottom <= 0.001f && shiftTop <= 0.001f)
                    {
                        break;
                    }
                }

                // Ensure center never leaves polygon
                if (!confinerCollider.OverlapPoint(pos))
                {
                    Vector2 closest = confinerCollider.ClosestPoint(pos);
                    pos.x = closest.x;
                    pos.y = closest.y;
                }
            }

            return pos;
        }

        // Standard Box / Axis-Aligned Collider Bounds
        Bounds b = confinerCollider.bounds;
        float boundMinX = b.min.x + (confineScreenEdges ? halfWidth : 0f);
        float boundMaxX = b.max.x - (confineScreenEdges ? halfWidth : 0f);
        float boundMinY = b.min.y + (confineScreenEdges ? halfHeight : 0f);
        float boundMaxY = b.max.y - (confineScreenEdges ? halfHeight : 0f);

        pos.x = boundMinX > boundMaxX ? b.center.x : Mathf.Clamp(pos.x, boundMinX, boundMaxX);
        pos.y = boundMinY > boundMaxY ? b.center.y : Mathf.Clamp(pos.y, boundMinY, boundMaxY);

        return pos;
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableConfiner) return;

        if (cam == null) cam = GetComponent<Camera>();
        FindConfinerInScene();
        UpdateBoundsFromCollider();

        if (confinerCollider is PolygonCollider2D poly)
        {
            Gizmos.color = Color.green;
            Transform t = poly.transform;
            for (int pathIdx = 0; pathIdx < poly.pathCount; pathIdx++)
            {
                Vector2[] pts = poly.GetPath(pathIdx);
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 p1 = t.TransformPoint(pts[i]);
                    Vector3 p2 = t.TransformPoint(pts[(i + 1) % pts.Length]);
                    p1.z = transform.position.z;
                    p2.z = transform.position.z;
                    Gizmos.DrawLine(p1, p2);
                }
            }
            return;
        }

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