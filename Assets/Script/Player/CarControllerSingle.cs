using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class CarControllerSingle : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 8.5f;
    public float turnSpeed = 220f; // Snappy 360-degree rotational responsiveness
    [SerializeField] private InputActionReference moveAction;

    public float currentSpeed;
    private float boostCooldownTimer;

    [Header("Drift & Smooth Handling")]
    [SerializeField] private float driftTurnMultiplier = 1.45f;
    [SerializeField] private float driftFactor = 0.94f;
    [SerializeField] private float driftIntensity = 0.82f;

    [Header("Pre-Start Wobble Indicator Settings")]
    [SerializeField] private GameObject startPromptVisual;
    [SerializeField] private Vector3 promptScale = Vector3.one; // Inspector field to easily resize visual prompt
    [SerializeField] private Vector3 promptOffset = new Vector3(1.2f, 0f, -0.5f); // Positioned just ahead of the player car!
    [SerializeField] private float wobbleSpeed = 2.5f;
    [SerializeField] private float wobbleHeight = 0.04f; // Very subtle vertical wobble
    [SerializeField] private float promptPulseSpeed = 2.5f;
    [SerializeField] private float promptPulseAmount = 0.06f; // Gentle scale up/down (±6%)
    private Vector3 initialPromptLocalPos;

    [Header("Car Start Effect")]
    [SerializeField] private GameObject carStartEffectPrefab;
    [SerializeField] private float carStartEffectLifetime = 1.0f;

    [Header("Boundary Drift")]
    [SerializeField] private float boundaryTurnMultiplier = 1.8f;
    [SerializeField] private float boundaryDriftCooldown = 0.4f;
    private float boundaryDriftTimer;
    private bool isTouchingBoundary = false;

    [Header("Sheep Collision Debuff")]
    private float sheepDebuffMultiplier = 1.0f;
    private Coroutine sheepDebuffCoroutine;

    public void ApplySheepSlowDebuff(float duration = 1.0f, float speedMultiplier = 0.45f)
    {
        if (sheepDebuffCoroutine != null)
        {
            StopCoroutine(sheepDebuffCoroutine);
        }
        sheepDebuffCoroutine = StartCoroutine(SheepSlowDebuffRoutine(duration, speedMultiplier));
    }

    private IEnumerator SheepSlowDebuffRoutine(float duration, float speedMultiplier)
    {
        sheepDebuffMultiplier = speedMultiplier;
        yield return new WaitForSeconds(duration);
        sheepDebuffMultiplier = 1.0f;
        sheepDebuffCoroutine = null;
    }

    [Header("Jump Effect Settings")]
    [SerializeField] private InputActionReference JumpAction;
    [SerializeField] private float jumpDuration = 0.32f;
    [SerializeField] private float jumpCooldown = 1f;
    [SerializeField] private float carJumpScale = 1.32f; // Scales UP cleanly for arcade elevation
    [SerializeField] private ShadowJump shadowPrefab;
    [SerializeField] private GameObject jumpEffectPrefab;

    [Header("Landing Camera Shake")]
    [SerializeField] private float landingShakeDuration = 0.12f;
    [SerializeField] private float landingShakeIntensity = 0.18f;

    [Header("Per-Scene Overrides")]
    [SerializeField] private List<SceneJumpOverride> sceneJumpOverrides = new List<SceneJumpOverride>();

    private GameObject spawnedJumpEffect;
    private InputAction fallbackJumpAction;
    private Vector3 originalCarScale;
    private Coroutine jumpCoroutine;
    private ShadowJump spawnedShadow;
    private bool canJump = false;
    private bool continuousJump = false; // When true, auto-jumps while inside JumpTrigger
    private bool isJumping = false;
    private float nextJumpTime = 0f;
    private int lastActionFrame = -1; // Single-frame lock to prevent double execution on 1st press

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
    [SerializeField] private float deathSequenceDelay = 0.0f;
    [SerializeField] private GameObject smokeEffect;
    [SerializeField] private GameObject holeDeathEffect;
    [SerializeField] private GameObject trapDeathEffect;
    [SerializeField] private GameObject GameoverMenu;
    [SerializeField] private CameraZoom2D cameraZoom;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private CapsuleCollider2D boxCollider;
    private InputAction fallbackMoveAction;
    private bool isBoosted = false; // Represents whether car movement has started
    private bool isDead = false;
    public bool hasWonPlayer { get; private set; } = false;

    private InputAction MoveInput => moveAction != null ? moveAction.action : fallbackMoveAction;
    private InputAction JumpInput => JumpAction != null ? JumpAction.action : fallbackJumpAction;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<CapsuleCollider2D>();

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // Enable physics interpolation for silky smooth 60/144fps movement
        }

        originalCarScale = transform.localScale;
        currentSpeed = speed;
        isTouchingBoundary = false;
        lastActionFrame = -1;

        ApplyLevelJumpSettings();
        SetupStartPromptVisual();
        CreateFallbackActions();
        InitializeTyrePositions();
    }

    private Vector3 defaultBaseScale = Vector3.one;

    public void ApplyLevelJumpSettings()
    {
        if (LevelJumpSettings.Instance != null)
        {
            if (LevelJumpSettings.Instance.EnableJumpOverride)
            {
                jumpDuration = LevelJumpSettings.Instance.LevelJumpDuration;
                jumpCooldown = LevelJumpSettings.Instance.LevelJumpCooldown;
                continuousJump = LevelJumpSettings.Instance.ContinuousJump;
            }

            if (LevelJumpSettings.Instance.EnableSpeedOverride)
            {
                speed = LevelJumpSettings.Instance.LevelSpeed;
                turnSpeed = LevelJumpSettings.Instance.LevelTurnSpeed;
                if (!isBoosted) currentSpeed = speed;
            }

            if (LevelJumpSettings.Instance.EnableScaleOverride)
            {
                originalCarScale = LevelJumpSettings.Instance.LevelVehicleScale;
                transform.localScale = LevelJumpSettings.Instance.LevelVehicleScale;
            }
            else
            {
                originalCarScale = defaultBaseScale;
                transform.localScale = defaultBaseScale;
            }
            return;
        }

        originalCarScale = defaultBaseScale;
        transform.localScale = defaultBaseScale;

        string currentScene = SceneManager.GetActiveScene().name;
        if (sceneJumpOverrides != null && sceneJumpOverrides.Count > 0)
        {
            foreach (var overrideItem in sceneJumpOverrides)
            {
                if (string.Equals(overrideItem.sceneName, currentScene, StringComparison.OrdinalIgnoreCase))
                {
                    if (overrideItem.jumpDuration > 0f) jumpDuration = overrideItem.jumpDuration;
                    jumpCooldown = overrideItem.jumpCooldown; // Allow 0 cooldown
                    continuousJump = overrideItem.continuousJump;
                    if (overrideItem.speed > 0f)
                    {
                        speed = overrideItem.speed;
                        if (!isBoosted) currentSpeed = speed;
                    }
                    if (overrideItem.turnSpeed > 0f) turnSpeed = overrideItem.turnSpeed;
                    return;
                }
            }
        }
    }

    private void SetupStartPromptVisual()
    {
        if (startPromptVisual != null && !startPromptVisual.scene.isLoaded)
        {
            startPromptVisual = Instantiate(startPromptVisual, transform);
        }

        if (startPromptVisual == null)
        {
            foreach (Transform child in transform)
            {
                string cName = child.name.ToLower();
                if (cName.Contains("start") || cName.Contains("prompt") || cName.Contains("indicator") || cName.Contains("visual") || child.CompareTag("StartPrompt"))
                {
                    startPromptVisual = child.gameObject;
                    break;
                }
            }

            if (startPromptVisual == null)
            {
                GameObject generatedPrompt = new GameObject("StartPromptVisual");
                generatedPrompt.transform.SetParent(transform, false);

                TextMeshPro tmPro = generatedPrompt.AddComponent<TextMeshPro>();
                tmPro.text = "PRESS SPACE / [A] TO START";
                tmPro.fontSize = 2.5f;
                tmPro.alignment = TextAlignmentOptions.Center;
                tmPro.color = Color.yellow;
                tmPro.sortingOrder = 50;

                startPromptVisual = generatedPrompt;
            }
        }

        if (startPromptVisual != null)
        {
            if (startPromptVisual.transform.parent != transform)
            {
                startPromptVisual.transform.SetParent(transform, false);
            }

            initialPromptLocalPos = promptOffset;
            startPromptVisual.transform.localPosition = initialPromptLocalPos;
            startPromptVisual.transform.localScale = promptScale;

            SpriteRenderer promptSr = startPromptVisual.GetComponent<SpriteRenderer>();
            if (promptSr == null) promptSr = startPromptVisual.GetComponentInChildren<SpriteRenderer>(true);

            if (promptSr != null)
            {
                promptSr.enabled = true;
                if (spriteRenderer != null)
                {
                    promptSr.sortingLayerID = spriteRenderer.sortingLayerID;
                    promptSr.sortingOrder = spriteRenderer.sortingOrder + 30;
                }
                else
                {
                    promptSr.sortingOrder = 30;
                }
            }

            Renderer rend = startPromptVisual.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.enabled = true;
                rend.sortingOrder = 50;
            }

            startPromptVisual.SetActive(!isBoosted && !hasWonPlayer);
        }
    }

    public void SetCarWon()
    {
        hasWonPlayer = true;
        isBoosted = false;
        if (startPromptVisual != null)
        {
            startPromptVisual.SetActive(false);
        }
    }

    void OnEnable()
    {
        EnableInput(MoveInput);
        EnableInput(JumpInput);
        lastActionFrame = -1;
        ApplyLevelJumpSettings();
    }

    void OnDisable()
    {
        DisableInput(MoveInput);
        DisableInput(JumpInput);
    }

    private void Update()
    {
        if (!isBoosted && !hasWonPlayer && startPromptVisual != null)
        {
            if (!startPromptVisual.activeSelf) startPromptVisual.SetActive(true);

            float bob = Mathf.Sin(Time.time * wobbleSpeed) * wobbleHeight;
            float pulse = 1f + (Mathf.Sin(Time.time * promptPulseSpeed) * promptPulseAmount);

            startPromptVisual.transform.localPosition = initialPromptLocalPos + new Vector3(0f, bob, 0f);
            startPromptVisual.transform.localScale = promptScale * pulse;
        }
        else if ((isBoosted || hasWonPlayer) && startPromptVisual != null && startPromptVisual.activeSelf)
        {
            startPromptVisual.SetActive(false);
        }

        if (isDead || hasWonPlayer) return;

        bool actionPressed = false;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            actionPressed = true;
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            actionPressed = true;
        }

        if (JumpInput != null)
        {
            try
            {
                if (!JumpInput.enabled) JumpInput.Enable();
                if (JumpInput.WasPressedThisFrame()) actionPressed = true;
            }
            catch { }
        }

        if (actionPressed)
        {
            HandleActionPressed();
        }
    }

    private void HandleActionPressed()
    {
        if (isDead || hasWonPlayer) return;

        if (Time.frameCount == lastActionFrame) return;
        lastActionFrame = Time.frameCount;

        if (!isBoosted)
        {
            if (boostCooldownTimer > 0f) return;
            isBoosted = true;
            currentSpeed = speed;

            if (startPromptVisual != null)
            {
                startPromptVisual.SetActive(false);
            }

            if (carStartEffectPrefab != null)
            {
                GameObject startFX = Instantiate(carStartEffectPrefab, transform.position, Quaternion.identity);
                Destroy(startFX, carStartEffectLifetime);
            }
        }
        else
        {
            TryExecuteJump();
        }
    }

    private void TryExecuteJump()
    {
        if (!isBoosted || isDead || hasWonPlayer) return;

        if (isJumping) return;

        if (!canJump) return;

        ApplyLevelJumpSettings();

        if (Time.time < nextJumpTime) return;

        canJump = false;
        nextJumpTime = Time.time + jumpCooldown;

        if (jumpCoroutine != null) StopCoroutine(jumpCoroutine);
        jumpCoroutine = StartCoroutine(JumpEffect());
    }

    void FixedUpdate()
    {
        if (!isBoosted || isDead || hasWonPlayer)
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

        Vector2 moveInput = ReadMoveVectorInput();
        float targetRotation = rb.rotation;
        float turnMagnitude = 0f;

        if (moveInput.sqrMagnitude > 0.04f)
        {
            float targetAngle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(rb.rotation, targetAngle));
            turnMagnitude = Mathf.Clamp01(angleDiff / 90f);

            float turnBoost = Mathf.Lerp(1f, driftTurnMultiplier, turnMagnitude);
            if (boundaryDriftTimer > 0f) turnBoost *= boundaryTurnMultiplier;

            float currentTurnSpeed = turnSpeed * turnBoost;
            targetRotation = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, currentTurnSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(targetRotation);
        }

        float rotationRadians = targetRotation * Mathf.Deg2Rad;
        Vector2 forwardDirection = new Vector2(Mathf.Cos(rotationRadians), Mathf.Sin(rotationRadians));
        float effectiveSpeed = currentSpeed * sheepDebuffMultiplier;
        Vector2 desiredVelocity = forwardDirection * effectiveSpeed;

        float driftFactorBlend = Mathf.Lerp(driftFactor, driftIntensity, turnMagnitude);
        float lerpRate = Mathf.Lerp(18f, 26f, driftFactorBlend);
        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, desiredVelocity, 1f - Mathf.Exp(-lerpRate * Time.fixedDeltaTime));

        UpdateTyreMarks(turnMagnitude);
    }

    private bool IsBoundaryCollision(Collision2D collision)
    {
        if (collision == null || collision.gameObject == null) return false;
        return collision.gameObject.CompareTag("Boundary") ||
               string.Equals(LayerMask.LayerToName(collision.gameObject.layer), "Boundary", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTrapObject(GameObject obj)
    {
        if (obj == null) return false;

        Transform current = obj.transform;
        while (current != null)
        {
            GameObject candidate = current.gameObject;
            if (candidate.CompareTag("Trap")) return true;
            string layerName = LayerMask.LayerToName(candidate.layer);
            if (!string.IsNullOrEmpty(layerName) && string.Equals(layerName, "Trap", StringComparison.OrdinalIgnoreCase)) return true;
            string n = candidate.name.ToLower();
            if (n.Contains("trap") || n.Contains("saw") || n.Contains("spike") || n.Contains("blade") || n.Contains("hazard")) return true;
            current = current.parent;
        }

        for (int i = 0; i < obj.transform.childCount; i++)
        {
            Transform child = obj.transform.GetChild(i);
            if (child.CompareTag("Trap")) return true;
            string layerName = LayerMask.LayerToName(child.gameObject.layer);
            if (!string.IsNullOrEmpty(layerName) && string.Equals(layerName, "Trap", StringComparison.OrdinalIgnoreCase)) return true;
            string n = child.name.ToLower();
            if (n.Contains("trap") || n.Contains("saw") || n.Contains("spike") || n.Contains("blade") || n.Contains("hazard")) return true;
        }

        if (obj.TryGetComponent<Rigidbody2D>(out var rbComp) && rbComp.gameObject != obj)
        {
            return IsTrapObject(rbComp.gameObject);
        }

        return false;
    }

    public bool IsHoleObject(GameObject obj)
    {
        if (obj == null) return false;

        Transform current = obj.transform;
        while (current != null)
        {
            GameObject candidate = current.gameObject;
            if (candidate.CompareTag("Hole")) return true;
            string layerName = LayerMask.LayerToName(candidate.layer);
            if (!string.IsNullOrEmpty(layerName) && string.Equals(layerName, "Hole", StringComparison.OrdinalIgnoreCase)) return true;
            string n = candidate.name.ToLower();
            if (n.Contains("hole") || n.Contains("water") || n.Contains("lava") || n.Contains("pit")) return true;
            current = current.parent;
        }

        if (obj.TryGetComponent<Rigidbody2D>(out var rbComp) && rbComp.gameObject != obj)
        {
            return IsHoleObject(rbComp.gameObject);
        }

        return false;
    }

    private bool CheckAnyOverlappingTrap()
    {
        if (boxCollider != null)
        {
            List<Collider2D> colHits = new List<Collider2D>();
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true, useLayerMask = false };
            int count = boxCollider.Overlap(filter, colHits);
            for (int i = 0; i < count; i++)
            {
                if (colHits[i] != null && colHits[i].gameObject != gameObject && IsTrapObject(colHits[i].gameObject))
                {
                    return true;
                }
            }
        }

        Vector2 pos = transform.position;
        Collider2D[] pointHits = Physics2D.OverlapPointAll(pos);
        if (pointHits != null)
        {
            foreach (var hit in pointHits)
            {
                if (hit != null && hit.gameObject != gameObject && IsTrapObject(hit.gameObject)) return true;
            }
        }

        Collider2D[] circleHits = Physics2D.OverlapCircleAll(pos, 0.8f);
        if (circleHits != null)
        {
            foreach (var hit in circleHits)
            {
                if (hit != null && hit.gameObject != gameObject && IsTrapObject(hit.gameObject)) return true;
            }
        }

        if (boxCollider != null)
        {
            Collider2D[] boxHits = Physics2D.OverlapBoxAll(boxCollider.bounds.center, boxCollider.bounds.size * 1.3f, transform.eulerAngles.z);
            if (boxHits != null)
            {
                foreach (var hit in boxHits)
                {
                    if (hit != null && hit.gameObject != gameObject && IsTrapObject(hit.gameObject)) return true;
                }
            }
        }

        return false;
    }

    private void CheckTriggerOrCollisionDeath(GameObject otherObj)
    {
        if (isDead || isJumping) return;

        bool isTrap = IsTrapObject(otherObj) || CheckAnyOverlappingTrap();
        bool isHole = IsHoleObject(otherObj);

        if (isTrap || isHole)
        {
            Debug.Log($"[CarControllerSingle] DEATH CONTACT: obj='{(otherObj != null ? otherObj.name : "null")}', " +
                      $"tag='{(otherObj != null ? otherObj.tag : "null")}', " +
                      $"isTrap={isTrap}, isHole={isHole} => Using {(isTrap ? "TRAP" : "HOLE")} death effect");
            StartCoroutine(Die(isTrap));
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CheckTriggerOrCollisionDeath(other?.gameObject);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        CheckTriggerOrCollisionDeath(other?.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = true;
            ApplyBoundaryDrift();
        }
        CheckTriggerOrCollisionDeath(collision.gameObject);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = true;
            ApplyBoundaryDrift();
        }
        CheckTriggerOrCollisionDeath(collision.gameObject);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = false;
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
        if (tyreMarkPrefab == null || isJumping || Mathf.Abs(turn) < 0.5f) return;
        if (isTouchingBoundary || boundaryDriftTimer > 0f) return;

        float sidewaysVelocity = GetSidewaysVelocity();
        float currentDriftThreshold = driftThreshold;

        if (Mathf.Abs(sidewaysVelocity) < currentDriftThreshold) return;

        UpdateTyreMark(frontLeftTyre, ref lastFrontLeftPosition, ref frontLeftDistance);
        UpdateTyreMark(frontRightTyre, ref lastFrontRightPosition, ref frontRightDistance);
        UpdateTyreMark(rearLeftTyre, ref lastRearLeftPosition, ref rearLeftDistance);
        UpdateTyreMark(rearRightTyre, ref lastRearRightPosition, ref rearRightDistance);
    }

    public void ApplyBoundaryDrift()
    {
        boundaryDriftTimer = boundaryDriftCooldown;
    }

    private void UpdateTyreMark(Transform tyre, ref Vector2 lastPosition, ref float distance)
    {
        if (tyre == null) return;

        Vector2 currentPosition = tyre.position;
        float movementDistance = Vector2.Distance(currentPosition, lastPosition);
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
        if (tyreMarkPrefab == null || tyre == null || isJumping) return;

        Quaternion rotation = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + tyreMarkRotationOffset);
        GameObject tyreMark = Instantiate(tyreMarkPrefab, tyre.position, rotation);
        Destroy(tyreMark, tyreMarkLifetime);
    }

    void OnDestroy()
    {
        fallbackMoveAction?.Dispose();
        fallbackJumpAction?.Dispose();
    }

    public void EnableJump()
    {
        if (isJumping) return;
        canJump = true;

        // In continuous jump mode, auto-execute jump immediately without waiting for button press
        if (continuousJump && isBoosted && !isDead && !hasWonPlayer)
        {
            TryExecuteJump();
        }
    }

    private void OnLandingGround()
    {
        if (!isJumping) return;

        isJumping = false;
        nextJumpTime = 0f; // Reset jump cooldown immediately upon landing back on Ground layer!

        Physics2D.IgnoreLayerCollision(playerLayer, jumpCollisionLayer, false);

        if (spawnedJumpEffect != null)
        {
            Destroy(spawnedJumpEffect);
            spawnedJumpEffect = null;
        }

        if (spawnedShadow != null)
        {
            spawnedShadow.DisableShadow();
        }

        jumpCoroutine = null;

        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.TriggerShake(landingShakeDuration, landingShakeIntensity);
        }
    }

    private IEnumerator JumpEffect()
    {
        isJumping = true;

        if (jumpEffectPrefab != null)
        {
            spawnedJumpEffect = Instantiate(jumpEffectPrefab, transform.position, Quaternion.identity);
        }

        Physics2D.IgnoreLayerCollision(playerLayer, jumpCollisionLayer, true);

        if (spawnedShadow == null && shadowPrefab != null)
        {
            spawnedShadow = Instantiate(shadowPrefab, transform.position, transform.rotation);
        }

        if (spawnedShadow != null)
        {
            spawnedShadow.Initialize(transform, jumpDuration);
        }

        float elapsedTime = 0f;
        Vector3 peakScale = originalCarScale * carJumpScale;

        try
        {
            while (elapsedTime < jumpDuration && isJumping)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / jumpDuration);
                float arc = Mathf.Sin(t * Mathf.PI);

                transform.localScale = Vector3.Lerp(originalCarScale, peakScale, arc);
                yield return null;
            }
        }
        finally
        {
            transform.localScale = originalCarScale;
            OnLandingGround();
        }
    }

    private Vector2 ReadMoveVectorInput()
    {
        Vector2 input = Vector2.zero;

        if (MoveInput != null)
        {
            try
            {
                if (!MoveInput.enabled) MoveInput.Enable();
                input = MoveInput.ReadValue<Vector2>();
            }
            catch { }
        }

        if (input.sqrMagnitude < 0.01f && Keyboard.current != null)
        {
            float x = 0f;
            float y = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;

            if (x != 0f || y != 0f) input = new Vector2(x, y);
        }

        if (input.sqrMagnitude < 0.01f && Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            Vector2 dpad = Gamepad.current.dpad.ReadValue();
            if (stick.sqrMagnitude > 0.04f) input = stick;
            else if (dpad.sqrMagnitude > 0.04f) input = dpad;
        }

        return input;
    }

    private void CreateFallbackActions()
    {
        fallbackMoveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

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

        fallbackMoveAction.AddBinding("<Gamepad>/leftStick");

        fallbackJumpAction = new InputAction("Jump", InputActionType.Button);
        fallbackJumpAction.AddBinding("<Keyboard>/space");
        fallbackJumpAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private static void EnableInput(InputAction action)
    {
        if (action != null && !action.enabled)
        {
            action.Enable();
        }
    }

    private static void DisableInput(InputAction action)
    {
        if (action != null && action.enabled)
        {
            action.Disable();
        }
    }

    private IEnumerator Die(bool isTrap = false)
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

        GameObject effectToSpawn = isTrap
            ? (trapDeathEffect != null ? trapDeathEffect : Resources.Load<GameObject>("ExplosionEffect") ?? Resources.Load<GameObject>("DeathEffect") ?? smokeEffect)
            : (holeDeathEffect != null ? holeDeathEffect : Resources.Load<GameObject>("SplashEffect") ?? smokeEffect);

        Debug.Log($"[CarControllerSingle] Die: isTrap={isTrap}, " +
                  $"trapDeathEffect={(trapDeathEffect != null ? trapDeathEffect.name : "NULL")}, " +
                  $"holeDeathEffect={(holeDeathEffect != null ? holeDeathEffect.name : "NULL")}, " +
                  $"selectedEffect={(effectToSpawn != null ? effectToSpawn.name : "NULL")}");

        if (effectToSpawn != null)
        {
            GameObject fx = Instantiate(effectToSpawn, transform.position, Quaternion.identity);
            SpriteRenderer sr = fx.GetComponent<SpriteRenderer>() ?? fx.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Player";
                sr.sortingOrder = 50;
            }
            Renderer[] rends = fx.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r != null)
                {
                    r.sortingLayerName = "Player";
                    r.sortingOrder = 50;
                }
            }
            StartCoroutine(AutoCleanEffectRoutine(fx));
        }
        else if (smokeEffect != null)
        {
            GameObject fx = Instantiate(smokeEffect, transform.position, Quaternion.identity);
            Renderer[] rends = fx.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r != null)
                {
                    r.sortingLayerName = "Player";
                    r.sortingOrder = 50;
                }
            }
            StartCoroutine(AutoCleanEffectRoutine(fx));
        }

        if (DeathMarkerManager.Instance != null)
        {
            DeathMarkerManager.Instance.SpawnDeathMarker(transform.position);
        }

        if (GameoverMenu != null)
        {
            GameoverMenu.SetActive(true);
        }

        if (deathSequenceDelay > 0f)
        {
            yield return new WaitForSeconds(deathSequenceDelay);
        }
        else
        {
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator AutoCleanEffectRoutine(GameObject effectObj, float maxTimeout = 2.5f)
    {
        if (effectObj == null) yield break;

        Animator anim = effectObj.GetComponent<Animator>() ?? effectObj.GetComponentInChildren<Animator>();
        ParticleSystem ps = effectObj.GetComponent<ParticleSystem>() ?? effectObj.GetComponentInChildren<ParticleSystem>();

        float timer = 0f;
        bool animStarted = false;

        while (effectObj != null && timer < maxTimeout)
        {
            timer += Time.deltaTime;

            if (anim != null && anim.isActiveAndEnabled)
            {
                AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.normalizedTime > 0.05f) animStarted = true;
                if (animStarted && info.normalizedTime >= 1.0f) break;
            }
            else if (ps != null)
            {
                if (!ps.IsAlive(true) && timer > 0.1f) break;
            }

            yield return null;
        }

        if (effectObj != null)
        {
            Destroy(effectObj);
        }
    }
}