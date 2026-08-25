using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
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

    [Header("Death Camera Shake")]
    [SerializeField] private float deathShakeDuration = 0.4f;
    [SerializeField] private float deathShakeIntensity = 0.5f;

    [Header("Player Lights (Disabled on Death, Enabled on Respawn)")]
    [SerializeField] private GameObject[] playerLightObjects;
    [SerializeField] private Behaviour[] playerLights;

    [Header("Shield Settings")]
    [SerializeField] private GameObject shieldVisualEffectPrefab;
    [SerializeField] private Vector3 shieldVisualScale = new Vector3(1.2f, 1.2f, 1f);
    [SerializeField] private int shieldSortingOrderOffset = 1;
    private bool isShielded = false;
    private Coroutine shieldCoroutine;
    private GameObject activeShieldVisual;

    public bool isInvulnerableDuringSpawn { get; set; } = false;
    public bool IsShielded => isShielded;

    public event Action OnDeath;
    public event Action OnRespawn;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D carCollider;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        carCollider = GetComponent<Collider2D>();
    }

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
        isDead.OnValueChanged += OnDeadStateChanged;

        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }

        // Hide Dead UI initially
        HideDeadUI();
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isDead.OnValueChanged -= OnDeadStateChanged;
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
            HandleDeathVisuals();
            OnDeath?.Invoke();
            ShowDeadUI();
        }
        else
        {
            HandleRespawnVisuals();
            OnRespawn?.Invoke();
            HideDeadUI();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Owner's own collider detects real-time trap contact
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value || isInvulnerableDuringSpawn || isShielded) return;

        if (other.CompareTag("Trap") || other.CompareTag("Hole"))
        {
            RequestTakeDamageRpc(maxHealth);
            PlayDeathEffectsRpc(transform.position);
        }
    }

    [Rpc(SendTo.Everyone)]
    public void ActivateShieldRpc(float duration)
    {
        if (shieldCoroutine != null) StopCoroutine(shieldCoroutine);
        shieldCoroutine = StartCoroutine(ShieldRoutine(duration));
    }

    private IEnumerator ShieldRoutine(float duration)
    {
        isShielded = true;
        Debug.Log($"[CarHealth SUCCESS] Shield Activated for {duration}s! (Player IsOwner: {IsOwner})");

        if (shieldVisualEffectPrefab != null && activeShieldVisual == null)
        {
            activeShieldVisual = Instantiate(shieldVisualEffectPrefab);
            activeShieldVisual.transform.SetParent(transform, false);
            activeShieldVisual.transform.localPosition = Vector3.zero;
            activeShieldVisual.transform.localRotation = Quaternion.identity;
            activeShieldVisual.transform.localScale = shieldVisualScale;

            // Ensure shield renders on top of player car sprite
            SpriteRenderer shieldSr = activeShieldVisual.GetComponent<SpriteRenderer>();
            if (shieldSr != null && spriteRenderer != null)
            {
                shieldSr.sortingLayerID = spriteRenderer.sortingLayerID;
                shieldSr.sortingOrder = spriteRenderer.sortingOrder + shieldSortingOrderOffset;
            }
        }
        else if (spriteRenderer != null)
        {
            // Fallback visual cyan tint if no shield visual prefab assigned
            spriteRenderer.color = new Color(0.2f, 0.9f, 1f, 0.85f);
        }

        yield return new WaitForSeconds(duration);

        isShielded = false;
        Debug.Log($"[CarHealth] Shield Expired for Player (IsOwner: {IsOwner})");

        if (activeShieldVisual != null)
        {
            Destroy(activeShieldVisual);
            activeShieldVisual = null;
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
        }

        shieldCoroutine = null;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestTakeDamageRpc(int amount)
    {
        TakeDamageServer(amount);
    }

    public void TakeDamageServer(int amount)
    {
        if (!IsServer || isDead.Value || isInvulnerableDuringSpawn || isShielded) return;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);
        if (currentHealth.Value <= 0)
        {
            DieServerAuthoritative();
        }
    }

    private void DieServerAuthoritative()
    {
        if (!IsServer || isInvulnerableDuringSpawn || isShielded) return;

        isDead.Value = true;
        deathCount.Value++;
    }

    [Rpc(SendTo.Everyone)]
    private void PlayDeathEffectsRpc(Vector3 deathPosition)
    {
        if (smokeEffect != null)
        {
            GameObject smokeObj = Instantiate(smokeEffect, deathPosition, Quaternion.identity);
            Destroy(smokeObj, smokeEffectLifetime);
        }

        if (deathSound != null)
        {
            AudioSource.PlayClipAtPoint(deathSound, deathPosition, 1.0f);
        }

        if (DeathMarkerManager.Instance != null)
        {
            DeathMarkerManager.Instance.SpawnDeathMarker(deathPosition);
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

        isInvulnerableDuringSpawn = false;
        currentHealth.Value = maxHealth;
        isDead.Value = false;
        if (carCollider != null) carCollider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.enabled = true;
    }

    private void HandleDeathVisuals()
    {
        if (carCollider != null) carCollider.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        if (spriteRenderer != null) spriteRenderer.enabled = false;

        SetLightsState(false);

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
        if (carCollider != null) carCollider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        SetLightsState(true);

        if (IsOwner)
        {
            CameraZoom2D camZoom = Camera.main != null ? Camera.main.GetComponent<CameraZoom2D>() : null;
            if (camZoom != null)
            {
                camZoom.ResetZoom();
            }
        }
    }

    private void SetLightsState(bool enabledState)
    {
        if (playerLightObjects != null)
        {
            foreach (GameObject lightObj in playerLightObjects)
            {
                if (lightObj != null) lightObj.SetActive(enabledState);
            }
        }

        if (playerLights != null)
        {
            foreach (Behaviour lightComp in playerLights)
            {
                if (lightComp != null) lightComp.enabled = enabledState;
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

            Button btn = deadPanel.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(OnRestartButtonClicked);
            }
        }
    }

    private void HideDeadUI()
    {
        if (!IsOwner) return;

        GameObject deadPanel = GetDeadPanelInScene();
        if (deadPanel != null)
        {
            deadPanel.SetActive(false);
        }
    }

    private void OnRestartButtonClicked()
    {
        RequestRespawnServerRpc();
    }
}
