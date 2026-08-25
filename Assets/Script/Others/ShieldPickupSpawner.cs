using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ShieldPickupSpawner : NetworkBehaviour
{
    public static ShieldPickupSpawner Instance { get; private set; }

    [Header("Pickup Prefab")]
    [SerializeField] private GameObject shieldPickupPrefab;

    [Header("Spawn Layer & Bounds")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Collider2D spawnAreaCollider;
    [SerializeField] private Vector2 spawnAreaMin = new Vector2(-25f, -25f);
    [SerializeField] private Vector2 spawnAreaMax = new Vector2(25f, 25f);

    [Header("Respawn Timing")]
    [SerializeField] private float minRespawnDelay = 5.0f;
    [SerializeField] private float maxRespawnDelay = 8.0f;

    private GameObject currentSpawnedPickup;
    private Coroutine respawnCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SpawnShieldPickupServer();
        }
    }

    public void OnPickupCollectedServer(GameObject pickupObj)
    {
        if (!IsServer) return;

        if (pickupObj != null)
        {
            NetworkObject netObj = pickupObj.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
        }

        currentSpawnedPickup = null;

        if (respawnCoroutine != null) StopCoroutine(respawnCoroutine);
        respawnCoroutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        float delay = Random.Range(minRespawnDelay, maxRespawnDelay);
        Debug.Log($"[ShieldPickupSpawner] Scheduling next Shield Pickup spawn in {delay:F1}s...");
        yield return new WaitForSeconds(delay);

        SpawnShieldPickupServer();
        respawnCoroutine = null;
    }

    public void SpawnShieldPickupServer()
    {
        if (!IsServer || shieldPickupPrefab == null) return;

        // Despawn existing if any
        if (currentSpawnedPickup != null)
        {
            NetworkObject netObj = currentSpawnedPickup.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
        }

        Vector3 spawnPosition = GetRandomGroundPosition();
        currentSpawnedPickup = Instantiate(shieldPickupPrefab, spawnPosition, Quaternion.identity);

        NetworkObject pickupNetObj = currentSpawnedPickup.GetComponent<NetworkObject>();
        if (pickupNetObj != null)
        {
            pickupNetObj.Spawn(true);
            Debug.Log($"[ShieldPickupSpawner SUCCESS] Spawned networked Shield Pickup on Ground layer at Position: {spawnPosition}");
        }
    }

    private Vector3 GetRandomGroundPosition()
    {
        float minX = spawnAreaMin.x;
        float maxX = spawnAreaMax.x;
        float minY = spawnAreaMin.y;
        float maxY = spawnAreaMax.y;

        if (spawnAreaCollider != null)
        {
            Bounds b = spawnAreaCollider.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            float randomX = Random.Range(minX, maxX);
            float randomY = Random.Range(minY, maxY);
            Vector2 randomPos = new Vector2(randomX, randomY);

            // Verify position overlaps with Ground layer
            Collider2D hit = Physics2D.OverlapPoint(randomPos, groundLayer);
            if (hit != null)
            {
                return new Vector3(randomPos.x, randomPos.y, 0f);
            }
        }

        // Fallback random center point if no ground overlap found in 40 attempts
        return new Vector3(Random.Range(minX, maxX), Random.Range(minY, maxY), 0f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;

        float minX = spawnAreaMin.x;
        float maxX = spawnAreaMax.x;
        float minY = spawnAreaMin.y;
        float maxY = spawnAreaMax.y;

        if (spawnAreaCollider != null)
        {
            Bounds b = spawnAreaCollider.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
        }

        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
        Vector3 size = new Vector3(Mathf.Abs(maxX - minX), Mathf.Abs(maxY - minY), 0.1f);
        Gizmos.DrawWireCube(center, size);
    }
}

