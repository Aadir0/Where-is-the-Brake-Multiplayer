using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
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
    [SerializeField] private float jumpCooldown = 1.5f;
    [SerializeField] private float carJumpScale = 0.8f;
    [SerializeField] private ShadowJump shadowPrefab;
    [SerializeField] private GameObject jumpEffectPrefab;
    private GameObject spawnedJumpEffect;
    private InputAction fallbackJumpAction;
    private Vector3 originalCarScale;
    private Coroutine jumpCoroutine;
    private ShadowJump spawnedShadow;
    private bool canJump = false; // Jump unlocked strictly upon contact with JumpTrigger
    private bool isJumping = false; // Prevents tyre mark spawning while airborne
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

    // Owner-authoritative physics state
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
        canJump = false;
        isJumping = false;

        CreateFallbackActions();
        InitializeTyrePositions();
    }

    public override void OnNetworkSpawn()
    {
        canJump = false;
        isJumping = false;

        if (rb != null)
        {
            rb.simulated = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Owner-authoritative physics: Owner simulates Dynamic Rigidbody2D dynamics, remote clients stay Kinematic
            rb.bodyType = (IsOwner || IsLocalPlayer) ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        }

        if (healthComp != null)
        {
            healthComp.OnRespawn += ResetCarBoostStateLocal;
        }

        isHostCarNet.OnValueChanged += OnHostCarStateChanged;

        if (IsServer)
        {
            isHostCarNet.Value = (OwnerClientId == NetworkManager.ServerClientId);
        }

        ApplyCarVisuals(isHostCarNet.Value);
        ResetCarBoostStateRpc();

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
            healthComp.OnRespawn -= ResetCarBoostStateLocal;
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

    [Rpc(SendTo.Everyone)]
    public void TeleportCarRpc(Vector3 position, Quaternion rotation)
    {
        NetworkTransform netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null)
        {
            netTransform.Teleport(position, rotation, transform.localScale);
        }
        else
        {
            transform.position = position;
            transform.rotation = rotation;
        }

        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation.eulerAngles.z;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    [Rpc(SendTo.Everyone)]
    public void ResetCarBoostStateRpc()
    {
        ResetCarBoostStateLocal();
    }

    public void ResetCarBoostStateLocal()
    {
        hasWonPlayer = false;
        isBoosted = false;
        canJump = false;
        isJumping = false;
        currentSpeed = speed;
        boostCooldownTimer = 0f;
        nextJumpTime = 0f;

        if (rb != null)
        {
            rb.bodyType = (IsOwner || IsLocalPlayer) ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void EnableJump()
    {
        canJump = true;
        Debug.Log($"[NetworkCarController SUCCESS] Jump Unlocked by JumpTrigger contact for Player Car (IsOwner: {IsOwner})!");
    }

    private void Update()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (hasWonPlayer) return;
        if (healthComp != null && healthComp.isDead.Value) return;

        // Stop all controls when TimeOver UI is displayed
        if (LevelTimer.Instance != null && LevelTimer.Instance.isTimeOver.Value)
        {
            isBoosted = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            return;
        }

        // Boost Input Check: ONLY X Key (Keyboard) or Gamepad buttonSouth (A/Cross)
        bool boostPressed = false;
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            boostPressed = true;
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            boostPressed = true;
        }

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
            TriggerBoostLocal();
        }

        // Jump Input Check: ONLY Spacebar (Keyboard) or Gamepad buttonNorth (Y/Triangle)
        bool jumpPressed = false;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            jumpPressed = true;
        }

        if (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame)
        {
            jumpPressed = true;
        }

        if (JumpInput != null)
        {
            try
            {
                if (!JumpInput.enabled) JumpInput.Enable();
                if (JumpInput.WasPressedThisFrame()) jumpPressed = true;
            }
            catch { }
        }

        if (jumpPressed)
        {
            TryExecuteJump();
        }
    }

    private void TriggerBoostLocal()
    {
        if (!IsOwner && !IsLocalPlayer) return;

        if (!isBoosted)
        {
            // First press: Starts car movement at normal base speed without applying speed boost amount
            Debug.Log($"[NetworkCarController SUCCESS] Car Movement Started by Local Player (IsOwner: {IsOwner})");
            isBoosted = true;
            currentSpeed = speed;
            boostCooldownTimer = speedBoostInterval;
            NotifyBoostStartedRpc();
        }
        else if (boostCooldownTimer <= 0f)
        {
            // Subsequent presses: Triggers speed boost burst
            Debug.Log($"[NetworkCarController SUCCESS] Speed Boost Burst Triggered by Local Player (IsOwner: {IsOwner})");
            if (speedBoostCoroutine != null) StopCoroutine(speedBoostCoroutine);
            speedBoostCoroutine = StartCoroutine(SpeedBoostEffect());
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void NotifyBoostStartedRpc()
    {
        // Shared state notification to server
    }

    private void TryExecuteJump()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (!isBoosted || (healthComp != null && healthComp.isDead.Value) || hasWonPlayer) return;
        if (LevelTimer.Instance != null && LevelTimer.Instance.isTimeOver.Value) return;

        if (!canJump)
        {
            Debug.Log("[NetworkCarController] Cannot Jump — JumpTrigger contact required to unlock jump!");
            return;
        }

        if (Time.time < nextJumpTime)
        {
            Debug.Log($"[NetworkCarController] Jump on Cooldown! Remaining: {nextJumpTime - Time.time:F1}s");
            return;
        }

        nextJumpTime = Time.time + jumpCooldown;
        canJump = false; // Consume jump pickup

        Debug.Log($"[NetworkCarController SUCCESS] Executing Jump with {jumpCooldown}s Cooldown (IsOwner: {IsOwner})!");
        TriggerJumpRpc();
    }

    private void FixedUpdate()
    {
        // Owner drives physics simulation locally with zero input latency
        if (!IsOwner && !IsLocalPlayer) return;
        if (hasWonPlayer) return;
        if (healthComp != null && healthComp.isDead.Value) return;

        if (LevelTimer.Instance != null && LevelTimer.Instance.isTimeOver.Value)
        {
            isBoosted = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
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

    private void OnBoostPerformed(InputAction.CallbackContext context)
    {
        TriggerBoostLocal();
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
        TryExecuteJump();
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

    private IEnumerator JumpEffect()
    {
        isJumping = true;

        if (jumpEffectPrefab != null)
        {
            GameObject jumpFX = Instantiate(
                jumpEffectPrefab,
                transform.position,
                Quaternion.identity
            );
            Destroy(jumpFX, jumpDuration + 0.5f);
        }

        if (shadowPrefab != null)
        {
            ShadowJump shadowObj = Instantiate(
                shadowPrefab,
                transform.position,
                Quaternion.identity
            );

            if (shadowObj != null)
            {
                shadowObj.Initialize(transform, jumpDuration);
                Destroy(shadowObj.gameObject, jumpDuration + 0.2f);
            }
        }

        // Change layer to jumpCollisionLayer for exactly jumpDuration seconds
        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = jumpCollisionLayer;
        }

        float halfDuration = jumpDuration * 0.5f;
        float elapsedTime = 0f;
        Vector3 targetScale = originalCarScale * carJumpScale;

        // Scale up phase (first 50% of jumpDuration)
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / halfDuration;
            transform.localScale = Vector3.Lerp(
                originalCarScale,
                targetScale,
                t
            );

            yield return null;
        }

        transform.localScale = targetScale;
        elapsedTime = 0f;

        // Scale down phase (second 50% of jumpDuration)
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / halfDuration;
            transform.localScale = Vector3.Lerp(
                targetScale,
                originalCarScale,
                t
            );

            yield return null;
        }

        transform.localScale = originalCarScale;

        // Revert layer back to playerLayer immediately when jumpDuration completes
        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = playerLayer;
        }

        isJumping = false;
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
            fallbackBoostAction.AddBinding("<Keyboard>/x");
            fallbackBoostAction.AddBinding("<Gamepad>/buttonSouth");

            fallbackBoostAction.Enable();
        }

        if (JumpAction == null && fallbackJumpAction == null)
        {
            fallbackJumpAction = new InputAction("FallbackJump", InputActionType.Button);
            fallbackJumpAction.AddBinding("<Keyboard>/space");
            fallbackJumpAction.AddBinding("<Gamepad>/buttonNorth");

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
        // Disable tyre marks while airborne / jumping
        if (isJumping || (boxCollider != null && boxCollider.gameObject.layer == jumpCollisionLayer)) return;

        if (Mathf.Abs(turn) < 0.35f) return;

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
        if (tyre == null || (!IsOwner && !IsLocalPlayer)) return;

        Vector2 currentPosition = tyre.position;
        distance += Vector2.Distance(currentPosition, lastPosition);

        if (distance >= tyreMarkSpacing)
        {
            float rotationDegrees = transform.eulerAngles.z + tyreMarkRotationOffset;
            SpawnTyreMarkRpc(currentPosition, rotationDegrees);
            lastPosition = currentPosition;
            distance = 0f;
        }
    }

    [Rpc(SendTo.Everyone)]
    private void SpawnTyreMarkRpc(Vector2 position, float rotationDegrees)
    {
        if (tyreMarkPrefab != null)
        {
            GameObject tyreMark = Instantiate(
                tyreMarkPrefab,
                position,
                Quaternion.Euler(0f, 0f, rotationDegrees)
            );

            Destroy(tyreMark, tyreMarkLifetime);
        }
        else
        {
            // Fallback visual tyre mark creation if prefab reference is missing
            GameObject fallbackMark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fallbackMark.transform.position = new Vector3(position.x, position.y, 0.1f);
            fallbackMark.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            fallbackMark.transform.localScale = new Vector3(0.2f, 0.4f, 1f);

            Collider col = fallbackMark.GetComponent<Collider>();
            if (col != null) Destroy(col);

            SpriteRenderer sr = fallbackMark.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(0.1f, 0.1f, 0.1f, 0.6f);

            Destroy(fallbackMark, tyreMarkLifetime);
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
