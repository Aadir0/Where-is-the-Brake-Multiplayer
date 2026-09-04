using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance { get; private set; }

    [Header("Player Prefabs")]
    [SerializeField] private GameObject playerCarPrefab;
    [SerializeField] private GameObject hostCarPrefab;
    [SerializeField] private GameObject clientCarPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private Vector3 fallbackSpawnPosition = Vector3.zero;

    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();
    private readonly List<ulong> pendingSpawnClients = new List<ulong>();
    private SpawnPoint[] cachedSpawnPoints;
    private bool networkEventsSubscribed = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (playerCarPrefab == null && hostCarPrefab == null && clientCarPrefab == null)
        {
            Debug.LogError("[PlayerSpawner ERROR] No Car Prefabs assigned in PlayerSpawner Inspector!");
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnUnitySceneLoaded;

        // ROOT-CAUSE FIX: NetworkManager.Singleton is assigned inside NetworkManager.OnEnable(), NOT
        // Awake(). PlayerSpawner lives on the SAME GameObject as NetworkManager, and Unity does NOT
        // guarantee the OnEnable order of components on one GameObject -- so Singleton can still be null
        // right now. If we subscribed here directly (behind an `if (Singleton != null)` guard), the guard
        // would fail and OnServerStarted / OnClientConnected / OnSceneEvent would NEVER get subscribed.
        // That left Unity's premature sceneLoaded as the only working spawn trigger (cars on host only),
        // and once that was removed, nothing spawned at all. Defer subscription until Singleton exists.
        StartCoroutine(SubscribeToNetworkEventsWhenReady());
    }

    private IEnumerator SubscribeToNetworkEventsWhenReady()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }
        SubscribeToNetworkEvents();
    }

    private void SubscribeToNetworkEvents()
    {
        if (Instance != this) return;                 // only the surviving DDOL singleton subscribes
        if (networkEventsSubscribed) return;
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // SceneManager is only created when the host/server starts, so it may still be null here.
        // OnServerStarted re-subscribes OnSceneEvent once SceneManager exists (and again on each new
        // session); subscribe now too if it already happens to be available.
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }

        networkEventsSubscribed = true;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        networkEventsSubscribed = false;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        if (IsGameplayScene(activeScene))
        {
            SpawnAllPlayersInScene();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string activeScene = SceneManager.GetActiveScene().name;
        if (IsGameplayScene(activeScene))
        {
            SpawnOrRepositionPlayerForClient(clientId, (int)clientId);
        }

        NotifyPlayerCountToAllClients();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        pendingSpawnClients.Remove(clientId);
        DespawnPlayerForClient(clientId);
        NotifyPlayerCountToAllClients();
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // IMPORTANT: This is Unity's LOCAL sceneLoaded callback. On the server it fires the instant the
        // SERVER finishes loading the scene locally, which happens DURING NGO's networked scene-load
        // sequence -- BEFORE connected clients have finished loading the scene. Spawning player objects
        // here is too early: the SpawnAsPlayerObject messages are generated while remote clients are not
        // yet synchronized for the new scene, so those clients never receive the cars.
        // (Symptom this caused: cars appeared on the host but not on the client.)
        //
        // Player spawning is therefore driven exclusively by NGO's SceneEventType.LoadEventCompleted in
        // OnSceneEvent below, which fires only AFTER every client has finished loading the scene and is
        // ready to receive spawned NetworkObjects. Do NOT re-add spawning to this method.
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // LoadEventCompleted = "all clients have finished loading the scene". This is the only point at
        // which it is safe to spawn player NetworkObjects and have them replicate to every client.
        if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted)
        {
            string loadedScene = string.IsNullOrEmpty(sceneEvent.SceneName) ? SceneManager.GetActiveScene().name : sceneEvent.SceneName;
            HandleSceneLoaded(loadedScene);
        }
    }

    private bool IsGameplayScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return !sceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) &&
               !sceneName.Equals("Ending", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSceneLoaded(string sceneName)
    {
        cachedSpawnPoints = null; // Reset spawn point cache for new scene

        if (!IsGameplayScene(sceneName))
        {
            HideAllCarsOffscreen();
            return;
        }

        // Driven solely by NGO's LoadEventCompleted, which fires exactly once per networked scene load
        // and only after every client has finished loading. No cross-trigger debounce is needed here:
        // SpawnOrRepositionPlayerForClient is idempotent (it repositions an already-spawned car rather
        // than creating a duplicate), so this is safe even if it were ever re-entered for the same scene.
        SpawnAllPlayersInScene();
    }

    public void SpawnAllPlayersInScene()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string activeSceneName = SceneManager.GetActiveScene().name;
        if (!IsGameplayScene(activeSceneName))
        {
            HideAllCarsOffscreen();
            return;
        }

        CacheSceneSpawnPoints();

        var clientIds = NetworkManager.Singleton.ConnectedClientsIds;
        for (int i = 0; i < clientIds.Count; i++)
        {
            SpawnOrRepositionPlayerForClient(clientIds[i], i);
        }

        NotifyPlayerCountToAllClients();

        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.StartCountdownServer();
        }
    }

    private void CacheSceneSpawnPoints()
    {
        cachedSpawnPoints = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
    }

    private GameObject SelectCarPrefabForClient(ulong clientId, int playerIndex)
    {
        bool isHost = (clientId == NetworkManager.ServerClientId || playerIndex == 0);

        if (isHost && hostCarPrefab != null) return hostCarPrefab;
        if (!isHost && clientCarPrefab != null) return clientCarPrefab;

        if (playerCarPrefab != null) return playerCarPrefab;
        if (hostCarPrefab != null) return hostCarPrefab;
        return clientCarPrefab;
    }

    private void SpawnOrRepositionPlayerForClient(ulong clientId, int playerIndex)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        Vector3 spawnPos = GetSpawnPosition(playerIndex, out Quaternion spawnRot);

        // Reposition existing Netcode Player Object
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null && client.PlayerObject.IsSpawned)
        {
            RepositionCar(client.PlayerObject, spawnPos, spawnRot);
            spawnedPlayers[clientId] = client.PlayerObject;
            return;
        }

        // Reposition tracked player object
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject existing) && existing != null && existing.IsSpawned)
        {
            RepositionCar(existing, spawnPos, spawnRot);
            return;
        }

        // Instantiate and spawn official player car object
        GameObject targetPrefab = SelectCarPrefabForClient(clientId, playerIndex);
        if (targetPrefab == null)
        {
            Debug.LogError("[PlayerSpawner ERROR] No Car Prefab assigned in PlayerSpawner Inspector!");
            return;
        }

        GameObject playerObj = Instantiate(targetPrefab, spawnPos, spawnRot);
        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();

        if (netObj != null)
        {
            netObj.SpawnAsPlayerObject(clientId, true);
            spawnedPlayers[clientId] = netObj;
            RepositionCar(netObj, spawnPos, spawnRot);
            Debug.Log($"[PlayerSpawner SUCCESS] Spawned official Player Object for Client {clientId} at Position: {spawnPos}");
        }
    }

    private void RepositionCar(NetworkObject netObj, Vector3 position, Quaternion rotation)
    {
        if (netObj == null) return;

        Transform carTransform = netObj.transform;
        NetworkCarController carCtrl = netObj.GetComponent<NetworkCarController>();
        CarHealth carHealth = netObj.GetComponent<CarHealth>();
        Collider2D col = netObj.GetComponent<Collider2D>();

        if (col != null) col.enabled = true;

        if (carCtrl != null)
        {
            carCtrl.TeleportCarRpc(position, rotation);
            carCtrl.ResetCarBoostStateRpc();
        }
        else
        {
            carTransform.SetPositionAndRotation(position, rotation);
        }

        if (carHealth != null)
        {
            carHealth.ResetHealthAndStateServer();
            carHealth.isInvulnerableDuringSpawn = false;
        }
    }

    private void HideAllCarsOffscreen()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        Vector3 offscreenPos = new Vector3(9999f, 9999f, 0f);

        foreach (var clientKvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (clientKvp.Value.PlayerObject != null && clientKvp.Value.PlayerObject.IsSpawned)
            {
                Transform carTransform = clientKvp.Value.PlayerObject.transform;
                Collider2D col = carTransform.GetComponent<Collider2D>();
                if (col != null) col.enabled = false;

                CarHealth carHealth = carTransform.GetComponent<CarHealth>();
                if (carHealth != null)
                {
                    carHealth.isInvulnerableDuringSpawn = true;
                    carHealth.ResetHealthAndStateServer();
                }

                NetworkCarController carCtrl = carTransform.GetComponent<NetworkCarController>();
                if (carCtrl != null)
                {
                    carCtrl.TeleportCarRpc(offscreenPos, Quaternion.identity);
                    carCtrl.ResetCarBoostStateRpc();
                }
                else
                {
                    carTransform.position = offscreenPos;
                }
            }
        }
    }

    public Vector3 GetSpawnPosition(int playerIndex, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        if (cachedSpawnPoints == null || cachedSpawnPoints.Length == 0)
        {
            CacheSceneSpawnPoints();
        }

        if (cachedSpawnPoints != null && cachedSpawnPoints.Length > 0)
        {
            int index = Mathf.Clamp(playerIndex, 0, cachedSpawnPoints.Length - 1);
            rotation = cachedSpawnPoints[index].transform.rotation;
            return cachedSpawnPoints[index].transform.position;
        }

        return fallbackSpawnPosition + new Vector3(playerIndex * 2f, 0f, 0f);
    }

    private void DespawnPlayerForClient(ulong clientId)
    {
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject netObj) && netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
            spawnedPlayers.Remove(clientId);
        }
    }

    private void NotifyPlayerCountToAllClients()
    {
        if (NetworkManager.Singleton == null) return;
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsServer)
        {
            NetworkRaceManager.Instance.connectedPlayerCount.Value = count;
        }
    }
}
