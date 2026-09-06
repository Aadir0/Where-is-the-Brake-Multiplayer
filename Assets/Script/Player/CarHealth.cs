using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class CarHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> deathCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Death Effects")]
    [SerializeField] private GameObject smokeEffect;
    [SerializeField] private float smokeEffectLifetime = 3.5f;
    [SerializeField] private AudioClip deathSound;
    [SerializeField] private Vector3 deathPrefabOffset = Vector3.zero;

    [Header("Death Camera Shake & Hit-Stop")]
    [SerializeField] private float deathShakeDuration = 0.4f;
    [SerializeField] private float deathShakeIntensity = 0.5f;
    [SerializeField] private float hitStopDuration = 0.05f;
    [SerializeField] private float hitStopTimeScale = 0.25f;

    public bool isInvulnerableDuringSpawn { get => false; set { } } // Always false — no spawn invulnerability
    public bool IsOverlappingHole => isOverlappingHole;

    public event Action OnDeath;
    public event Action OnRespawn;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D carCollider;
    private NetworkCarController carController;
    private Coroutine enableRestartCoroutine;
    private Coroutine hitStopCoroutine;
    private bool isOverlappingHole = false;
    private bool localDeathRequested = false;
    private bool deathResponseApplied = false;
    private bool isRestartInteractable = false;
    private float spawnGraceTimer = 0.6f;

    // Exposed so NetworkCarController can stop driving the car the INSTANT the owner locally detects
    // death, instead of waiting for the server's isDead NetworkVariable to round-trip back (that
    // round-trip is exactly what made the client car "keep going for a few seconds" after falling in).
    public bool LocalDeathRequested => localDeathRequested;

    public static CarHealth LocalPlayerHealth { get; private set; }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        LocalPlayerHealth = this;
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        carCollider = GetComponent<Collider2D>();
        carController = GetComponent<NetworkCarController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            LocalPlayerHealth = this;
        }

        currentHealth.OnValueChanged += OnHealthChanged;
        isDead.OnValueChanged += OnDeadStateChanged;

        ResetLocalSpawnState();

        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
            deathCount.Value = 0;
        }

        // Hide Dead UI initially
        HideDeadUI();
    }

    public void ResetLocalSpawnState()
    {
        localDeathRequested = false;
        deathResponseApplied = false;
        isOverlappingHole = false;
        spawnGraceTimer = 0.6f;
        if (CameraFollow.Instance != null) CameraFollow.Instance.StopShake();
        HandleRespawnVisuals();
        HideDeadUI();
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isDead.OnValueChanged -= OnDeadStateChanged;
        Time.timeScale = 1.0f;
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedHealth;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedHealth;
        Time.timeScale = 1.0f;
    }

    private void OnSceneLoadedHealth(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (IsServer)
        {
            deathCount.Value = 0;
            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }

        ResetLocalSpawnState();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        Time.timeScale = 1.0f;
    }

    private void Update()
    {
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) || activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (isRestartInteractable && (isDead.Value || localDeathRequested))
        {
            Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
            bool retryPressed = (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame)) ||
                                (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (retryPressed)
            {
                OnRestartButtonClicked();
                return;
            }
        }

        if (spawnGraceTimer > 0f)
        {
            spawnGraceTimer -= Time.deltaTime;
            return;
        }

        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;
        if (localDeathRequested) return;

        CheckImmediateHoleOverlap();
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        if (IsServer && newValue <= 0 && !isDead.Value)
        {
            DieServerAuthoritative();
        }
    }

    private void OnDeadStateChanged(bool previousState, bool newState)
    {
        if (newState)
        {
            // Authoritative confirmation from the server. On the OWNER client this usually runs AFTER
            // ApplyLocalDeathResponse() already fired from client-side prediction, so the guard inside
            // makes it a no-op (no double camera shake / zoom / UI). On every OTHER peer (e.g. the host
            // watching the client's car) this is the first time death is applied, so their copy of the
            // car hides here too.
            ApplyLocalDeathResponse();
            OnDeath?.Invoke();
        }
        else
        {
            localDeathRequested = false;
            deathResponseApplied = false;
            HandleRespawnVisuals();
            OnRespawn?.Invoke();
            HideDeadUI();
        }
    }

    private bool IsHoleOrTrapObject(GameObject obj)
    {
        if (obj == null) return false;

        // Check self, parent, root, and attached rigidbody
        GameObject[] candidates = new GameObject[]
        {
            obj,
            obj.transform.parent != null ? obj.transform.parent.gameObject : null,
            obj.transform.root != null ? obj.transform.root.gameObject : null,
            obj.GetComponent<Rigidbody2D>() != null ? obj.GetComponent<Rigidbody2D>().gameObject : null
        };

        foreach (GameObject candidate in candidates)
        {
            if (candidate == null) continue;

            // 1. Tag Check
            if (candidate.CompareTag("Hole") || candidate.CompareTag("Trap")) return true;

            // 2. Physics Layer Check
            string layerName = LayerMask.LayerToName(candidate.layer);
            if (!string.IsNullOrEmpty(layerName) &&
                (string.Equals(layerName, "Hole", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(layerName, "Trap", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 3. Name Fallback Check
            string candidateName = candidate.name.ToLower();
            if (candidateName.Contains("hole") || candidateName.Contains("trap"))
            {
                return true;
            }
        }

        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsHoleOrTrapObject(other.gameObject))
        {
            isOverlappingHole = true;
            CheckTrapOrHoleContact(other);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (IsHoleOrTrapObject(other.gameObject))
        {
            isOverlappingHole = true;
            CheckTrapOrHoleContact(other);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsHoleOrTrapObject(other.gameObject))
        {
            isOverlappingHole = false;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsHoleOrTrapObject(collision.gameObject))
        {
            isOverlappingHole = true;
            CheckTrapOrHoleContact(collision.collider);
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsHoleOrTrapObject(collision.gameObject))
        {
            isOverlappingHole = true;
            CheckTrapOrHoleContact(collision.collider);
        }
    }

    private void CheckTrapOrHoleContact(Collider2D other)
    {
        if (spawnGraceTimer > 0f) return;
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;
        if (localDeathRequested) return;

        if (carController == null) carController = GetComponent<NetworkCarController>();

        // Check if jump duration has expired (time > jumpDuration)
        bool jumpExpired = carController == null || !carController.IsJumping || carController.IsJumpDurationExpired;

        if (jumpExpired)
        {
            localDeathRequested = true;
            RequestTakeDamageRpc(maxHealth);
            PlayDeathEffectsRpc(transform.position, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            ApplyLocalDeathResponse(); // client-side prediction: stop, hide & show UI NOW; do not wait for isDead to round-trip
        }
    }

    public void CheckImmediateHoleOverlap()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;
        if (localDeathRequested) return;

        if (carController == null) carController = GetComponent<NetworkCarController>();

        bool jumpExpired = carController == null || !carController.IsJumping || carController.IsJumpDurationExpired;

        if (jumpExpired)
        {
            bool insideHoleOrTrap = false;

            // 1. Direct collider overlap check (checks all layers & triggers with useTriggers = true)
            if (carCollider != null)
            {
                ContactFilter2D filter = new ContactFilter2D();
                filter.useTriggers = true; // MUST explicitly query trigger colliders!
                filter.useLayerMask = false;

                List<Collider2D> results = new List<Collider2D>();
                int count = carCollider.Overlap(filter, results);

                for (int i = 0; i < count; i++)
                {
                    Collider2D col = results[i];
                    if (col != null && col.gameObject != gameObject && IsHoleOrTrapObject(col.gameObject))
                    {
                        insideHoleOrTrap = true;
                        break;
                    }
                }
            }

            // 2. Point & Circle & Box 2D Overlap Queries
            if (!insideHoleOrTrap)
            {
                Vector2 pos = transform.position;
                Collider2D[] pointHits = Physics2D.OverlapPointAll(pos);
                if (pointHits != null)
                {
                    foreach (Collider2D hit in pointHits)
                    {
                        if (hit != null && hit.gameObject != gameObject && IsHoleOrTrapObject(hit.gameObject))
                        {
                            insideHoleOrTrap = true;
                            break;
                        }
                    }
                }
            }

            if (!insideHoleOrTrap)
            {
                Vector2 pos = transform.position;
                Collider2D[] circleHits = Physics2D.OverlapCircleAll(pos, 0.4f);
                if (circleHits != null)
                {
                    foreach (Collider2D hit in circleHits)
                    {
                        if (hit != null && hit.gameObject != gameObject && IsHoleOrTrapObject(hit.gameObject))
                        {
                            insideHoleOrTrap = true;
                            break;
                        }
                    }
                }
            }

            if (!insideHoleOrTrap && carCollider != null)
            {
                Collider2D[] boundsHits = Physics2D.OverlapBoxAll(carCollider.bounds.center, carCollider.bounds.size, transform.eulerAngles.z);
                if (boundsHits != null)
                {
                    foreach (Collider2D hit in boundsHits)
                    {
                        if (hit != null && hit.gameObject != gameObject && IsHoleOrTrapObject(hit.gameObject))
                        {
                            insideHoleOrTrap = true;
                            break;
                        }
                    }
                }
            }

            if (insideHoleOrTrap)
            {
                isOverlappingHole = true;
                localDeathRequested = true;
                Debug.Log($"[CarHealth DANGER] Hole Layer detected after jump expired! Executing Death Sequence (IsOwner: {IsOwner})");
                RequestTakeDamageRpc(maxHealth);
                PlayDeathEffectsRpc(transform.position, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                ApplyLocalDeathResponse(); // client-side prediction: stop, hide & show UI NOW; do not wait for isDead to round-trip
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestTakeDamageRpc(int amount)
    {
        TakeDamageServer(amount);
    }

    public void TakeDamageServer(int amount)
    {
        if (!IsServer || isDead.Value) return;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);
        if (currentHealth.Value <= 0)
        {
            DieServerAuthoritative();
        }
    }

    private void DieServerAuthoritative()
    {
        if (!IsServer) return;

        isDead.Value = true;
        deathCount.Value++;
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlayDeathEffectsRpc(Vector3 deathPosition, Unity.Collections.FixedString32Bytes deathSceneName)
    {
        string localScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!string.Equals(deathSceneName.ToString(), localScene, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (carController == null) carController = GetComponent<NetworkCarController>();
        if (carController != null && !carController.IsInSameSceneAsLocalPlayer())
        {
            return;
        }

        Vector3 finalSpawnPosition = deathPosition + deathPrefabOffset;

        if (smokeEffect != null)
        {
            GameObject smokeObj = Instantiate(smokeEffect, finalSpawnPosition, Quaternion.identity);
            Destroy(smokeObj, smokeEffectLifetime);
        }

        if (deathSound != null)
        {
            float sfxVol = AudioManager.Instance != null ? AudioManager.Instance.GetSfxVolume() : 1.0f;
            AudioSource.PlayClipAtPoint(deathSound, finalSpawnPosition, sfxVol);
        }

        if (DeathMarkerManager.Instance != null)
        {
            DeathMarkerManager.Instance.SpawnDeathMarker(finalSpawnPosition, OwnerClientId);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestRespawnServerRpc()
    {
        if (!IsServer) return;

        CarRespawn respawnComp = GetComponent<CarRespawn>();
        if (respawnComp != null)
        {
            respawnComp.RespawnCarServer();
        }

        ResetHealthAndStateServer();
    }

    public void ResetHealthAndStateServer()
    {
        if (!IsServer) return;

        currentHealth.Value = maxHealth;
        isDead.Value = false;
        isOverlappingHole = false;
        localDeathRequested = false;
        deathResponseApplied = false;
        spawnGraceTimer = 0.6f;

        if (carController == null) carController = GetComponent<NetworkCarController>();
        if (carController != null)
        {
            carController.UpdateVisibilityBasedOnScene();
        }
        else
        {
            if (carCollider != null) carCollider.enabled = true;
            if (spriteRenderer != null) spriteRenderer.enabled = true;
        }

        ResetHealthAndStateClientRpc();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void ResetHealthAndStateClientRpc()
    {
        ResetLocalSpawnState();
    }

    // Applies the LOCAL death response exactly once: stop & hide the car (HandleDeathVisuals) and show
    // the Game Over UI (ShowDeadUI, which is owner-guarded internally). Called from two places:
    //   1. Client-side prediction, the instant the OWNER detects a hole/trap  -> immediate feedback.
    //   2. OnDeadStateChanged, when the server's authoritative isDead syncs   -> covers non-owner peers,
    //      and is a harmless no-op on the owner because prediction already ran.
    // The deathResponseApplied guard makes it idempotent, so camera shake/zoom and the UI animation are
    // never triggered twice for a single death. It is reset to false on respawn (OnDeadStateChanged=false).
    private void ApplyLocalDeathResponse()
    {
        if (deathResponseApplied) return;
        deathResponseApplied = true;

        if (IsOwner)
        {
            if (hitStopCoroutine != null) StopCoroutine(hitStopCoroutine);
            hitStopCoroutine = StartCoroutine(HitStopRoutine(hitStopDuration, hitStopTimeScale));
        }

        HandleDeathVisuals();
        ShowDeadUI();
    }

    private IEnumerator HitStopRoutine(float duration, float slowScale)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Time.timeScale = slowScale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1.0f;
        }
        hitStopCoroutine = null;
    }

    private void HandleDeathVisuals()
    {
        isOverlappingHole = false;
        if (carCollider != null) carCollider.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        if (spriteRenderer != null) spriteRenderer.enabled = false;

        if (IsOwner)
        {
            CameraFollow camFollow = CameraFollow.Instance != null ? CameraFollow.Instance : (Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null);
            if (camFollow != null)
            {
                camFollow.TriggerShake(deathShakeDuration, deathShakeIntensity);
            }

            CameraZoom2D camZoom = Camera.main != null ? Camera.main.GetComponent<CameraZoom2D>() : null;
            if (camZoom != null)
            {
                camZoom.StartZoom(transform);
            }
        }
    }

    private void HandleRespawnVisuals()
    {
        isOverlappingHole = false;

        if (carController == null) carController = GetComponent<NetworkCarController>();
        if (carController != null)
        {
            carController.UpdateVisibilityBasedOnScene();
        }
        else
        {
            if (carCollider != null) carCollider.enabled = true;
            if (spriteRenderer != null) spriteRenderer.enabled = true;
        }

        if (IsOwner)
        {
            if (CameraFollow.Instance != null) CameraFollow.Instance.StopShake();
            CameraZoom2D camZoom = Camera.main != null ? Camera.main.GetComponent<CameraZoom2D>() : null;
            if (camZoom != null)
            {
                camZoom.ResetZoom();
            }
        }
    }

    private GameObject GetDeadPanelInScene()
    {
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.CompareTag("Dead") && go.scene.isLoaded)
            {
                return go;
            }
        }
        return GameObject.FindGameObjectWithTag("Dead");
    }

    private void ShowDeadUI()
    {
        if (!IsOwner) return;

        GameObject deadPanel = GetDeadPanelInScene();
        if (deadPanel != null)
        {
            deadPanel.SetActive(true);

            // Populate Death UI labels
            int currentAttempts = deathCount.Value + 1;
            TextMeshProUGUI[] tmps = deadPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in tmps)
            {
                string n = t.gameObject.name.ToLower();
                if (n.Contains("attempt") || n.Contains("count") || n.Contains("death"))
                {
                    t.text = $"ATTEMPTS: {currentAttempts}";
                }
                else if (n.Contains("prompt") || n.Contains("hint") || n.Contains("retry"))
                {
                    t.text = "PRESS [SPACE] / (A) TO RETRY";
                }
            }

            Text[] legacyTexts = deadPanel.GetComponentsInChildren<Text>(true);
            foreach (var t in legacyTexts)
            {
                string n = t.gameObject.name.ToLower();
                if (n.Contains("attempt") || n.Contains("count") || n.Contains("death"))
                {
                    t.text = $"ATTEMPTS: {currentAttempts}";
                }
                else if (n.Contains("prompt") || n.Contains("hint") || n.Contains("retry"))
                {
                    t.text = "PRESS [SPACE] / (A) TO RETRY";
                }
            }

            Button btn = deadPanel.GetComponentInChildren<Button>(true);
            if (enableRestartCoroutine != null) StopCoroutine(enableRestartCoroutine);
            enableRestartCoroutine = StartCoroutine(EnableRestartButtonAfterAnimationRoutine(deadPanel, btn));
        }
    }

    private IEnumerator EnableRestartButtonAfterAnimationRoutine(GameObject deadPanel, Button btn)
    {
        isRestartInteractable = false;
        if (btn != null)
        {
            btn.interactable = false;
        }

        Animator anim = deadPanel.GetComponent<Animator>();
        if (anim == null) anim = deadPanel.GetComponentInChildren<Animator>();

        if (anim != null)
        {
            yield return null; // Allow animator state initialization
            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            float animLength = stateInfo.length > 0f ? stateInfo.length : 0.5f;

            // Wait for GameOver UI animation to finish before enabling restart button
            yield return new WaitForSecondsRealtime(animLength);
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.35f);
        }

        isRestartInteractable = true;

        if (btn != null)
        {
            btn.interactable = true;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnRestartButtonClicked);

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(btn.gameObject);
                btn.Select();
            }
        }

        enableRestartCoroutine = null;
    }

    private void HideDeadUI()
    {
        if (!IsOwner) return;

        isRestartInteractable = false;

        if (hitStopCoroutine != null)
        {
            StopCoroutine(hitStopCoroutine);
            hitStopCoroutine = null;
            Time.timeScale = 1.0f;
        }

        if (enableRestartCoroutine != null)
        {
            StopCoroutine(enableRestartCoroutine);
            enableRestartCoroutine = null;
        }

        GameObject deadPanel = GetDeadPanelInScene();
        if (deadPanel != null)
        {
            deadPanel.SetActive(false);
        }
    }

    private void OnRestartButtonClicked()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (!isDead.Value && !localDeathRequested) return; // Prevent double clicks / re-triggering

        localDeathRequested = false;
        deathResponseApplied = false;
        spawnGraceTimer = 0.6f;
        if (CameraFollow.Instance != null) CameraFollow.Instance.StopShake();
        HideDeadUI();
        HandleRespawnVisuals();

        CarRespawn respawnComp = GetComponent<CarRespawn>();
        if (respawnComp != null)
        {
            respawnComp.RespawnCarLocal();
        }

        if (carController == null) carController = GetComponent<NetworkCarController>();
        if (carController != null)
        {
            carController.ResetCarBoostStateLocal();
        }

        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            RequestRespawnServerRpc();
        }
        else if (IsServer)
        {
            ResetHealthAndStateServer();
        }
    }
}