using System.Collections;
using UnityEngine;

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
        EnterIdleState();
    }

    private void Update()
    {
        if (currentState == SheepState.Knocked || currentState == SheepState.Recovering)
        {
            // State transitions handled by coroutines
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

    private void UpdateIdle()
    {
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

        Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
        targetPosition = originPosition + randomOffset;

        UpdateFacingDirection(targetPosition.x - transform.position.x);
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

        UpdateFacingDirection(direction.x);
        Vector2 moveStep = direction.normalized * (walkSpeed * Time.deltaTime);
        transform.position = currentPos + moveStep;
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
        if (stateTimer <= 0f)
        {
            EnterWalkingState();
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

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleCarImpact(collision.gameObject, collision.relativeVelocity);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleCarImpact(other.gameObject, Vector2.zero);
    }

    private void HandleCarImpact(GameObject hitObj, Vector2 relativeVel)
    {
        if (currentState == SheepState.Knocked || currentState == SheepState.Recovering) return;

        bool isPlayer = hitObj.CompareTag("Player") ||
                        hitObj.GetComponentInParent<CarControllerSingle>() != null ||
                        hitObj.GetComponentInParent<NetworkCarController>() != null ||
                        hitObj.GetComponentInParent<CarHealth>() != null;

        if (!isPlayer) return;

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