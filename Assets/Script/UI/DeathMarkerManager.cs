using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DeathMarkerManager : MonoBehaviour
{
    public static DeathMarkerManager Instance { get; private set; }

    [Header("Death Marker Settings")]
    [SerializeField] private GameObject deathMarkerPrefab;
    [SerializeField] private Vector3 spawnOffset = Vector3.zero;
    [SerializeField] private int maxMarkersPerPlayer = 5;

    // Per-player active death marker tracking (keyed by Network Client ID)
    private readonly Dictionary<ulong, Queue<GameObject>> playerMarkers = new Dictionary<ulong, Queue<GameObject>>();

    private int currentSceneBuildIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        currentSceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
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
        if (scene.buildIndex == currentSceneBuildIndex)
        {
            return;
        }

        ClearDeathMarkers();
        currentSceneBuildIndex = scene.buildIndex;
    }

    public void SpawnDeathMarker(Vector3 position, ulong playerId = 0)
    {
        if (deathMarkerPrefab == null)
        {
            return;
        }

        Vector3 finalPosition = position + spawnOffset;

        if (!playerMarkers.ContainsKey(playerId))
        {
            playerMarkers[playerId] = new Queue<GameObject>();
        }

        // Limit maximum active markers per player to 5. Destroy oldest mark when exceeding threshold.
        while (playerMarkers[playerId].Count >= maxMarkersPerPlayer)
        {
            GameObject oldestMarker = playerMarkers[playerId].Dequeue();
            if (oldestMarker != null)
            {
                Destroy(oldestMarker);
            }
        }

        GameObject marker = Instantiate(
            deathMarkerPrefab,
            finalPosition,
            Quaternion.identity
        );

        DontDestroyOnLoad(marker);

        // Disable colliders on spawned death marker
        Collider2D[] colliders2D = marker.GetComponentsInChildren<Collider2D>(true);
        foreach (Collider2D collider in colliders2D)
        {
            collider.enabled = false;
        }

        Collider[] colliders3D = marker.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders3D)
        {
            collider.enabled = false;
        }

        playerMarkers[playerId].Enqueue(marker);
    }

    public void ClearDeathMarkers()
    {
        foreach (var pair in playerMarkers)
        {
            foreach (GameObject marker in pair.Value)
            {
                if (marker != null)
                {
                    Destroy(marker);
                }
            }
        }

        playerMarkers.Clear();
    }
}