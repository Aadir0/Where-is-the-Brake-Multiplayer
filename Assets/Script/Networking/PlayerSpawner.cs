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
    private readonly List<ulong> pendingSpawnClients = new List<ulong>();
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
            Debug.LogError("[PlayerSpawner ERROR] No Car Prefabs assigned in PlayerSpawner Inspector!");
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
        else
        {
            if (!pendingSpawnClients.Contains(clientId))
            {
                pendingSpawnClients.Add(clientId);
            }
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

    private IEnumerator DelayedSpawnRoutine(string sceneName, float delaySeconds)
    {
        if (isSpawningInProgress) yield break;
        isSpawningInProgress = true;

        yield return new WaitForSeconds(delaySeconds);

        Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        }

        SpawnAllPlayersInScene();

        foreach (ulong clientId in pendingSpawnClients)
        {
            SpawnOrRepositionPlayerForClient(clientId, (int)clientId);
        }
        pendingSpawnClients.Clear();

        isSpawningInProgress = false;

        if (NetworkRaceManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkRaceManager.Instance.StartCountdownServer();
        }
    }

    public void SpawnAllPlayersInScene()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeSceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            HideAllCarsOffscreen();
            return;
        }

        var clientIds = NetworkManager.Singleton.ConnectedClientsIds;
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

        // 1. Reposition existing NGO Player Object for clientId via TeleportCarRpc
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null && client.PlayerObject.IsSpawned)
        {
            Transform carTransform = client.PlayerObject.transform;

            NetworkCarController carCtrl = carTransform.GetComponent<NetworkCarController>();
            if (carCtrl != null)
            {
                carCtrl.TeleportCarRpc(spawnPos, spawnRot);
                carCtrl.ResetCarBoostStateRpc();
            }
            else
            {
                carTransform.position = spawnPos;
                carTransform.rotation = spawnRot;
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
            NetworkCarController carCtrl = existing.GetComponent<NetworkCarController>();
            if (carCtrl != null)
            {
                carCtrl.TeleportCarRpc(spawnPos, spawnRot);
                carCtrl.ResetCarBoostStateRpc();
            }
            else
            {
                existing.transform.position = spawnPos;
                existing.transform.rotation = spawnRot;
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
            spawnedPlayers[clientId] = netObj;

            NetworkCarController carCtrl = playerObj.GetComponent<NetworkCarController>();
            if (carCtrl != null)
            {
                carCtrl.TeleportCarRpc(spawnPos, spawnRot);
                carCtrl.ResetCarBoostStateRpc();
            }

            Debug.Log($"[PlayerSpawner SUCCESS] Spawned official Player Object for Client {clientId} at Position: {spawnPos}");
        }
    }

    public Vector3 GetSpawnPosition(int playerIndex, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        SpawnPoint[] spawnPoints = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = Mathf.Clamp(playerIndex, 0, spawnPoints.Length - 1);
            rotation = spawnPoints[index].transform.rotation;
            return spawnPoints[index].transform.position;
        }

        GameObject[] spawnObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
        if (spawnObjects != null && spawnObjects.Length > 0)
        {
            int index = Mathf.Clamp(playerIndex, 0, spawnObjects.Length - 1);
            rotation = spawnObjects[index].transform.rotation;
            return spawnObjects[index].transform.position;
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
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsServer)
        {
            NetworkRaceManager.Instance.connectedPlayerCount.Value = count;
        }
    }
}
