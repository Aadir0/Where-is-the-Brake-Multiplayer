using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class CarControllerSingle : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 8f;
    public float turnSpeed = 140f;
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference BoostAction;

    [Header("Speed Boost")]
    [SerializeField] private float speedBoostAmount = 5f;
    [SerializeField] private float speedBoostDuration = 0.3f;
    [SerializeField] private float speedBoostInterval = 1f;

    public float currentSpeed;
    private float boostCooldownTimer;
    private Coroutine speedBoostCoroutine;

    [Header("Drift")]
    [SerializeField] private float driftTurnMultiplier = 1.7f;
    [SerializeField] private float driftFactor = 0.8f;
    [SerializeField] private float driftIntensity = 0.5f;

    [Header("Boundary Drift")]
    [SerializeField] private float boundaryTurnMultiplier = 1.8f;
    [SerializeField] private float boundaryDriftCooldown = 0.4f;
    [SerializeField] private float boundaryDriftThresholdMultiplier = 2f;
    private float boundaryDriftTimer;

    [Header("Jump Effect")]
    [SerializeField] private InputActionReference JumpAction;
    [SerializeField] private float jumpDuration = 0.3f;
    [SerializeField] private float jumpCooldown = 1f;
    [SerializeField] private float carJumpScale = 0.8f;
    [SerializeField] private ShadowJump shadowPrefab;
    [SerializeField] private GameObject jumpEffectPrefab;
    private GameObject spawnedJumpEffect;
    private InputAction fallbackJumpAction;
    private Vector3 originalCarScale;
    private Coroutine jumpCoroutine;
    private ShadowJump spawnedShadow;
    private bool canJump = false;
    private bool isJumping = false;
    private float nextJumpTime = 0f;

    [Header("Jump Collision")]
    [SerializeField] private int playerLayer = 6;
    [SerializeField] private int jumpCollisionLayer = 3;

    [Header("Tyre Marks")]
    [SerializeField] private GameObject tyreMarkPrefab;
    [SerializeField] private Transform frontLeftTyre;
    [SerializeField] private Transform frontRightTyre;
    [SerializeField] private Transform rearLeftTyre;
    [SerializeField] private Transform rearRightTyre;
    [SerializeField] private float tyreMarkSpacing = 0.15f;
    [SerializeField] private float driftThreshold = 0.35f;
    [SerializeField] private float tyreMarkLifetime = 3f;
    [SerializeField] private float tyreMarkRotationOffset = 0f;
    private float frontLeftDistance;
    private float frontRightDistance;
    private float rearLeftDistance;
    private float rearRightDistance;
    private Vector2 lastFrontLeftPosition;
    private Vector2 lastFrontRightPosition;
    private Vector2 lastRearLeftPosition;
    private Vector2 lastRearRightPosition;

    [Header("Death")]
    [SerializeField] private float deathSequenceDelay = 0.6f;
    [SerializeField] private GameObject smokeEffect;
    [SerializeField] private GameObject GameoverMenu;
    [SerializeField] private CameraZoom2D cameraZoom;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private CapsuleCollider2D boxCollider;
    private InputAction fallbackMoveAction;
    private InputAction fallbackBoostAction;
    private bool isBoosted = false;
    private bool isDead = false;
    private InputAction MoveInput =>
        moveAction != null ? moveAction.action : fallbackMoveAction;

    private InputAction BoostInput =>
        BoostAction != null ? BoostAction.action : fallbackBoostAction;

    private InputAction JumpInput =>
        JumpAction != null ? JumpAction.action : fallbackJumpAction;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<CapsuleCollider2D>();

        originalCarScale = transform.localScale;

        currentSpeed = speed;

        CreateFallbackActions();

        InitializeTyrePositions();
    }

    void OnEnable()
    {
        EnableInput(MoveInput);
        EnableInput(BoostInput);
        EnableInput(JumpInput);

        BoostInput.performed += OnBoostPerformed;
        JumpInput.performed += OnJumpPerformed;
    }

    void OnDisable()
    {
        BoostInput.performed -= OnBoostPerformed;
        JumpInput.performed -= OnJumpPerformed;
        DisableInput(MoveInput);
        DisableInput(BoostInput);
        DisableInput(JumpInput);
    }

    void FixedUpdate()
    {
        if (!isBoosted || isDead)
        {
            return;
        }

        if (boostCooldownTimer > 0f)
        {
            boostCooldownTimer -= Time.fixedDeltaTime;
        }

        if (boundaryDriftTimer > 0f)
        {
            boundaryDriftTimer -= Time.fixedDeltaTime;
        }

        float turn = Mathf.Clamp(ReadTurnInput(), -1f, 1f);
        float absTurn = Mathf.Abs(turn);

        float turnBoost = Mathf.Lerp(
            1f,
            driftTurnMultiplier,
            absTurn
        );

        if (boundaryDriftTimer > 0f)
        {
            turnBoost *= boundaryTurnMultiplier;
        }

        float currentTurnSpeed =
            turnSpeed * turnBoost;

        float targetRotation =
            rb.rotation -
            turn *
            currentTurnSpeed *
            Time.fixedDeltaTime;

        rb.MoveRotation(targetRotation);

        float rotationRadians =
            targetRotation * Mathf.Deg2Rad;

        Vector2 forwardDirection =
            new Vector2(
                Mathf.Cos(rotationRadians),
                Mathf.Sin(rotationRadians)
            );

        Vector2 desiredVelocity =
            forwardDirection * currentSpeed;

        if (absTurn > 0.5f)
        {
            float currentDrift =
                Mathf.Lerp(
                    driftFactor,
                    driftIntensity,
                    (absTurn - 0.5f) * 2f
                );

            rb.linearVelocity =
                Vector2.Lerp(
                    rb.linearVelocity,
                    desiredVelocity,
                    currentDrift
                );
        }
        else
        {
            rb.linearVelocity =
                Vector2.Lerp(
                    rb.linearVelocity,
                    desiredVelocity,
                    driftFactor
                );
        }

        UpdateTyreMarks(turn);
    }

    private void InitializeTyrePositions()
    {
        if (frontLeftTyre != null)
        {
            lastFrontLeftPosition =
                frontLeftTyre.position;
        }

        if (frontRightTyre != null)
        {
            lastFrontRightPosition =
                frontRightTyre.position;
        }

        if (rearLeftTyre != null)
        {
            lastRearLeftPosition =
                rearLeftTyre.position;
        }

        if (rearRightTyre != null)
        {
            lastRearRightPosition =
                rearRightTyre.position;
        }
    }

    private float GetSidewaysVelocity()
    {
        float rotationRadians =
            rb.rotation * Mathf.Deg2Rad;

        Vector2 rightDirection =
            new Vector2(
                -Mathf.Sin(rotationRadians),
                Mathf.Cos(rotationRadians)
            );

        return Vector2.Dot(
            rb.linearVelocity,
            rightDirection
        );
    }

    private void UpdateTyreMarks(float turn)
    {
        if (tyreMarkPrefab == null)
        {
            return;
        }

        if (Mathf.Abs(turn) < 0.5f)
        {
            return;
        }

        float sidewaysVelocity = GetSidewaysVelocity();

        float currentDriftThreshold = driftThreshold;

        if (boundaryDriftTimer > 0f)
        {
            currentDriftThreshold *=
                boundaryDriftThresholdMultiplier;
        }

        if (Mathf.Abs(sidewaysVelocity) <
            currentDriftThreshold)
        {
            return;
        }

        UpdateTyreMark(
            frontLeftTyre,
            ref lastFrontLeftPosition,
            ref frontLeftDistance
        );

        UpdateTyreMark(
            frontRightTyre,
            ref lastFrontRightPosition,
            ref frontRightDistance
        );

        UpdateTyreMark(
            rearLeftTyre,
            ref lastRearLeftPosition,
            ref rearLeftDistance
        );

        UpdateTyreMark(
            rearRightTyre,
            ref lastRearRightPosition,
            ref rearRightDistance
        );
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Boundary"))
        {
            boundaryDriftTimer = boundaryDriftCooldown;
        }
    }

    private void UpdateTyreMark(Transform tyre, ref Vector2 lastPosition, ref float distance)
    {
        if (tyre == null)
        {
            return;
        }

        Vector2 currentPosition =
            tyre.position;

        float movementDistance =
            Vector2.Distance(
                currentPosition,
                lastPosition
            );

        distance += movementDistance;

        if (distance >= tyreMarkSpacing)
        {
            SpawnTyreMark(tyre);

            distance = 0f;
        }

        lastPosition = currentPosition;
    }

    private void SpawnTyreMark(Transform tyre)
    {
        if (tyreMarkPrefab == null || tyre == null)
        {
            return;
        }

        if (!isJumping)
        {
            Quaternion rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    transform.eulerAngles.z +
                    tyreMarkRotationOffset
                );

            GameObject tyreMark = Instantiate(
                tyreMarkPrefab,
                tyre.position,
                rotation
            );
            Destroy(
                tyreMark,
                tyreMarkLifetime
            );
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if ((other.CompareTag("Trap") || other.CompareTag("Hole")) && !isDead)
        {
            StartCoroutine(Die());
        }
    }

    void OnDestroy()
    {
        fallbackMoveAction?.Dispose();
        fallbackBoostAction?.Dispose();
        fallbackJumpAction?.Dispose();
    }

    private void OnBoostPerformed(InputAction.CallbackContext context)
    {
        if (!isBoosted)
        {
            isBoosted = true;
            return;
        }

        if (boostCooldownTimer > 0f)
        {
            return;
        }

        if (speedBoostCoroutine != null)
        {
            StopCoroutine(speedBoostCoroutine);
        }

        speedBoostCoroutine =
            StartCoroutine(SpeedBoostEffect());
    }

    private IEnumerator SpeedBoostEffect()
    {
        boostCooldownTimer =
            speedBoostInterval;

        currentSpeed =
            speed + speedBoostAmount;

        yield return new WaitForSeconds(
            speedBoostDuration
        );

        currentSpeed =
            speed;

        speedBoostCoroutine = null;
    }

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (!isBoosted || isDead)
        {
            return;
        }

        if (!canJump)
        {
            return;
        }

        if (Time.time < nextJumpTime)
        {
            return;
        }

        canJump = false;

        nextJumpTime = Time.time + jumpCooldown;

        if (jumpCoroutine != null)
        {
            StopCoroutine(jumpCoroutine);
        }

        jumpCoroutine =
            StartCoroutine(JumpEffect());
    }

    public void EnableJump()
    {
        canJump = true;
    }

    private IEnumerator JumpEffect()
    {
        isJumping = true;

        if (jumpEffectPrefab != null)
        {
            spawnedJumpEffect = Instantiate(
                jumpEffectPrefab,
                transform.position,
                Quaternion.identity
            );

            // spawnedJumpEffect.transform.SetParent(transform);
        }

        transform.localScale =
            originalCarScale * carJumpScale;

        Physics2D.IgnoreLayerCollision(
            playerLayer,
            jumpCollisionLayer,
            true
        );


        if (spawnedShadow == null)
        {
            if (shadowPrefab != null)
            {
                spawnedShadow =
                    Instantiate(
                        shadowPrefab,
                        transform.position,
                        transform.rotation
                    );
            }
        }

        if (spawnedShadow != null)
        {
            spawnedShadow.Initialize(
                transform,
                jumpDuration
            );
        }
        yield return new WaitForSeconds(
            jumpDuration
        );

        transform.localScale =
            originalCarScale;

        Physics2D.IgnoreLayerCollision(
            playerLayer,
            jumpCollisionLayer,
            false
        );

        if (spawnedJumpEffect != null)
        {
            Destroy(spawnedJumpEffect);
            spawnedJumpEffect = null;
        }

        jumpCoroutine = null;
        isJumping = false;
    }

    private float ReadTurnInput()
    {
        InputAction action = MoveInput;

        if (action == null)
        {
            return 0f;
        }

        return action.expectedControlType == "Vector2"
            ? action.ReadValue<Vector2>().x
            : action.ReadValue<float>();
    }

    private void CreateFallbackActions()
    {
        fallbackMoveAction =
            new InputAction(
                "Move",
                InputActionType.Value,
                expectedControlType: "Vector2"
            );

        fallbackMoveAction
            .AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        fallbackMoveAction.AddBinding(
            "<Gamepad>/leftStick"
        );

        fallbackBoostAction =
            new InputAction(
                "Boost",
                InputActionType.Button
            );

        fallbackBoostAction.AddBinding(
            "<mouse>/leftButton"
        );

        fallbackBoostAction.AddBinding(
            "<Gamepad>/buttonSouth"
        );

        fallbackJumpAction =
            new InputAction(
                "Jump",
                InputActionType.Button
            );

        fallbackJumpAction.AddBinding(
            "<Keyboard>/space"
        );

        fallbackJumpAction.AddBinding(
            "<Gamepad>/buttonNorth"
        );
    }

    private static void EnableInput(
        InputAction action)
    {
        if (action != null &&
            !action.enabled)
        {
            action.Enable();
        }
    }

    private static void DisableInput(
        InputAction action)
    {
        if (action != null &&
            action.enabled)
        {
            action.Disable();
        }
    }

    private IEnumerator Die()
    {
        isDead = true;

        boxCollider.enabled = false;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        spriteRenderer.enabled = false;

        if (cameraZoom != null)
        {
            cameraZoom.StartZoom(transform);
        }

        if (smokeEffect != null)
        {
            Instantiate(
                smokeEffect,
                transform.position,
                Quaternion.identity
            );
        }

        if (DeathMarkerManager.Instance != null)
        {
            DeathMarkerManager.Instance.SpawnDeathMarker(
                transform.position
            );
        }

        yield return new WaitForSeconds(
            deathSequenceDelay
        );

        if (GameoverMenu != null)
        {
            GameoverMenu.SetActive(true);
        }

        Destroy(gameObject);
    }
}