using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[System.Serializable]
public struct SceneJumpOverride
{
    public string sceneName;
    public float jumpDuration;
    public float jumpCooldown;
    public float speed;
    public float turnSpeed;
    public bool continuousJump;
}

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class NetworkCarController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 8.5f;
    public float turnSpeed = 220f; // Snappy 360-degree rotational responsiveness
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference JumpAction;

    public float currentSpeed;
    private float boostCooldownTimer;

    [Header("Drift & Smooth Handling")]
    [SerializeField] private float driftTurnMultiplier = 1.45f;
    [SerializeField] private float driftFactor = 0.94f; // Responsive traction
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

    [Header("Surface Modifiers (Mud / Slime / Oil)")]
    private float surfaceSpeedMultiplier = 1.0f;
    private float surfaceDriftMultiplier = 1.0f;
    private bool isInSurfaceModifierZone = false;

    [Header("Jump Effect Settings")]
    [SerializeField] private float jumpDuration = 0.32f;
    [SerializeField] private float jumpCooldown = 1.5f;
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
    private bool canJump = false; // Jump unlocked strictly upon contact with JumpTrigger
    private bool continuousJump = false; // When true, auto-jumps while inside JumpTrigger
    private bool isJumping = false; // Prevents tyre mark spawning while airborne
    private float jumpStartTime = -100f;
    private float nextJumpTime = 0f;
    private int lastActionFrame = -1; // Single-frame lock to prevent double execution on 1st press

    public bool IsJumping => isJumping;
    public float JumpDuration => jumpDuration;
    public bool IsJumpDurationExpired => (Time.time - jumpStartTime) >= jumpDuration;

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

    [Header("Car Sound Effects")]
    [SerializeField] private AudioClip engineStartSound;
    [SerializeField] private AudioClip driftSound;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioSource driftAudioSource;
    [SerializeField] private AudioSource effectsAudioSource;

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

    // Networked scene name for independent level progression visibility
    public NetworkVariable<Unity.Collections.FixedString32Bytes> currentSceneNet = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
        string.Empty,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private CapsuleCollider2D boxCollider;
    private CarHealth healthComp;
    private InputAction fallbackMoveAction;

    // Owner-authoritative physics state
    private bool isBoosted = false; // Represents whether car movement has started
    public bool hasWonPlayer { get; private set; } = false;
    public static NetworkCarController LocalPlayerInstance { get; private set; }

    private InputAction MoveInput => moveAction != null ? moveAction.action : fallbackMoveAction;
    private InputAction JumpInput => JumpAction != null ? JumpAction.action : fallbackJumpAction;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        LocalPlayerInstance = this;
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<CapsuleCollider2D>();
        healthComp = GetComponent<CarHealth>();

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate; // Enable physics interpolation for silky smooth 60/144fps movement
        }

        originalCarScale = transform.localScale;
        currentSpeed = speed;
        canJump = false;
        isJumping = false;
        jumpStartTime = -100f;
        isTouchingBoundary = false;
        lastActionFrame = -1;

        ApplyLevelJumpSettings();
        SetupStartPromptVisual();
        InitializeAudioSources();
        CreateFallbackActions();
        InitializeTyrePositions();
    }

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
            return;
        }

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

    private void InitializeAudioSources()
    {
        if (driftAudioSource == null)
        {
            driftAudioSource = gameObject.AddComponent<AudioSource>();
            driftAudioSource.loop = true;
            driftAudioSource.playOnAwake = false;
            driftAudioSource.spatialBlend = 0.5f;
        }

        if (effectsAudioSource == null)
        {
            effectsAudioSource = gameObject.AddComponent<AudioSource>();
            effectsAudioSource.loop = false;
            effectsAudioSource.playOnAwake = false;
            effectsAudioSource.spatialBlend = 0.5f;
        }
    }

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);
        canJump = false;
        isJumping = false;
        jumpStartTime = -100f;
        isTouchingBoundary = false;
        lastActionFrame = -1;

        ApplyLevelJumpSettings();
        // Disable player-to-player collision
        Physics2D.IgnoreLayerCollision(playerLayer, playerLayer, true);
        Physics2D.IgnoreLayerCollision(playerLayer, jumpCollisionLayer, true);
        Physics2D.IgnoreLayerCollision(jumpCollisionLayer, jumpCollisionLayer, true);

        if (boxCollider != null)
        {
            NetworkCarController[] allCars = UnityEngine.Object.FindObjectsByType<NetworkCarController>(FindObjectsSortMode.None);
            foreach (var otherCar in allCars)
            {
                if (otherCar != this)
                {
                    Collider2D otherCol = otherCar.GetComponent<Collider2D>();
                    if (otherCol != null)
                    {
                        Physics2D.IgnoreCollision(boxCollider, otherCol, true);
                    }
                }
            }
        }

        if (rb != null)
        {
            if (IsOwner || IsLocalPlayer)
            {
                rb.simulated = true;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                rb.bodyType = RigidbodyType2D.Dynamic;
            }
            else
            {
                rb.simulated = false; // Prevents 2D physics from fighting NetworkTransform interpolation on remote cars
            }
        }

        if (healthComp != null)
        {
            healthComp.OnRespawn += ResetCarBoostStateLocal;
        }

        isHostCarNet.OnValueChanged += OnHostCarStateChanged;
        currentSceneNet.OnValueChanged += OnCurrentSceneChanged;

        if (IsServer)
        {
            isHostCarNet.Value = (OwnerClientId == NetworkManager.ServerClientId);
            currentSceneNet.Value = SceneManager.GetActiveScene().name;
        }
        else if (IsOwner && IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            UpdateCurrentSceneServerRpc(SceneManager.GetActiveScene().name);
        }

        ApplyCarVisuals(isHostCarNet.Value);
        ResetCarBoostStateLocal();
        UpdateAllCarsSceneVisibility();

        if (IsOwner || IsLocalPlayer)
        {
            EnableInput(MoveInput);
            EnableInput(JumpInput);

            CameraFollow camFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (camFollow == null)
            {
                camFollow = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
            }

            if (camFollow != null)
            {
                camFollow.SetTarget(transform);
            }
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name.Equals("Ending", StringComparison.OrdinalIgnoreCase) || scene.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            Renderer[] allR = GetComponentsInChildren<Renderer>(true);
            foreach (var r in allR) if (r != null) r.enabled = false;
            Collider2D[] allC = GetComponentsInChildren<Collider2D>(true);
            foreach (var c in allC) if (c != null) c.enabled = false;
            StopCarAudio();
            if (startPromptVisual != null) startPromptVisual.SetActive(false);
            if (rb != null)
            {
                rb.simulated = false;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            if ((IsOwner || IsLocalPlayer) && IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (IsServer)
                {
                    currentSceneNet.Value = scene.name;
                }
                else
                {
                    UpdateCurrentSceneServerRpc(scene.name);
                }
            }
            return;
        }

        ResetCarBoostStateLocal();
        ApplyLevelJumpSettings();

        if (healthComp != null)
        {
            healthComp.ResetLocalSpawnState();
        }

        if (IsOwner || IsLocalPlayer)
        {
            EnableInput(MoveInput);
            EnableInput(JumpInput);

            if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (IsServer)
                {
                    currentSceneNet.Value = scene.name;
                }
                else
                {
                    UpdateCurrentSceneServerRpc(scene.name);
                }
            }

            RepositionToSceneSpawnPoint();
            StartCoroutine(DeferredRepositionRoutine());
        }

        UpdateAllCarsSceneVisibility();
    }

    private IEnumerator DeferredRepositionRoutine()
    {
        yield return null;
        if (IsOwner || IsLocalPlayer)
        {
            RepositionToSceneSpawnPoint();
        }
        UpdateVisibilityBasedOnScene();
    }

    public void RepositionToSceneSpawnPoint()
    {
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        bool foundSpawn = false;

        bool isHost = IsServer || (NetworkManager.Singleton != null && OwnerClientId == NetworkManager.ServerClientId) || isHostCarNet.Value;
        int playerIndex = isHost ? 0 : 1;

        if (SpawnPointConfig.Instance != null)
        {
            spawnPos = SpawnPointConfig.Instance.GetSpawnPosition(playerIndex, out spawnRot);
            foundSpawn = true;
        }

        if (!foundSpawn)
        {
            SpawnPoint[] points = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (points != null && points.Length > 0)
            {
                Array.Sort(points, (a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));
                int index = Mathf.Clamp(playerIndex, 0, points.Length - 1);
                spawnPos = points[index].transform.position;
                spawnRot = points[index].transform.rotation;
                foundSpawn = true;
            }
        }

        if (!foundSpawn)
        {
            GameObject[] tagged = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (tagged != null && tagged.Length > 0)
            {
                Array.Sort(tagged, (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                int index = Mathf.Clamp(playerIndex, 0, tagged.Length - 1);
                spawnPos = tagged[index].transform.position;
                spawnRot = tagged[index].transform.rotation;
                foundSpawn = true;
            }
        }

        if (!foundSpawn)
        {
            spawnPos = new Vector3(-9.97f, playerIndex == 1 ? -6.46f : -5.4f, 0f);
        }

        if (rb != null)
        {
            rb.position = spawnPos;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        NetworkTransform netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null && netTransform.CanCommitToTransform)
        {
            netTransform.Teleport(spawnPos, spawnRot, transform.localScale);
        }
        else
        {
            transform.SetPositionAndRotation(spawnPos, spawnRot);
        }

        if (TryGetComponent<CarRespawn>(out var respawnComp))
        {
            respawnComp.SetCheckpoint(spawnPos, spawnRot);
        }

        CameraFollow camFollow = CameraFollow.Instance != null ? CameraFollow.Instance : (Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null);
        if (camFollow == null) camFollow = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
        if (camFollow != null && (IsOwner || IsLocalPlayer))
        {
            camFollow.SetTarget(transform);
            camFollow.transform.position = new Vector3(spawnPos.x, spawnPos.y, camFollow.transform.position.z);
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
        }
        if (boxCollider != null)
        {
            boxCollider.enabled = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        isHostCarNet.OnValueChanged -= OnHostCarStateChanged;
        currentSceneNet.OnValueChanged -= OnCurrentSceneChanged;

        if (healthComp != null)
        {
            healthComp.OnRespawn -= ResetCarBoostStateLocal;
        }

        if (IsOwner || IsLocalPlayer)
        {
            DisableInput(MoveInput);
            DisableInput(JumpInput);
        }

        StopCarAudio();
    }

    private void OnCurrentSceneChanged(Unity.Collections.FixedString32Bytes previousState, Unity.Collections.FixedString32Bytes newState)
    {
        UpdateAllCarsSceneVisibility();

        string sceneStr = newState.ToString();
        if (sceneStr.Equals("Ending", StringComparison.OrdinalIgnoreCase))
        {
            string localScene = SceneManager.GetActiveScene().name;
            if (!localScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
                !localScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                if (LevelTimer.Instance != null)
                {
                    LevelTimer.Instance.TriggerTimeOverFromMatchEnd();
                }
            }
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyMatchEndedRpc(ulong winnerClientId = 9999)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == winnerClientId)
        {
            return;
        }

        if (FinishLine.LocalPlayerHasWon || hasWonPlayer)
        {
            return;
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (!currentScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
            !currentScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (LevelTimer.Instance != null)
            {
                LevelTimer.Instance.TriggerTimeOverFromMatchEnd();
            }
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

    public void SetCarWonLocal()
    {
        hasWonPlayer = true;
        isBoosted = false;

        if (startPromptVisual != null)
        {
            startPromptVisual.SetActive(false);
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        StopCarAudio();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetCarWonRpc()
    {
        SetCarWonLocal();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void TeleportCarRpc(Vector3 position, Quaternion rotation)
    {
        NetworkTransform netTransform = GetComponent<NetworkTransform>();

        // The car's NetworkTransform is owner-authoritative (AuthorityMode.Owner), so ONLY the motion
        // authority (the owning peer) may commit a Teleport or move the physics body. Calling
        // NetworkTransform.Teleport() on any other peer throws "Teleporting on non-authoritative side
        // is not allowed!". Because this RPC fans out to Everyone, that exception previously fired on
        // the host (for the client's car) and on the client (for the host's car); the thrown RPC
        // aborted message processing and left BOTH cars stranded at the off-screen hide position, so
        // nothing appeared on the client. Non-authority peers receive the new position automatically
        // through NetworkTransform state replication, so they must not touch the transform/rigidbody.
        bool isMotionAuthority = netTransform == null || netTransform.CanCommitToTransform;
        if (!isMotionAuthority)
        {
            StopCarAudio();
            return;
        }

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
        StopCarAudio();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void ResetCarBoostStateRpc()
    {
        ResetCarBoostStateLocal();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpdateCurrentSceneServerRpc(Unity.Collections.FixedString32Bytes sceneName)
    {
        currentSceneNet.Value = sceneName;
        UpdateAllCarsSceneVisibility();
    }

    public bool IsInSameSceneAsLocalPlayer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return true;
        if (IsOwner || IsLocalPlayer) return true;

        string localScene = SceneManager.GetActiveScene().name;
        string carScene = currentSceneNet.Value.ToString();

        if (string.IsNullOrEmpty(carScene))
        {
            return false;
        }

        return string.Equals(carScene, localScene, StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateVisibilityBasedOnScene()
    {
        bool isSameScene = IsInSameSceneAsLocalPlayer();

        // 1. SpriteRenderer & all other Renderers on car and its children
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null)
            {
                r.enabled = isSameScene;
            }
        }

        // Stop all particle systems if in different scene
        ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>(true);
        foreach (var p in particles)
        {
            if (p != null)
            {
                if (!isSameScene)
                {
                    p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        // 2. Colliders
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        foreach (var c in colliders)
        {
            if (c != null)
            {
                c.enabled = isSameScene;
            }
        }

        // 3. Audio sources
        AudioSource[] audios = GetComponentsInChildren<AudioSource>(true);
        foreach (var a in audios)
        {
            if (a != null)
            {
                a.mute = !isSameScene;
                if (!isSameScene && a.isPlaying)
                {
                    a.Stop();
                }
            }
        }

        // 4. Start prompt visual
        if (startPromptVisual != null)
        {
            if (!isSameScene)
            {
                startPromptVisual.SetActive(false);
            }
            else if (!isBoosted && !hasWonPlayer && (IsOwner || IsLocalPlayer))
            {
                startPromptVisual.SetActive(true);
            }
            else
            {
                startPromptVisual.SetActive(false);
            }
        }
    }

    public static void UpdateAllCarsSceneVisibility()
    {
        NetworkCarController[] allCars = UnityEngine.Object.FindObjectsByType<NetworkCarController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var car in allCars)
        {
            if (car != null)
            {
                car.UpdateVisibilityBasedOnScene();
            }
        }
    }

    public void ResetCarBoostStateLocal()
    {
        hasWonPlayer = false;
        isBoosted = false;
        canJump = false;
        isJumping = false;
        jumpStartTime = -100f;
        boostCooldownTimer = 0.5f;
        nextJumpTime = 0f;
        isTouchingBoundary = false;
        lastActionFrame = -1;
        ResetSurfaceModifiers();

        ApplyLevelJumpSettings();
        if (LevelJumpSettings.Instance == null)
        {
            StartCoroutine(RetryApplyLevelJumpSettings());
        }
        currentSpeed = speed;
        SetupStartPromptVisual();

        if (healthComp != null)
        {
            healthComp.ResetLocalSpawnState();
        }

        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = playerLayer;
        }
        transform.localScale = originalCarScale;

        if (rb != null)
        {
            if (IsOwner || IsLocalPlayer)
            {
                rb.simulated = true;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else
            {
                rb.simulated = false;
            }
        }

        StopCarAudio();
        UpdateVisibilityBasedOnScene();
    }

    private IEnumerator RetryApplyLevelJumpSettings()
    {
        float timeout = 2f;
        float elapsed = 0f;
        while (LevelJumpSettings.Instance == null && elapsed < timeout)
        {
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        if (LevelJumpSettings.Instance != null)
        {
            ApplyLevelJumpSettings();
            currentSpeed = speed;
        }
    }

    public void ApplySurfaceModifiers(float speedMult, float driftMult)
    {
        surfaceSpeedMultiplier = speedMult;
        surfaceDriftMultiplier = driftMult;
        isInSurfaceModifierZone = true;
    }

    public void ResetSurfaceModifiers()
    {
        surfaceSpeedMultiplier = 1.0f;
        surfaceDriftMultiplier = 1.0f;
        isInSurfaceModifierZone = false;
    }

    public void EnableJump()
    {
        if (isJumping) return; // Ignore jump triggers while mid-air to prevent double jumps!
        canJump = true;


        // In continuous jump mode, auto-execute jump immediately without waiting for button press
        if (continuousJump && isBoosted && !hasWonPlayer)
        {
            TryExecuteJump();
        }
    }

    private void CheckJumpDurationExpiration()
    {
        if (isJumping && IsJumpDurationExpired)
        {
            if (jumpCoroutine != null)
            {
                StopCoroutine(jumpCoroutine);
                jumpCoroutine = null;
            }

            OnLandingGround();
        }
    }

    private void OnLandingGround()
    {
        if (!isJumping) return;

        isJumping = false;
        nextJumpTime = 0f; // Reset jump cooldown immediately upon landing back on Ground layer!

        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = playerLayer;
        }
        transform.localScale = originalCarScale;

        if (IsOwner || IsLocalPlayer)
        {
            if (CameraFollow.Instance != null)
            {
                CameraFollow.Instance.TriggerShake(landingShakeDuration, landingShakeIntensity);
            }
        }

        if (healthComp != null)
        {
            healthComp.CheckImmediateHoleOverlap();
        }
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) || activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (spriteRenderer != null && spriteRenderer.enabled) spriteRenderer.enabled = false;
            if (boxCollider != null && boxCollider.enabled) boxCollider.enabled = false;
            if (startPromptVisual != null && startPromptVisual.activeSelf) startPromptVisual.SetActive(false);
            StopCarAudio();
            return;
        }

        bool inSameScene = IsInSameSceneAsLocalPlayer();

        if (!IsOwner && !IsLocalPlayer)
        {
            if (!inSameScene)
            {
                if (spriteRenderer != null && spriteRenderer.enabled) spriteRenderer.enabled = false;
                Renderer[] allR = GetComponentsInChildren<Renderer>(true);
                foreach (var r in allR) if (r != null && r.enabled) r.enabled = false;

                Collider2D[] allC = GetComponentsInChildren<Collider2D>(true);
                foreach (var c in allC) if (c != null && c.enabled) c.enabled = false;

                StopCarAudio();
                if (startPromptVisual != null && startPromptVisual.activeSelf) startPromptVisual.SetActive(false);
                return;
            }
            else
            {
                if (healthComp != null && !healthComp.isDead.Value)
                {
                    if (spriteRenderer != null && !spriteRenderer.enabled) spriteRenderer.enabled = true;
                    if (boxCollider != null && !boxCollider.enabled) boxCollider.enabled = true;
                }
            }
        }
        else
        {
            if (healthComp != null && !healthComp.isDead.Value && !healthComp.LocalDeathRequested)
            {
                if (spriteRenderer != null && !spriteRenderer.enabled) spriteRenderer.enabled = true;
                if (boxCollider != null && !boxCollider.enabled) boxCollider.enabled = true;
            }
        }

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

        if (!IsOwner && !IsLocalPlayer) return;

        // Check Esc / Gamepad Menu Button to return to MainMenu
        bool menuPressed = false;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            menuPressed = true;
        }

        Gamepad activeGamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
        if (activeGamepad != null && (activeGamepad.startButton.wasPressedThisFrame || activeGamepad.selectButton.wasPressedThisFrame))
        {
            menuPressed = true;
        }

        if (menuPressed)
        {
            ReturnToMainMenu();
            return;
        }

        if (hasWonPlayer) return;
        if (healthComp != null && (healthComp.isDead.Value || healthComp.LocalDeathRequested))
        {
            StopCarAudio();
            return;
        }

        CheckJumpDurationExpiration();

        if (LevelTimer.Instance != null && LevelTimer.Instance.IsTimeOver)
        {
            isBoosted = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            StopCarAudio();
            return;
        }

        bool actionPressed = false;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            actionPressed = true;
        }

        if (activeGamepad != null && activeGamepad.buttonSouth.wasPressedThisFrame)
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
        if (!IsOwner && !IsLocalPlayer) return;
        ApplyLevelJumpSettings();

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

            if (effectsAudioSource != null && engineStartSound != null)
            {
                effectsAudioSource.PlayOneShot(engineStartSound);
            }
        }
        else
        {
            TryExecuteJump();
        }
    }

    private void LateUpdate()
    {
        if (IsOwner || IsLocalPlayer)
        {
            CheckJumpDurationExpiration();
        }
    }

    private void TryExecuteJump()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (!isBoosted || (healthComp != null && (healthComp.isDead.Value || healthComp.LocalDeathRequested)) || hasWonPlayer) return;
        if (LevelTimer.Instance != null && LevelTimer.Instance.IsTimeOver) return;

        if (isJumping) return;
        if (!canJump) return;

        ApplyLevelJumpSettings();

        if (Time.time < nextJumpTime) return;

        nextJumpTime = Time.time + jumpCooldown;
        canJump = false;

        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            TriggerJumpRpc(SceneManager.GetActiveScene().name);
        }
        else
        {
            if (jumpCoroutine != null)
            {
                StopCoroutine(jumpCoroutine);
            }
            jumpCoroutine = StartCoroutine(JumpEffect());
        }
    }

    private void FixedUpdate()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) || activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsOwner && !IsLocalPlayer) return;
        if (hasWonPlayer) return;
        if (healthComp != null && (healthComp.isDead.Value || healthComp.LocalDeathRequested))
        {
            StopCarAudio();
            return;
        }

        CheckJumpDurationExpiration();

        if (LevelTimer.Instance != null && LevelTimer.Instance.IsTimeOver)
        {
            isBoosted = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            StopCarAudio();
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

        if (!isBoosted)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
            StopCarAudio();
            return;
        }

        Vector2 moveInput = ReadMoveVectorInput();
        float targetRotation = rb.rotation;
        float turnMagnitude = 0f;

        if (moveInput.sqrMagnitude > 0.04f)
        {
            float targetAngle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg;
            float activeDriftMultiplier = isInSurfaceModifierZone ? (driftTurnMultiplier * surfaceDriftMultiplier) : driftTurnMultiplier;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(rb.rotation, targetAngle));
            turnMagnitude = Mathf.Clamp01(angleDiff / 90f);

            float turnBoost = Mathf.Lerp(1f, activeDriftMultiplier, turnMagnitude);
            if (boundaryDriftTimer > 0f) turnBoost *= boundaryTurnMultiplier;

            float currentTurnSpeed = turnSpeed * turnBoost;
            targetRotation = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, currentTurnSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(targetRotation);
        }

        float rotationRadians = targetRotation * Mathf.Deg2Rad;
        Vector2 forwardDirection = new Vector2(Mathf.Cos(rotationRadians), Mathf.Sin(rotationRadians));

        float effectiveSpeed = isInSurfaceModifierZone ? (currentSpeed * surfaceSpeedMultiplier) : currentSpeed;
        Vector2 desiredVelocity = forwardDirection * effectiveSpeed;

        float driftFactorBlend = Mathf.Lerp(driftFactor, driftIntensity, turnMagnitude);
        float lerpRate = Mathf.Lerp(18f, 26f, driftFactorBlend);
        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, desiredVelocity, 1f - Mathf.Exp(-lerpRate * Time.fixedDeltaTime));

        UpdateTyreMarks(turnMagnitude);
        UpdateCarAudio(turnMagnitude);
    }

    private bool IsBoundaryCollision(Collision2D collision)
    {
        if (collision == null || collision.gameObject == null) return false;
        return collision.gameObject.CompareTag("Boundary") ||
               string.Equals(LayerMask.LayerToName(collision.gameObject.layer), "Boundary", StringComparison.OrdinalIgnoreCase);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = true;
            ApplyBoundaryDrift();
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = true;
            ApplyBoundaryDrift();
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (IsBoundaryCollision(collision))
        {
            isTouchingBoundary = false;
        }
    }

    private void UpdateCarAudio(float turn)
    {
        float sidewaysVel = GetSidewaysVelocity();
        bool isDrifting = Mathf.Abs(turn) >= 0.35f && Mathf.Abs(sidewaysVel) >= driftThreshold && !isJumping;

        if (isDrifting && driftSound != null)
        {
            if (driftAudioSource != null && (!driftAudioSource.isPlaying || driftAudioSource.clip != driftSound))
            {
                driftAudioSource.clip = driftSound;
                driftAudioSource.Play();
            }
        }
        else
        {
            if (driftAudioSource != null && driftAudioSource.isPlaying)
            {
                driftAudioSource.Stop();
            }
        }
    }

    private void StopCarAudio()
    {
        if (driftAudioSource != null && driftAudioSource.isPlaying) driftAudioSource.Stop();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerJumpRpc(Unity.Collections.FixedString32Bytes sceneName)
    {
        string localScene = SceneManager.GetActiveScene().name;
        if (!string.Equals(sceneName.ToString(), localScene, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsInSameSceneAsLocalPlayer())
        {
            return;
        }

        if (jumpCoroutine != null)
        {
            StopCoroutine(jumpCoroutine);
        }
        jumpCoroutine = StartCoroutine(JumpEffect());
    }

    private IEnumerator JumpEffect()
    {
        if (!IsInSameSceneAsLocalPlayer()) yield break;

        isJumping = true;
        jumpStartTime = Time.time;
        StopCarAudio();

        if (effectsAudioSource != null && jumpSound != null)
        {
            effectsAudioSource.PlayOneShot(jumpSound);
        }

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

        if (boxCollider != null)
        {
            boxCollider.gameObject.layer = jumpCollisionLayer;
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
            OnLandingGround();
        }
    }

    public void ApplyBoundaryDrift()
    {
        boundaryDriftTimer = boundaryDriftCooldown;
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

        if (JumpAction == null && fallbackJumpAction == null)
        {
            fallbackJumpAction = new InputAction("FallbackJump", InputActionType.Button);
            fallbackJumpAction.AddBinding("<Keyboard>/space");
            fallbackJumpAction.AddBinding("<Gamepad>/buttonSouth");

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
        if (isJumping || (boxCollider != null && boxCollider.gameObject.layer == jumpCollisionLayer)) return;
        if (isTouchingBoundary || boundaryDriftTimer > 0f) return;

        if (Mathf.Abs(turn) < 0.35f) return;

        float sidewaysVelocity = GetSidewaysVelocity();
        float currentDriftThreshold = driftThreshold;

        if (Mathf.Abs(sidewaysVelocity) < currentDriftThreshold) return;

        UpdateTyreMark(frontLeftTyre, ref lastFrontLeftPosition, ref frontLeftDistance);
        UpdateTyreMark(frontRightTyre, ref lastFrontRightPosition, ref frontRightDistance);
        UpdateTyreMark(rearLeftTyre, ref lastRearLeftPosition, ref rearLeftDistance);
        UpdateTyreMark(rearRightTyre, ref lastRearRightPosition, ref rearRightDistance);
    }

    private void UpdateTyreMark(Transform tyre, ref Vector2 lastPosition, ref float distance)
    {
        if (tyre == null || (!IsOwner && !IsLocalPlayer) || !IsInSameSceneAsLocalPlayer()) return;

        Vector2 currentPosition = tyre.position;
        distance += Vector2.Distance(currentPosition, lastPosition);

        if (distance >= tyreMarkSpacing)
        {
            float rotationDegrees = transform.eulerAngles.z + tyreMarkRotationOffset;
            SpawnTyreMarkLocal(currentPosition, rotationDegrees);
            lastPosition = currentPosition;
            distance = 0f;
        }
    }

    private void SpawnTyreMarkLocal(Vector2 position, float rotationDegrees)
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

    public void ReturnToMainMenu()
    {
        StopCarAudio();

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene("MainMenu");
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        fallbackMoveAction?.Dispose();
        fallbackJumpAction?.Dispose();
        StopCarAudio();
    }
}
