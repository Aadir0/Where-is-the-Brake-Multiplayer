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

    [Header("Spawn Timing")]
    [SerializeField] private float spawnDelayInGameplayScene = 3.0f;

    [Header("Spawn Settings")]
    [SerializeField] private Vector3 fallbackSpawnPosition = Vector3.zero;

    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();
    private bool isSpawningInProgress = false;

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
            Debug.LogError("[PlayerSpawner ERROR] No Car Prefabs assigned in PlayerSpawner Inspector! Drag Player.prefab into PlayerSpawner.");
        }
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
            }
        }
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;

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
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }

        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            HideAllCarsOffscreen();
        }

        NotifyPlayerCountToAllClients();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            HideAllCarsOffscreen();
        }
        else if (!isSpawningInProgress)
        {
            SpawnOrRepositionPlayerForClient(clientId, (int)clientId);
        }

        NotifyPlayerCountToAllClients();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        DespawnPlayerForClient(clientId);
        NotifyPlayerCountToAllClients();
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[PlayerSpawner Unity SceneLoaded] Scene '{scene.name}' loaded.");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        if (scene.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            HideAllCarsOffscreen();
        }
        else
        {
            HideAllCarsOffscreen();
            StartCoroutine(DelayedSpawnRoutine(scene.name, spawnDelayInGameplayScene));
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        Debug.Log($"[PlayerSpawner NGO SceneEvent] {sceneEvent.SceneEventType} in scene '{sceneEvent.SceneName}'");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        if (sceneEvent.SceneEventType == SceneEventType.Load)
        {
            HideAllCarsOffscreen();
        }
        else if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted)
        {
            string loadedScene = sceneEvent.SceneName;
            if (string.IsNullOrEmpty(loadedScene))
            {
                loadedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }

            if (!loadedScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                StartCoroutine(DelayedSpawnRoutine(loadedScene, spawnDelayInGameplayScene));
            }
        }
    }

    private void HideAllCarsOffscreen()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        foreach (var clientKvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (clientKvp.Value.PlayerObject != null && clientKvp.Value.PlayerObject.IsSpawned)
            {
                Transform carTransform = clientKvp.Value.PlayerObject.transform;
                carTransform.position = new Vector3(9999f, 9999f, 0f);

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
                    carCtrl.ResetCarBoostState();
                }
            }
        }
    }

    private IEnumerator DelayedSpawnRoutine(string sceneName, float delaySeconds)
    {
        if (isSpawningInProgress) yield break;
        isSpawningInProgress = true;

        Debug.Log($"[PlayerSpawner] Scene '{sceneName}' loaded! Waiting {delaySeconds} seconds before positioning cars at SpawnPoints...");
        yield return new WaitForSeconds(delaySeconds);

        Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        }

        SpawnAllPlayersInScene();
        isSpawningInProgress = false;

        // Trigger race countdown timer ONLY after the load delay finishes
        if (NetworkRaceManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[PlayerSpawner] Load delay finished. Starting race countdown now...");
            NetworkRaceManager.Instance.StartCountdownServer();
        }
    }

    public void SpawnAllPlayersInScene()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeSceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[PlayerSpawner] In MainMenu - NO cars will be spawned.");
            HideAllCarsOffscreen();
            return;
        }

        var clientIds = NetworkManager.Singleton.ConnectedClientsIds;
        Debug.Log($"[PlayerSpawner SUCCESS] Positioning player cars in '{activeSceneName}' for {clientIds.Count} connected client(s).");

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];
            SpawnOrRepositionPlayerForClient(clientId, i);
        }

        NotifyPlayerCountToAllClients();
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

        // 1. Reposition existing NGO Player Object for clientId
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null && client.PlayerObject.IsSpawned)
        {
            Transform carTransform = client.PlayerObject.transform;
            carTransform.position = spawnPos;
            carTransform.rotation = spawnRot;

            Rigidbody2D rb = carTransform.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            NetworkCarController carCtrl = carTransform.GetComponent<NetworkCarController>();
            if (carCtrl != null)
            {
                carCtrl.ResetCarBoostState();
            }

            CarHealth carHealth = carTransform.GetComponent<CarHealth>();
            if (carHealth != null)
            {
                carHealth.ResetHealthAndStateServer();
                carHealth.isInvulnerableDuringSpawn = false;
            }

            spawnedPlayers[clientId] = client.PlayerObject;
            Debug.Log($"[PlayerSpawner SUCCESS] Teleported existing Player Object for Client {clientId} to SpawnPoint Position: {spawnPos}");
            return;
        }

        // 2. Reposition tracked player object
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject existing) && existing != null && existing.IsSpawned)
        {
            existing.transform.position = spawnPos;
            existing.transform.rotation = spawnRot;

            Rigidbody2D rb = existing.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            NetworkCarController carCtrl = existing.GetComponent<NetworkCarController>();
            if (carCtrl != null)
            {
                carCtrl.ResetCarBoostState();
            }

            CarHealth carHealth = existing.GetComponent<CarHealth>();
            if (carHealth != null)
            {
                carHealth.ResetHealthAndStateServer();
                carHealth.isInvulnerableDuringSpawn = false;
            }

            Debug.Log($"[PlayerSpawner SUCCESS] Teleported tracked Player Object for Client {clientId} to SpawnPoint Position: {spawnPos}");
            return;
        }

        // 3. Select distinct Host/Client Prefab & Spawn As Official Player Object
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

            CarHealth carHealth = playerObj.GetComponent<CarHealth>();
            if (carHealth != null)
            {
                carHealth.ResetHealthAndStateServer();
                carHealth.isInvulnerableDuringSpawn = false;
            }

            spawnedPlayers[clientId] = netObj;
            Debug.Log($"[PlayerSpawner SUCCESS] Spawned Official Player Car ({targetPrefab.name}) for Client {clientId} (IsOwner: true) at Position: {spawnPos}");
        }
    }

    private void DespawnPlayerForClient(ulong clientId)
    {
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject netObj))
        {
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            spawnedPlayers.Remove(clientId);
        }
    }

    public Vector3 GetSpawnPosition(int index, out Quaternion rotation)
    {
        if (SpawnPointConfig.Instance != null)
        {
            return SpawnPointConfig.Instance.GetSpawnPosition(index, out rotation);
        }

        rotation = Quaternion.identity;

        SpawnPoint[] foundComponents = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (foundComponents != null && foundComponents.Length > 0)
        {
            int pointIndex = Mathf.Abs(index) % foundComponents.Length;
            rotation = foundComponents[pointIndex].transform.rotation;
            Vector3 pos = foundComponents[pointIndex].transform.position;
            return pos;
        }

        GameObject[] foundSpawns = GameObject.FindGameObjectsWithTag("SpawnPoint");
        if (foundSpawns != null && foundSpawns.Length > 0)
        {
            int pointIndex = Mathf.Abs(index) % foundSpawns.Length;
            rotation = foundSpawns[pointIndex].transform.rotation;
            Vector3 pos = foundSpawns[pointIndex].transform.position;
            return pos;
        }

        Vector3 fallback = fallbackSpawnPosition + new Vector3(index * 3f, 0f, 0f);
        return fallback;
    }

    public void NotifyPlayerCountToAllClients()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;

        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.UpdatePlayerCountDisplay(count);
        }
    }
}
