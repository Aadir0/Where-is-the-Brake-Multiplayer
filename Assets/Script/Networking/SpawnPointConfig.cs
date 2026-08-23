using System.Collections.Generic;
using UnityEngine;

public class SpawnPointConfig : MonoBehaviour
{
    public static SpawnPointConfig Instance { get; private set; }

    [Header("Spawn Points Array")]
    [SerializeField] private Transform[] spawnPoints;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public Transform GetSpawnPoint(int index)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int idx = Mathf.Abs(index) % spawnPoints.Length;
            if (spawnPoints[idx] != null) return spawnPoints[idx];
        }

        SpawnPoint[] components = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (components != null && components.Length > 0)
        {
            int idx = Mathf.Abs(index) % components.Length;
            return components[idx].transform;
        }

        GameObject[] tagged = GameObject.FindGameObjectsWithTag("SpawnPoint");
        if (tagged != null && tagged.Length > 0)
        {
            int idx = Mathf.Abs(index) % tagged.Length;
            return tagged[idx].transform;
        }

        return transform;
    }

    public Vector3 GetSpawnPosition(int index, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        Transform t = GetSpawnPoint(index);
        if (t != null)
        {
            rotation = t.rotation;
            return t.position;
        }
        return new Vector3(index * 3f, 0f, 0f);
    }
}

