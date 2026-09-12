using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FrogHoleSpawner : MonoBehaviour
{
    public static FrogHoleSpawner Instance { get; private set; }

    [Header("Frog Prefab / Template")]
    [SerializeField] private GameObject frogPrefab;

    [Header("Spawn Interval")]
    [SerializeField] private float minSpawnInterval = 2.5f;
    [SerializeField] private float maxSpawnInterval = 6.0f;

    [Header("Auto Discovery")]
    [SerializeField] private bool autoFindHoles = true;
    [SerializeField] private List<Collider2D> holeColliders = new List<Collider2D>();

    private readonly List<FrogHoleJumper> frogPool = new List<FrogHoleJumper>();
    private float nextSpawnTime = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        if (autoFindHoles)
        {
            FindSceneHoles();
        }

        ScheduleNextSpawn();
    }

    public void FindSceneHoles()
    {
        holeColliders.Clear();

        Collider2D[] allColliders = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Collider2D col in allColliders)
        {
            if (col == null || col.isTrigger == false && col.gameObject.layer == 0) continue;

            bool isHole = col.CompareTag("Hole") ||
                          string.Equals(LayerMask.LayerToName(col.gameObject.layer), "Hole", System.StringComparison.OrdinalIgnoreCase) ||
                          col.gameObject.name.ToLower().Contains("hole") ||
                          col.gameObject.name.ToLower().Contains("water");

            if (isHole && !holeColliders.Contains(col))
            {
                holeColliders.Add(col);
            }
        }
    }

    private void Update()
    {
        // Refresh hole list if it became empty (e.g., scene changed)
        if (holeColliders == null || holeColliders.Count == 0)
        {
            FindSceneHoles();
            // If still no holes, skip spawning this frame
            if (holeColliders == null || holeColliders.Count == 0)
            {
                // Optional: log warning for developers
                Debug.LogWarning("[FrogHoleSpawner] No hole colliders found – skipping frog spawn.");
                return;
            }
        }

        if (Time.time >= nextSpawnTime)
        {
            SpawnAndJumpRandomFrog();
            ScheduleNextSpawn();
        }
    }

    private void ScheduleNextSpawn()
    {
        nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    private void SpawnAndJumpRandomFrog()
    {
        if (holeColliders.Count == 0) return;

        // Pick a random hole
        Collider2D randomHole = holeColliders[Random.Range(0, holeColliders.Count)];
        if (randomHole == null || !randomHole.gameObject.activeInHierarchy) return;
        // Choose a different hole as the jump destination
        int sourceIndex = Random.Range(0, holeColliders.Count);
        int targetIndex = sourceIndex;
        if (holeColliders.Count > 1)
        {
            while (targetIndex == sourceIndex)
            {
                targetIndex = Random.Range(0, holeColliders.Count);
            }
        }
        Collider2D sourceHole = holeColliders[sourceIndex];
        Collider2D targetHole = holeColliders[targetIndex];

        if (sourceHole == null || !sourceHole.gameObject.activeInHierarchy) return;

        Bounds sourceBounds = sourceHole.bounds;
        Vector3 spawnPos = new Vector3(
            Random.Range(sourceBounds.min.x + 0.2f, sourceBounds.max.x - 0.2f),
            Random.Range(sourceBounds.min.y + 0.2f, sourceBounds.max.y - 0.2f),
            0f
        );

        Vector3 targetCenter = targetHole != null ? targetHole.bounds.center : spawnPos;
        Vector2 jumpDir = (targetCenter - spawnPos).normalized;
        FrogHoleJumper jumper = GetOrCreateFrog();
        if (jumper != null)
        {
            Debug.Log($"[FrogHoleSpawner] Spawning frog at {spawnPos} jumping towards hole at {targetCenter}");
            jumper.LaunchJump(spawnPos, jumpDir);
        }
        else
        {
            Debug.LogWarning("[FrogHoleSpawner] Failed to obtain a FrogHoleJumper instance.");
        }
    }

    private FrogHoleJumper GetOrCreateFrog()
    {
        foreach (var frog in frogPool)
        {
            if (frog != null && !frog.gameObject.activeInHierarchy)
            {
                return frog;
            }
        }

        GameObject newFrogObj = null;
        if (frogPrefab != null)
        {
            newFrogObj = Instantiate(frogPrefab, transform);
        }
        else
        {
            // Procedurally create frog object with sprite if no prefab assigned
            newFrogObj = new GameObject("Frog_HoleJumper");
            newFrogObj.transform.SetParent(transform);

            SpriteRenderer sr = newFrogObj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 50;

            // Load green froglet sprite if available
            Sprite frogSprite = Resources.Load<Sprite>("froglet_frog_green_sheet_jump");
            if (frogSprite != null) sr.sprite = frogSprite;
        }

        FrogHoleJumper jumper = newFrogObj.GetComponent<FrogHoleJumper>();
        if (jumper == null) jumper = newFrogObj.AddComponent<FrogHoleJumper>();

        frogPool.Add(jumper);
        return jumper;
    }
}

