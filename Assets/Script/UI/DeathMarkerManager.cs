using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DeathMarkerManager : MonoBehaviour
{
    public static DeathMarkerManager Instance;

    [SerializeField] private GameObject deathMarkerPrefab;

    private readonly List<GameObject> deathMarkers =
        new List<GameObject>();

    private int currentSceneBuildIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        currentSceneBuildIndex =
            SceneManager.GetActiveScene().buildIndex;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        if (scene.buildIndex == currentSceneBuildIndex)
        {
            return;
        }

        ClearDeathMarkers();

        currentSceneBuildIndex =
            scene.buildIndex;
    }

    public void SpawnDeathMarker(Vector3 position)
    {
        if (deathMarkerPrefab == null)
        {
            return;
        }

        GameObject marker = Instantiate(
            deathMarkerPrefab,
            position,
            Quaternion.identity
        );

        DontDestroyOnLoad(marker);

        Collider2D[] colliders =
            marker.GetComponentsInChildren<Collider2D>(true);

        foreach (Collider2D collider in colliders)
        {
            collider.enabled = false;
        }

        Collider[] colliders3D =
            marker.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders3D)
        {
            collider.enabled = false;
        }

        deathMarkers.Add(marker);
    }

    private void ClearDeathMarkers()
    {
        foreach (GameObject marker in deathMarkers)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }

        deathMarkers.Clear();
    }
}