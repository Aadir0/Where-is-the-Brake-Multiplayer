using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Death Camera Shake")]
    [SerializeField] private float deathShakeDuration = 0.4f;
    [SerializeField] private float deathShakeIntensity = 0.5f;

    public bool isInvulnerableDuringSpawn { get => false; set { } } // Always false — no spawn invulnerability
    public bool IsOverlappingHole => isOverlappingHole;

    public event Action OnDeath;
    public event Action OnRespawn;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D carCollider;
    private NetworkCarController carController;
    private Coroutine enableRestartCoroutine;
    private bool isOverlappingHole = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        carCollider = GetComponent<Collider2D>();
        carController = GetComponent<NetworkCarController>();
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

    private void Update()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;

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

    private void CheckTrapOrHoleContact(Collider2D other)
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;

        if (carController == null) carController = GetComponent<NetworkCarController>();

        // Check if jump duration has expired (time > jumpDuration)
        bool jumpExpired = carController == null || !carController.IsJumping || carController.IsJumpDurationExpired;

        if (jumpExpired)
        {
            RequestTakeDamageRpc(maxHealth);
            PlayDeathEffectsRpc(transform.position);
        }
    }

    public void CheckImmediateHoleOverlap()
    {
        if (!IsOwner && !IsLocalPlayer) return;
        if (isDead.Value) return;

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
                Debug.Log($"[CarHealth DANGER] Hole Layer detected after jump expired! Executing Death Sequence (IsOwner: {IsOwner})");
                RequestTakeDamageRpc(maxHealth);
                PlayDeathEffectsRpc(transform.position);
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestTakeDamageRpc(int amount)
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

    [Rpc(SendTo.Everyone)]
    private void PlayDeathEffectsRpc(Vector3 deathPosition)
    {
        Vector3 finalSpawnPosition = deathPosition + deathPrefabOffset;

        if (smokeEffect != null)
        {
            GameObject smokeObj = Instantiate(smokeEffect, finalSpawnPosition, Quaternion.identity);
            Destroy(smokeObj, smokeEffectLifetime);
        }

        if (deathSound != null)
        {
            AudioSource.PlayClipAtPoint(deathSound, finalSpawnPosition, 1.0f);
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
        if (carCollider != null) carCollider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.enabled = true;
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
        if (carCollider != null) carCollider.enabled = true;
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        if (IsOwner)
        {
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

            Button btn = deadPanel.GetComponentInChildren<Button>(true);
            if (enableRestartCoroutine != null) StopCoroutine(enableRestartCoroutine);
            enableRestartCoroutine = StartCoroutine(EnableRestartButtonAfterAnimationRoutine(deadPanel, btn));
        }
    }

    private IEnumerator EnableRestartButtonAfterAnimationRoutine(GameObject deadPanel, Button btn)
    {
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
            yield return new WaitForSecondsRealtime(0.4f);
        }

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
        RequestRespawnServerRpc();
    }
}