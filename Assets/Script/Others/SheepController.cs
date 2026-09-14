using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class SheepController : MonoBehaviour
{
    public enum SheepState
    {
        Idle,
        Walking,
        Eating,
        Knocked,
        Recovering
    }

    [Header("State")]
    [SerializeField] private SheepState currentState = SheepState.Idle;

    [Header("Movement & Roam")]
    [SerializeField] private float walkSpeed = 1.2f;
    [SerializeField] private float wanderRadius = 4.0f;
    [SerializeField] private float minIdleDuration = 2.0f;
    [SerializeField] private float maxIdleDuration = 4.5f;
    [SerializeField] private float minWalkDuration = 2.0f;
    [SerializeField] private float maxWalkDuration = 5.0f;
    [SerializeField] private float minEatDuration = 3.0f;
    [SerializeField] private float maxEatDuration = 6.0f;
    [Range(0f, 1f)]
    [SerializeField] private float eatProbability = 0.45f;

    [Header("Rotation & Facing")]
    [SerializeField] private bool rotateInMovementDirection = true;
    [SerializeField] private float turnSpeed = 720.0f;
    [SerializeField] private bool baseFacingLeft = true;
    [SerializeField] private float rotationOffsetAngle = 0.0f;

    [Header("Knockback & Recovery")]
    [SerializeField] private float knockbackForce = 12.0f;
    [SerializeField] private float knockbackTorque = 180.0f;
    [SerializeField] private float airborneScaleMultiplier = 0.85f; // 0.15 (15%) size reduction during flight
    [SerializeField] private float recoveryDuration = 0.5f;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private AudioClip hitSound;

    [Header("Visuals & Animation")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private bool flipXFacingLeft = true;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 3;

    [Header("Procedural Animation (When No Animator Controller)")]
    [SerializeField] private bool useProceduralAnimation = true;
    [SerializeField] private float walkBobSpeed = 8.0f;
    [SerializeField] private float walkBobAmount = 0.08f;
    [SerializeField] private float eatBobSpeed = 4.0f;
    [SerializeField] private float eatBobAmount = 0.12f;

    private Rigidbody2D rb;
    private Collider2D col;
    private Vector2 originPosition;
    private Vector2 targetPosition;
    private float stateTimer;
    private Vector3 initialScale;
    private Vector3 initialVisualPos;
    private Transform visualTransform;
    private bool isKnocked;
    private Coroutine recoveryCoroutine;

    // Surface Cache
    private readonly List<Tilemap> groundTilemaps = new List<Tilemap>();
    private readonly List<Tilemap> boundaryTilemaps = new List<Tilemap>();
    private readonly List<Tilemap> holeTilemaps = new List<Tilemap>();
    private readonly List<Collider2D> groundColliders = new List<Collider2D>();
    private readonly List<Collider2D> boundaryColliders = new List<Collider2D>();
    private readonly List<Collider2D> holeColliders = new List<Collider2D>();
    private float lastTilemapCacheTime = -10f;

    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
    private static readonly int IsEatingHash = Animator.StringToHash("isEating");
    private static readonly int IsIdleHash = Animator.StringToHash("isIdle");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        spriteRenderer ??= GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        animator ??= GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        visualTransform = spriteRenderer?.transform;

        originPosition = transform.position;
        initialScale = transform.localScale;
        initialVisualPos = visualTransform != null ? visualTransform.localPosition : Vector3.zero;

        if (spriteRenderer != null)
        {
            if (!string.IsNullOrEmpty(sortingLayerName))
            {
                spriteRenderer.sortingLayerName = sortingLayerName;
            }
            spriteRenderer.sortingOrder = sortingOrder;
        }

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void Start()
    {
        CacheSceneSurfaces(true);
        EnterIdleState();
    }

    private void Update()
    {
        // 1. If knocked or recovering, check if landed in a hole
        if (currentState == SheepState.Knocked)
        {
            if (rb != null && rb.linearVelocity.sqrMagnitude < 0.1f && IsPositionOnHole(transform.position))
            {
                RespawnAtOrigin();
            }
            return;
        }

        if (currentState == SheepState.Recovering)
        {
            if (IsPositionOnHole(transform.position))
            {
                RespawnAtOrigin();
            }
            return;
        }

        // 2. Continuous Hole Check: If on a hole tilemap at any time, respawn back at origin
        if (IsPositionOnHole(transform.position))
        {
            RespawnAtOrigin();
            return;
        }

        stateTimer -= Time.deltaTime;

        switch (currentState)
        {
            case SheepState.Idle:
                UpdateIdle();
                break;
            case SheepState.Walking:
                UpdateWalking();
                break;
            case SheepState.Eating:
                UpdateEating();
                break;
        }

        UpdateProceduralVisuals();
    }

    private void EnterIdleState()
    {
        currentState = SheepState.Idle;
        stateTimer = Random.Range(minIdleDuration, maxIdleDuration);
        if (rb != null) rb.linearVelocity = Vector2.zero;
        UpdateAnimatorParams();
    }

    private bool isFacingRight = false;

    private void UpdateIdle()
    {
        // Smoothly return body rotation upright while idle
        if (transform.rotation != Quaternion.identity)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.identity, turnSpeed * Time.deltaTime);
        }

        if (stateTimer <= 0f)
        {
            if (Random.value < eatProbability)
            {
                EnterEatingState();
            }
            else
            {
                EnterWalkingState();
            }
        }
    }

    private void EnterWalkingState()
    {
        currentState = SheepState.Walking;
        stateTimer = Random.Range(minWalkDuration, maxWalkDuration);

        targetPosition = PickValidWanderDestination();

        Vector2 direction = targetPosition - (Vector2)transform.position;
        ApplyRotationAndFacing(direction);
        UpdateAnimatorParams();
    }

    private void UpdateWalking()
    {
        Vector2 currentPos = transform.position;
        Vector2 direction = targetPosition - currentPos;
        float distance = direction.magnitude;

        if (distance <= 0.15f || stateTimer <= 0f)
        {
            if (Random.value < eatProbability)
            {
                EnterEatingState();
            }
            else
            {
                EnterIdleState();
            }
            return;
        }

        Vector2 moveDir = direction.normalized;
        ApplyRotationAndFacing(direction);

        float stepDist = walkSpeed * Time.deltaTime;
        Vector2 moveStep = moveDir * stepDist;
        Vector2 nextPos = currentPos + moveStep;

        // Lookahead 0.35f in front of movement to stop BEFORE reaching hole or stepping off ground/boundary
        Vector2 lookAheadPos = currentPos + moveDir * (stepDist + 0.35f);

        if (IsPositionOnHole(nextPos) || IsPositionOnHole(lookAheadPos))
        {
            if (IsPositionOnHole(currentPos))
            {
                RespawnAtOrigin();
                return;
            }
            EnterIdleState();
            return;
        }

        if (!IsPositionWalkable(nextPos) || !IsPositionWalkable(lookAheadPos))
        {
            // Reached edge of valid ground/boundary: stop walking and enter idle
            EnterIdleState();
            return;
        }

        transform.position = nextPos;
    }

    private void EnterEatingState()
    {
        currentState = SheepState.Eating;
        stateTimer = Random.Range(minEatDuration, maxEatDuration);
        if (rb != null) rb.linearVelocity = Vector2.zero;
        UpdateAnimatorParams();
    }

    private void UpdateEating()
    {
        // Smoothly return body rotation upright while eating
        if (transform.rotation != Quaternion.identity)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.identity, turnSpeed * Time.deltaTime);
        }

        if (stateTimer <= 0f)
        {
            EnterWalkingState();
        }
    }

    private void ApplyRotationAndFacing(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;

        // 1. Determine horizontal facing (Left vs Right)
        if (direction.x > 0.05f)
        {
            isFacingRight = true;
        }
        else if (direction.x < -0.05f)
        {
            isFacingRight = false;
        }

        // 2. Set sprite horizontal flip
        if (spriteRenderer != null)
        {
            // Base sprite faces Left:
            // Moving Right (isFacingRight == true) -> flipX = true
            // Moving Left (isFacingRight == false) -> flipX = false
            spriteRenderer.flipX = flipXFacingLeft ? isFacingRight : !isFacingRight;
        }

        // 3. Smooth directional tilt/rotation without ever going upside-down
        if (rotateInMovementDirection)
        {
            float moveAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float targetRotZ;

            if (isFacingRight)
            {
                // Head faces East (0 deg) when flipped Right.
                // Angle for right-half plane is within [-90, +90] deg.
                targetRotZ = moveAngle;
            }
            else
            {
                // Head faces West (180 deg) when unflipped (Left).
                // Angle for left-half plane relative to West (180 deg).
                targetRotZ = Mathf.DeltaAngle(180f, moveAngle);
            }

            // Smoothly rotate body towards target angle
            float currentRotZ = transform.eulerAngles.z;
            float smoothRotZ = Mathf.MoveTowardsAngle(currentRotZ, targetRotZ, turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, smoothRotZ);
        }
    }

    private void UpdateFacingDirection(float moveDeltaX)
    {
        if (Mathf.Abs(moveDeltaX) < 0.01f || spriteRenderer == null) return;

        bool facingLeft = moveDeltaX < 0f;
        spriteRenderer.flipX = flipXFacingLeft ? !facingLeft : facingLeft;
    }

    private void UpdateAnimatorParams()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;

        animator.SetBool(IsWalkingHash, currentState == SheepState.Walking);
        animator.SetBool(IsEatingHash, currentState == SheepState.Eating);
        animator.SetBool(IsIdleHash, currentState == SheepState.Idle);
    }

    private void UpdateProceduralVisuals()
    {
        if (!useProceduralAnimation || visualTransform == null) return;

        if (currentState == SheepState.Walking)
        {
            float bob = Mathf.Sin(Time.time * walkBobSpeed) * walkBobAmount;
            visualTransform.localPosition = initialVisualPos + new Vector3(0f, Mathf.Abs(bob), 0f);
            float tilt = Mathf.Sin(Time.time * walkBobSpeed * 0.5f) * 4f;
            visualTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }
        else if (currentState == SheepState.Eating)
        {
            float headDip = Mathf.Sin(Time.time * eatBobSpeed) * eatBobAmount;
            visualTransform.localPosition = initialVisualPos + new Vector3(0f, -Mathf.Abs(headDip), 0f);
            float chewRot = Mathf.Sin(Time.time * eatBobSpeed * 2f) * 2.5f;
            visualTransform.localRotation = Quaternion.Euler(0f, 0f, chewRot);
        }
        else
        {
            float breathe = Mathf.Sin(Time.time * 2.5f) * 0.02f;
            visualTransform.localPosition = initialVisualPos + new Vector3(0f, breathe, 0f);
            visualTransform.localRotation = Quaternion.identity;
        }
    }

    #region Surface and Walkability Queries

    public void CacheSceneSurfaces(bool force = false)
    {
        if (!force && Time.time - lastTilemapCacheTime < 3f && (groundTilemaps.Count > 0 || boundaryTilemaps.Count > 0 || holeTilemaps.Count > 0))
        {
            return;
        }

        lastTilemapCacheTime = Time.time;
        groundTilemaps.Clear();
        boundaryTilemaps.Clear();
        holeTilemaps.Clear();
        groundColliders.Clear();
        boundaryColliders.Clear();
        holeColliders.Clear();

        Tilemap[] allTilemaps = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var tm in allTilemaps)
        {
            if (tm == null) continue;
            GameObject go = tm.gameObject;

            if (IsHoleObject(go))
            {
                holeTilemaps.Add(tm);
            }
            else if (IsBoundaryObject(go))
            {
                boundaryTilemaps.Add(tm);
            }
            else if (IsGroundObject(go))
            {
                groundTilemaps.Add(tm);
            }
        }

        Collider2D[] allColliders = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var c in allColliders)
        {
            if (c == null || c.gameObject == gameObject) continue;
            GameObject go = c.gameObject;

            if (IsHoleObject(go))
            {
                holeColliders.Add(c);
            }
            else if (IsBoundaryObject(go))
            {
                boundaryColliders.Add(c);
            }
            else if (IsGroundObject(go))
            {
                groundColliders.Add(c);
            }
        }
    }

    public bool IsHoleObject(GameObject go)
    {
        if (go == null) return false;

        // Exclude purely visual background tilemaps/objects explicitly
        string objName = go.name.ToLower();
        if (objName.Contains("background") || objName.Contains("waterbackground"))
        {
            return false;
        }

        Transform current = go.transform;
        while (current != null)
        {
            GameObject candidate = current.gameObject;
            string cName = candidate.name.ToLower();
            if (cName.Contains("background") || cName.Contains("waterbackground"))
            {
                return false;
            }

            if (candidate.CompareTag("Hole")) return true;

            int layer = candidate.layer;
            string layerName = LayerMask.LayerToName(layer);
            if (!string.IsNullOrEmpty(layerName) && (string.Equals(layerName, "Hole", System.StringComparison.OrdinalIgnoreCase) || string.Equals(layerName, "Water", System.StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Check if it's named Hole/Holes with a Collider or Tilemap
            if (cName == "hole" || cName == "holes" || cName == "water" || cName.StartsWith("hole") || cName.StartsWith("water"))
            {
                if (candidate.GetComponent<Collider2D>() != null || candidate.GetComponent<Tilemap>() != null)
                {
                    return true;
                }
            }

            current = current.parent;
        }

        if (go.TryGetComponent<Rigidbody2D>(out var rbComp) && rbComp.gameObject != go)
        {
            return IsHoleObject(rbComp.gameObject);
        }

        return false;
    }

    public bool IsBoundaryObject(GameObject go)
    {
        if (go == null) return false;

        Transform current = go.transform;
        while (current != null)
        {
            GameObject candidate = current.gameObject;
            if (candidate.CompareTag("Boundary")) return true;
            string layerName = LayerMask.LayerToName(candidate.layer);
            if (!string.IsNullOrEmpty(layerName) && string.Equals(layerName, "Boundary", System.StringComparison.OrdinalIgnoreCase)) return true;
            string n = candidate.name.ToLower();
            if (n.Contains("boundary")) return true;
            current = current.parent;
        }

        return false;
    }

    public bool IsGroundObject(GameObject go)
    {
        if (go == null) return false;

        Transform current = go.transform;
        while (current != null)
        {
            GameObject candidate = current.gameObject;
            if (candidate.CompareTag("Ground")) return true;
            string layerName = LayerMask.LayerToName(candidate.layer);
            if (!string.IsNullOrEmpty(layerName) && string.Equals(layerName, "Ground", System.StringComparison.OrdinalIgnoreCase)) return true;
            string n = candidate.name.ToLower();
            if (n.Contains("ground") || n.Contains("road") || n.Contains("track")) return true;
            current = current.parent;
        }

        return false;
    }

    public bool IsPositionOnHole(Vector2 pos)
    {
        CacheSceneSurfaces();

        // 1. Check Hole Tilemaps (using cell lookup)
        for (int i = 0; i < holeTilemaps.Count; i++)
        {
            Tilemap tm = holeTilemaps[i];
            if (tm != null && tm.isActiveAndEnabled)
            {
                Vector3Int cell = tm.WorldToCell(pos);
                if (tm.HasTile(cell)) return true;

                // Also check perimeter offsets to cover sheep radius (0.25f)
                if (tm.HasTile(tm.WorldToCell(pos + new Vector2(0.25f, 0f))) ||
                    tm.HasTile(tm.WorldToCell(pos + new Vector2(-0.25f, 0f))) ||
                    tm.HasTile(tm.WorldToCell(pos + new Vector2(0f, 0.25f))) ||
                    tm.HasTile(tm.WorldToCell(pos + new Vector2(0f, -0.25f))))
                {
                    return true;
                }
            }
        }

        // 2. Check 2D Physics OverlapPointAll
        Collider2D[] hits = Physics2D.OverlapPointAll(pos);
        if (hits != null)
        {
            foreach (var hit in hits)
            {
                if (hit != null && hit.gameObject != gameObject && IsHoleObject(hit.gameObject)) return true;
            }
        }

        // 3. Check OverlapCircleAll with sheep radius
        Collider2D[] circleHits = Physics2D.OverlapCircleAll(pos, 0.35f);
        if (circleHits != null)
        {
            foreach (var hit in circleHits)
            {
                if (hit != null && hit.gameObject != gameObject && IsHoleObject(hit.gameObject)) return true;
            }
        }

        return false;
    }

    public bool IsPositionWalkable(Vector2 pos)
    {
        // 1. Hole is NEVER walkable
        if (IsPositionOnHole(pos)) return false;

        // 2. Trap is NEVER walkable
        Collider2D[] trapCheck = Physics2D.OverlapCircleAll(pos, 0.35f);
        if (trapCheck != null)
        {
            foreach (var hit in trapCheck)
            {
                if (hit != null && hit.gameObject != gameObject)
                {
                    if (hit.CompareTag("Trap") || hit.gameObject.name.ToLower().Contains("trap") || hit.gameObject.name.ToLower().Contains("saw") || hit.gameObject.name.ToLower().Contains("spike"))
                    {
                        return false;
                    }
                }
            }
        }

        CacheSceneSurfaces();

        // 3. Must be on Ground or Boundary Tilemap / Collider
        bool onGroundOrBoundary = false;

        // Check Boundary Tilemaps
        for (int i = 0; i < boundaryTilemaps.Count; i++)
        {
            Tilemap tm = boundaryTilemaps[i];
            if (tm != null && tm.isActiveAndEnabled)
            {
                Vector3Int cell = tm.WorldToCell(pos);
                if (tm.HasTile(cell))
                {
                    onGroundOrBoundary = true;
                    break;
                }
            }
        }

        // Check Ground Tilemaps
        if (!onGroundOrBoundary)
        {
            for (int i = 0; i < groundTilemaps.Count; i++)
            {
                Tilemap tm = groundTilemaps[i];
                if (tm != null && tm.isActiveAndEnabled)
                {
                    Vector3Int cell = tm.WorldToCell(pos);
                    if (tm.HasTile(cell))
                    {
                        onGroundOrBoundary = true;
                        break;
                    }
                }
            }
        }

        // Check Boundary Colliders
        if (!onGroundOrBoundary)
        {
            for (int i = 0; i < boundaryColliders.Count; i++)
            {
                Collider2D c = boundaryColliders[i];
                if (c != null && c.isActiveAndEnabled && c.OverlapPoint(pos))
                {
                    onGroundOrBoundary = true;
                    break;
                }
            }
        }

        // Check Ground Colliders
        if (!onGroundOrBoundary)
        {
            for (int i = 0; i < groundColliders.Count; i++)
            {
                Collider2D c = groundColliders[i];
                if (c != null && c.isActiveAndEnabled && c.OverlapPoint(pos))
                {
                    onGroundOrBoundary = true;
                    break;
                }
            }
        }

        // Check Point and Circle Overlaps for Ground / Boundary
        if (!onGroundOrBoundary)
        {
            Collider2D[] pointHits = Physics2D.OverlapPointAll(pos);
            if (pointHits != null)
            {
                foreach (var hit in pointHits)
                {
                    if (hit != null && hit.gameObject != gameObject && (IsBoundaryObject(hit.gameObject) || IsGroundObject(hit.gameObject)))
                    {
                        onGroundOrBoundary = true;
                        break;
                    }
                }
            }
        }

        if (!onGroundOrBoundary)
        {
            Collider2D[] circleHits = Physics2D.OverlapCircleAll(pos, 0.25f);
            if (circleHits != null)
            {
                foreach (var hit in circleHits)
                {
                    if (hit != null && hit.gameObject != gameObject && (IsBoundaryObject(hit.gameObject) || IsGroundObject(hit.gameObject)))
                    {
                        onGroundOrBoundary = true;
                        break;
                    }
                }
            }
        }

        return onGroundOrBoundary;
    }

    private Vector2 PickValidWanderDestination()
    {
        Vector2 bestCandidate = originPosition;
        bool found = false;

        // Sample candidate points within wanderRadius
        for (int i = 0; i < 30; i++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
            if (randomOffset.sqrMagnitude < 0.25f) randomOffset = randomOffset.normalized * 0.5f;

            Vector2 candidate = originPosition + randomOffset;

            if (IsPositionWalkable(candidate))
            {
                if (!IsPathCrossingHole(transform.position, candidate))
                {
                    bestCandidate = candidate;
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            Vector2 toOrigin = (originPosition - (Vector2)transform.position);
            if (toOrigin.magnitude > 0.5f)
            {
                Vector2 stepTowardsOrigin = (Vector2)transform.position + toOrigin.normalized * 1.0f;
                if (IsPositionWalkable(stepTowardsOrigin) && !IsPathCrossingHole(transform.position, stepTowardsOrigin))
                {
                    bestCandidate = stepTowardsOrigin;
                }
                else if (IsPositionWalkable(originPosition))
                {
                    bestCandidate = originPosition;
                }
                else
                {
                    bestCandidate = transform.position;
                }
            }
            else
            {
                bestCandidate = transform.position;
            }
        }

        return bestCandidate;
    }

    private bool IsPathCrossingHole(Vector2 from, Vector2 to)
    {
        int samples = 10;
        for (int i = 1; i <= samples; i++)
        {
            float t = (float)i / (samples + 1);
            Vector2 point = Vector2.Lerp(from, to, t);
            if (IsPositionOnHole(point) || !IsPositionWalkable(point))
            {
                return true;
            }
        }
        return false;
    }

    public void RespawnAtOrigin()
    {
        Debug.Log($"[SheepController] Sheep entered Hole! Respawning back at origin {originPosition}");

        if (recoveryCoroutine != null)
        {
            StopCoroutine(recoveryCoroutine);
            recoveryCoroutine = null;
        }

        Vector3 currentPos = transform.position;

        // Spawn splash effect at hole location
        GameObject holeFxPrefab = Resources.Load<GameObject>("SplashEffect") ?? Resources.Load<GameObject>("Smoke") ?? hitEffectPrefab;
        if (holeFxPrefab != null)
        {
            GameObject fx = Instantiate(holeFxPrefab, currentPos, Quaternion.identity);
            Destroy(fx, 2.0f);
        }

        // Teleport back to origin
        transform.position = originPosition;
        transform.rotation = Quaternion.identity;
        transform.localScale = initialScale;

        if (visualTransform != null)
        {
            visualTransform.localPosition = initialVisualPos;
            visualTransform.localRotation = Quaternion.identity;
        }

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (col != null) col.enabled = true;
        if (animator != null) animator.enabled = true;

        // Spawn respawn smoke effect at origin
        GameObject spawnFx = hitEffectPrefab != null ? hitEffectPrefab : Resources.Load<GameObject>("Smoke");
        if (spawnFx != null)
        {
            GameObject fx2 = Instantiate(spawnFx, (Vector3)originPosition, Quaternion.identity);
            Destroy(fx2, 2.0f);
        }

        isKnocked = false;
        EnterIdleState();
    }

    #endregion

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsHoleObject(collision.gameObject))
        {
            RespawnAtOrigin();
            return;
        }
        HandleCarImpact(collision.gameObject, collision.relativeVelocity);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsHoleObject(collision.gameObject))
        {
            RespawnAtOrigin();
            return;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsHoleObject(other.gameObject))
        {
            RespawnAtOrigin();
            return;
        }
        HandleCarImpact(other.gameObject, Vector2.zero);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (IsHoleObject(other.gameObject))
        {
            RespawnAtOrigin();
            return;
        }
    }

    private void HandleCarImpact(GameObject hitObj, Vector2 relativeVel)
    {
        if (currentState == SheepState.Knocked || currentState == SheepState.Recovering) return;

        NetworkCarController netCar = hitObj.GetComponentInParent<NetworkCarController>();
        CarControllerSingle singleCar = hitObj.GetComponentInParent<CarControllerSingle>();

        if (netCar == null && singleCar == null)
        {
            CarHealth health = hitObj.GetComponentInParent<CarHealth>();
            if (health != null)
            {
                netCar = health.GetComponent<NetworkCarController>();
                singleCar = health.GetComponent<CarControllerSingle>();
            }
        }

        bool isPlayer = hitObj.CompareTag("Player") || netCar != null || singleCar != null;
        if (!isPlayer) return;

        if (singleCar != null)
        {
            singleCar.ApplySheepSlowDebuff(1.0f, 0.45f);
        }

        if (netCar != null)
        {
            if (netCar.IsOwner)
            {
                netCar.ApplySheepSlowDebuff(1.0f, 0.45f);
                netCar.SyncSheepKnockedRpc(gameObject.name, transform.position, hitObj.transform.position, relativeVel);
            }
        }

        KnockFromCarHit(hitObj.transform.position, relativeVel);
    }

    public void KnockFromCarHit(Vector3 carPosition, Vector2 carVelocity)
    {
        if (currentState == SheepState.Knocked || currentState == SheepState.Recovering) return;

        isKnocked = true;
        currentState = SheepState.Knocked;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1.6f;
            rb.linearDamping = 0.25f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            Vector2 hitDir = (transform.position - carPosition).normalized;
            if (hitDir.sqrMagnitude < 0.01f) hitDir = Vector2.up;

            Vector2 impulse = (hitDir * 0.7f + Vector2.up * 1.1f).normalized * knockbackForce;
            rb.linearVelocity = impulse;
            rb.angularVelocity = Random.Range(-240f, 240f);
        }

        if (animator != null)
        {
            animator.enabled = false;
        }

        if (col != null)
        {
            col.enabled = false;
        }

        Vector3 effectPos = transform.position;
        if (hitEffectPrefab == null)
        {
            hitEffectPrefab = Resources.Load<GameObject>("Smoke");
            if (hitEffectPrefab == null) hitEffectPrefab = Resources.Load<GameObject>("DeathEffect");
        }

        if (hitEffectPrefab != null)
        {
            GameObject fx = Instantiate(hitEffectPrefab, effectPos, Quaternion.identity);
            Destroy(fx, 2.5f);
        }

        if (hitSound != null)
        {
            float vol = AudioManager.Instance != null ? AudioManager.Instance.GetSfxVolume() : 1.0f;
            AudioSource.PlayClipAtPoint(hitSound, effectPos, vol);
        }

        if (recoveryCoroutine != null)
        {
            StopCoroutine(recoveryCoroutine);
        }
        recoveryCoroutine = StartCoroutine(KnockbackAndRecoveryRoutine());
    }

    private IEnumerator KnockbackAndRecoveryRoutine()
    {
        // 1. Airborne flight: smoothly shrink scale while flying
        float flightDuration = 0.75f;
        float elapsedFlight = 0f;
        Vector3 targetShrunkScale = initialScale * airborneScaleMultiplier;

        while (elapsedFlight < flightDuration)
        {
            elapsedFlight += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedFlight / flightDuration);
            transform.localScale = Vector3.Lerp(initialScale, targetShrunkScale, t);
            yield return null;
        }

        // Wait a short moment for landing physics
        yield return new WaitForSeconds(0.2f);

        // Check if landed in a hole during knockback
        if (IsPositionOnHole(transform.position))
        {
            RespawnAtOrigin();
            yield break;
        }

        // 2. Settle on ground: stop physics movement
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // 3. Regain original scale and rotate upright
        float regainDuration = 0.45f;
        float elapsedRegain = 0f;
        Vector3 currentScale = transform.localScale;
        Quaternion currentRot = transform.rotation;

        while (elapsedRegain < regainDuration)
        {
            elapsedRegain += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedRegain / regainDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            transform.localScale = Vector3.Lerp(currentScale, initialScale, smoothT);
            transform.rotation = Quaternion.Slerp(currentRot, Quaternion.identity, smoothT);
            yield return null;
        }

        transform.localScale = initialScale;
        transform.rotation = Quaternion.identity;

        // 4. Recovering pause: wait for 2 seconds before resuming tasks
        currentState = SheepState.Recovering;
        yield return new WaitForSeconds(2.0f);

        // 5. Re-enable collider, animator, and resume normal tasks
        if (col != null)
        {
            col.enabled = true;
        }

        if (animator != null)
        {
            animator.enabled = true;
        }

        isKnocked = false;
        EnterIdleState();
    }

    private void OnDisable()
    {
        if (recoveryCoroutine != null)
        {
            StopCoroutine(recoveryCoroutine);
            recoveryCoroutine = null;
        }
    }
}