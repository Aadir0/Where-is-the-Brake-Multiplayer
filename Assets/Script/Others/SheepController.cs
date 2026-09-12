using System.Collections;
using System.Collections.Generic;
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
        Dead
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

    [Header("Knockback & Death")]
    [SerializeField] private float knockbackForce = 12.0f;
    [SerializeField] private float knockbackTorque = 180.0f;
    [SerializeField] private float despawnDelay = 1.2f;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private AudioClip hitSound;

    [Header("Visuals & Animation")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private bool flipXFacingLeft = true;

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
    private float stateTimer = 0f;
    private Vector3 initialScale;
    private Vector3 initialVisualPos;
    private bool isDead = false;

    // Animator Hashes
    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
    private static readonly int IsEatingHash = Animator.StringToHash("isEating");
    private static readonly int IsIdleHash = Animator.StringToHash("isIdle");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        originPosition = transform.position;
        initialScale = transform.localScale;
        if (spriteRenderer != null)
        {
            initialVisualPos = spriteRenderer.transform.localPosition;
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
        if (isDead) return;

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
        Vector2 direction = (targetPosition - currentPos);
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

        // Update facing direction based on movement delta each frame
        UpdateFacingDirection(direction.x);
        // Apply movement
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
        if (Mathf.Abs(moveDeltaX) < 0.01f) return;

        bool facingLeft = moveDeltaX < 0f;
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = flipXFacingLeft ? !facingLeft : facingLeft;
        }
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
        if (!useProceduralAnimation || spriteRenderer == null) return;

        Transform visualTransform = spriteRenderer.transform;
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
        if (isDead) return;

        bool isPlayer = hitObj.CompareTag("Player") ||
                        hitObj.GetComponentInParent<CarControllerSingle>() != null ||
                        hitObj.GetComponentInParent<NetworkCarController>() != null ||
                        hitObj.GetComponentInParent<CarHealth>() != null;

        if (!isPlayer) return;

        DieFromCarHit(hitObj.transform.position, relativeVel);
    }

    public void DieFromCarHit(Vector3 carPosition, Vector2 carVelocity)
    {
        if (isDead) return;
        isDead = true;
        currentState = SheepState.Dead;

        if (col != null) col.enabled = false;

        // Switch to Dynamic physics body for realistic knockback flight
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1.0f;
            rb.linearDamping = 0.5f;

            Vector2 hitDir = (transform.position - carPosition).normalized;
            if (hitDir.sqrMagnitude < 0.01f) hitDir = Vector2.up;

            // Upward arc knockback force
            Vector2 impulse = (hitDir + Vector2.up * 0.6f).normalized * knockbackForce;
            rb.linearVelocity = impulse;
            rb.angularVelocity = Random.Range(-knockbackTorque, knockbackTorque);
        }

        // Disable Animator on death so code purely controls the physics knockback, tumble, and fade
        if (animator != null)
        {
            animator.enabled = false;
        }

        // Spawn hit puff / death particles
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

        // Play Sound
        if (hitSound != null)
        {
            float vol = AudioManager.Instance != null ? AudioManager.Instance.GetSfxVolume() : 1.0f;
            AudioSource.PlayClipAtPoint(hitSound, effectPos, vol);
        }

        StartCoroutine(FadeAndDespawnRoutine());
    }

    private IEnumerator FadeAndDespawnRoutine()
    {
        float elapsed = 0f;
        Color initialColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

        while (elapsed < despawnDelay)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / despawnDelay;

            if (spriteRenderer != null)
            {
                Color c = initialColor;
                c.a = Mathf.Lerp(initialColor.a, 0f, progress);
                spriteRenderer.color = c;
            }

            transform.localScale = Vector3.Lerp(initialScale, initialScale * 0.2f, progress);
            yield return null;
        }

        Destroy(gameObject);
    }
}

