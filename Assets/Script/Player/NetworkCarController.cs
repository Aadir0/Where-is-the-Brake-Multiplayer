using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class NetworkCarController : NetworkBehaviour
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

    [Header("Host/Client Visual Distinctions")]
    [SerializeField] private Sprite hostSprite;
    [SerializeField] private Sprite clientSprite;
    [SerializeField] private Color hostColor = Color.white;
    [SerializeField] private Color clientColor = new Color(0.3f, 0.7f, 1f, 1f); // Distinct blue tint for Client
    [SerializeField] private bool applyColorTintIfNoSprite = true;

    // Networked Host/Client role identification
    public NetworkVariable<bool> isHostCarNet = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private CapsuleCollider2D boxCollider;
    private CarHealth healthComp;
    private InputAction fallbackMoveAction;
    private InputAction fallbackBoostAction;

    private bool isBoosted = false;
    public bool hasWonPlayer { get; private set; } = false;

    private InputAction MoveInput => moveAction != null ? moveAction.action : fallbackMoveAction;
    private InputAction BoostInput => BoostAction != null ? BoostAction.action : fallbackBoostAction;
    private InputAction JumpInput => JumpAction != null ? JumpAction.action : fallbackJumpAction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<CapsuleCollider2D>();
        healthComp = GetComponent<CarHealth>();

        originalCarScale = transform.localScale;
        currentSpeed = speed;

        CreateFallbackActions();
        InitializeTyrePositions();
    }

    public override void OnNetworkSpawn()
    {
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (healthComp != null)
        {
            healthComp.OnRespawn += ResetCarBoostState;
        }

        isHostCarNet.OnValueChanged += OnHostCarStateChanged;

        if (IsServer)
        {
            isHostCarNet.Value = (OwnerClientId == NetworkManager.ServerClientId);
        }

        ApplyCarVisuals(isHostCarNet.Value);
        ResetCarBoostState();

        if (IsOwner || IsLocalPlayer)
        {
            EnableInput(MoveInput);
            EnableInput(BoostInput);
            EnableInput(JumpInput);

            if (BoostInput != null) BoostInput.performed += OnBoostPerformed;
            if (JumpInput != null) JumpInput.performed += OnJumpPerformed;

            // Connect camera
            CameraFollow camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (camFollow != null)
            {
                camFollow.SetTarget(transform);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        isHostCarNet.OnValueChanged -= OnHostCarStateChanged;

        if (healthComp != null)
        {
            healthComp.OnRespawn -= ResetCarBoostState;
        }

        if (IsOwner || IsLocalPlayer)
        {
            if (BoostInput != null) BoostInput.performed -= OnBoostPerformed;
            if (JumpInput != null) JumpInput.performed -= OnJumpPerformed;

            DisableInput(MoveInput);
            DisableInput(BoostInput);
            DisableInput(JumpInput);
        }
    }

    private void OnHostCarStateChanged(bool previousState, bool newState)
    {
        ApplyCarVisuals(newState);
    }

    private void ApplyCarVisuals(bool isHost)
    {
        if (spriteRenderer == null) return;

        if (isHost)
        {
            if (hostSprite != null) spriteRenderer.sprite = hostSprite;
            else if (applyColorTintIfNoSprite) spriteRenderer.color = hostColor;
        }
        else
        {
            if (clientSprite != null) spriteRenderer.sprite = clientSprite;
            else if (applyColorTintIfNoSprite) spriteRenderer.color = clientColor;
        }
    }

    [Rpc(SendTo.Everyone)]
    public void SetCarWonRpc()
    {
        hasWonPlayer = true;
        isBoosted = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void ResetCarBoostState()
    {
        hasWonPlayer = false;
        isBoosted = false;
        currentSpeed = speed;
        boostCooldownTimer = 0f;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private void Update()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (hasWonPlayer) return;
        if (healthComp != null && healthComp.isDead.Value) return;

        bool boostPressed = false;

        // Pure Unity 6 Keyboard check
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.leftShiftKey.wasPressedThisFrame ||
                Keyboard.current.rightShiftKey.wasPressedThisFrame ||
                Keyboard.current.wKey.wasPressedThisFrame ||
                Keyboard.current.upArrowKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame)
            {
                boostPressed = true;
            }
        }

        // Pure Unity 6 Gamepad check
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                Gamepad.current.rightTrigger.wasPressedThisFrame)
            {
                boostPressed = true;
            }
        }

        // New Input System Action check
        if (BoostInput != null)
        {
            try
            {
                if (!BoostInput.enabled) BoostInput.Enable();
                if (BoostInput.WasPressedThisFrame()) boostPressed = true;
            }
            catch { }
        }

        if (boostPressed)
        {
            Debug.Log($"[NetworkCarController SUCCESS] Boost Activated for Local Car (IsOwner: {IsOwner}, IsHost: {isHostCarNet.Value})");
            isBoosted = true;
            RequestBoostServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (healthComp != null && healthComp.isDead.Value) return;
        if (hasWonPlayer) return;

        if (boostCooldownTimer > 0f)
        {
            boostCooldownTimer -= Time.fixedDeltaTime;
        }

        if (boundaryDriftTimer > 0f)
        {
            boundaryDriftTimer -= Time.fixedDeltaTime;
        }

        // Stationary until Boost is activated
        if (!isBoosted)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        float turn = ReadTurnInput();
        float absTurn = Mathf.Abs(turn);

        float turnBoost = Mathf.Lerp(1f, driftTurnMultiplier, absTurn);
        if (boundaryDriftTimer > 0f)
        {
            turnBoost *= boundaryTurnMultiplier;
        }

        float currentTurnSpeed = turnSpeed * turnBoost;
        float targetRotation = rb.rotation - turn * currentTurnSpeed * Time.fixedDeltaTime;

        rb.MoveRotation(targetRotation);

        float rotationRadians = targetRotation * Mathf.Deg2Rad;
        Vector2 forwardDirection = new Vector2(Mathf.Cos(rotationRadians), Mathf.Sin(rotationRadians));
        Vector2 desiredVelocity = forwardDirection * currentSpeed;

        if (absTurn > 0.5f)
        {
            float currentDrift = Mathf.Lerp(driftFactor, driftIntensity, (absTurn - 0.5f) * 2f);
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, desiredVelocity, currentDrift);
        }
        else
        {
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, desiredVelocity, driftFactor);
        }

        UpdateTyreMarks(turn);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBoostServerRpc()
    {
        if (!isBoosted)
        {
            isBoosted = true;
            SyncBoostClientRpc();
        }
        else if (boostCooldownTimer <= 0f)
        {
            if (speedBoostCoroutine != null) StopCoroutine(speedBoostCoroutine);
            speedBoostCoroutine = StartCoroutine(SpeedBoostEffect());
        }
    }

    [ClientRpc]
    private void SyncBoostClientRpc()
    {
        isBoosted = true;
    }

    private void OnBoostPerformed(InputAction.CallbackContext context)
    {
        if (!IsOwner && !IsLocalPlayer) return;
        isBoosted = true;
        RequestBoostServerRpc();
    }

    private IEnumerator SpeedBoostEffect()
    {
        boostCooldownTimer = speedBoostInterval;
        currentSpeed = speed + speedBoostAmount;

        yield return new WaitForSeconds(speedBoostDuration);

        currentSpeed = speed;
        speedBoostCoroutine = null;
    }

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if ((!IsOwner && !IsLocalPlayer) || !isBoosted || (healthComp != null && healthComp.isDead.Value) || hasWonPlayer) return;
        if (!canJump || Time.time < nextJumpTime) return;

        canJump = false;
        nextJumpTime = Time.time + jumpCooldown;

        TriggerJumpRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void TriggerJumpRpc()
    {
        if (jumpCoroutine != null)
        {
            StopCoroutine(jumpCoroutine);
        }
        jumpCoroutine = StartCoroutine(JumpEffect());
    }

    public void EnableJump()
    {
        canJump = true;
    }

    private IEnumerator JumpEffect()
    {
        if (jumpEffectPrefab != null)
        {
            spawnedJumpEffect = Instantiate(
                jumpEffectPrefab,
                transform.position,
                Quaternion.identity
            );
        }

        if (shadowPrefab != null)
        {
            spawnedShadow = Instantiate(
                shadowPrefab,
                transform.position,
                Quaternion.identity
            );

            if (spawnedShadow != null)
            {
                spawnedShadow.Initialize(transform, jumpDuration);
            }
        }

        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = jumpCollisionLayer;
        }

        float elapsedTime = 0f;
        Vector3 targetScale = originalCarScale * carJumpScale;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;
            transform.localScale = Vector3.Lerp(
                originalCarScale,
                targetScale,
                t
            );

            yield return null;
        }

        transform.localScale = targetScale;
        elapsedTime = 0f;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;
            transform.localScale = Vector3.Lerp(
                targetScale,
                originalCarScale,
                t
            );

            yield return null;
        }

        transform.localScale = originalCarScale;

        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = playerLayer;
        }

        if (spawnedShadow != null)
        {
            Destroy(spawnedShadow.gameObject);
        }

        if (spawnedJumpEffect != null)
        {
            Destroy(spawnedJumpEffect);
        }
    }

    public void ApplyBoundaryDrift()
    {
        boundaryDriftTimer = boundaryDriftCooldown;
    }

    private float ReadTurnInput()
    {
        float input = 0f;

        // 1. New Input System Action
        if (MoveInput != null)
        {
            try
            {
                if (!MoveInput.enabled) MoveInput.Enable();
                input = MoveInput.ReadValue<Vector2>().x;
            }
            catch { }
        }

        // 2. Pure Unity 6 Keyboard check (No legacy Input.GetKey crash!)
        if (Mathf.Abs(input) < 0.01f && Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input = -1f;
            else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input = 1f;
        }

        // 3. Pure Unity 6 Gamepad check
        if (Mathf.Abs(input) < 0.01f && Gamepad.current != null)
        {
            float stickX = Gamepad.current.leftStick.x.ReadValue();
            float dpadX = Gamepad.current.dpad.x.ReadValue();
            if (Mathf.Abs(stickX) > 0.1f) input = stickX;
            else if (Mathf.Abs(dpadX) > 0.1f) input = dpadX;
        }

        return input;
    }

    private void CreateFallbackActions()
    {
        if (moveAction == null && fallbackMoveAction == null)
        {
            fallbackMoveAction = new InputAction("FallbackMove", InputActionType.Value);
            fallbackMoveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow")
                .With("Left", "<Gamepad>/dpad/left")
                .With("Right", "<Gamepad>/dpad/right")
                .With("Left", "<Gamepad>/leftStick/left")
                .With("Right", "<Gamepad>/leftStick/right");

            fallbackMoveAction.Enable();
        }

        if (BoostAction == null && fallbackBoostAction == null)
        {
            fallbackBoostAction = new InputAction("FallbackBoost", InputActionType.Button);
            fallbackBoostAction.AddBinding("<Keyboard>/space");
            fallbackBoostAction.AddBinding("<Keyboard>/shift");
            fallbackBoostAction.AddBinding("<Gamepad>/buttonSouth");
            fallbackBoostAction.AddBinding("<Gamepad>/rightTrigger");

            fallbackBoostAction.Enable();
        }

        if (JumpAction == null && fallbackJumpAction == null)
        {
            fallbackJumpAction = new InputAction("FallbackJump", InputActionType.Button);
            fallbackJumpAction.AddBinding("<Keyboard>/j");
            fallbackJumpAction.AddBinding("<Gamepad>/buttonEast");

            fallbackJumpAction.Enable();
        }
    }

    private void EnableInput(InputAction action)
    {
        if (action != null && !action.enabled)
        {
            action.Enable();
        }
    }

    private void DisableInput(InputAction action)
    {
        if (action != null && action.enabled)
        {
            action.Disable();
        }
    }

    private void InitializeTyrePositions()
    {
        if (frontLeftTyre != null) lastFrontLeftPosition = frontLeftTyre.position;
        if (frontRightTyre != null) lastFrontRightPosition = frontRightTyre.position;
        if (rearLeftTyre != null) lastRearLeftPosition = rearLeftTyre.position;
        if (rearRightTyre != null) lastRearRightPosition = rearRightTyre.position;
    }

    private float GetSidewaysVelocity()
    {
        float rotationRadians = rb.rotation * Mathf.Deg2Rad;
        Vector2 rightDirection = new Vector2(-Mathf.Sin(rotationRadians), Mathf.Cos(rotationRadians));
        return Vector2.Dot(rb.linearVelocity, rightDirection);
    }

    private void UpdateTyreMarks(float turn)
    {
        if (tyreMarkPrefab == null || Mathf.Abs(turn) < 0.5f) return;

        float sidewaysVelocity = GetSidewaysVelocity();
        float currentDriftThreshold = driftThreshold;

        if (boundaryDriftTimer > 0f)
        {
            currentDriftThreshold *= boundaryDriftThresholdMultiplier;
        }

        if (Mathf.Abs(sidewaysVelocity) < currentDriftThreshold) return;

        UpdateTyreMark(frontLeftTyre, ref lastFrontLeftPosition, ref frontLeftDistance);
        UpdateTyreMark(frontRightTyre, ref lastFrontRightPosition, ref frontRightDistance);
        UpdateTyreMark(rearLeftTyre, ref lastRearLeftPosition, ref rearLeftDistance);
        UpdateTyreMark(rearRightTyre, ref lastRearRightPosition, ref rearRightDistance);
    }

    private void UpdateTyreMark(Transform tyre, ref Vector2 lastPosition, ref float distance)
    {
        if (tyre == null) return;

        Vector2 currentPosition = tyre.position;
        distance += Vector2.Distance(currentPosition, lastPosition);

        if (distance >= tyreMarkSpacing)
        {
            float rotationDegrees = transform.eulerAngles.z + tyreMarkRotationOffset;
            GameObject tyreMark = Instantiate(
                tyreMarkPrefab,
                currentPosition,
                Quaternion.Euler(0f, 0f, rotationDegrees)
            );

            Destroy(tyreMark, tyreMarkLifetime);
            lastPosition = currentPosition;
            distance = 0f;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        fallbackMoveAction?.Dispose();
        fallbackBoostAction?.Dispose();
        fallbackJumpAction?.Dispose();
    }
}
