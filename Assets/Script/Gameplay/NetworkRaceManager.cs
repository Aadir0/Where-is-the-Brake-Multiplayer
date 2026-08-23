using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkRaceManager : NetworkBehaviour
{
    public static NetworkRaceManager Instance { get; private set; }

    public enum RaceState
    {
        LobbyWaiting,
        Countdown,
        Racing,
        Finished
    }

    [Header("Race Config")]
    [SerializeField] private float countdownDuration = 3f;

    public NetworkVariable<RaceState> currentRaceState = new NetworkVariable<RaceState>(RaceState.LobbyWaiting);
    public NetworkVariable<float> countdownTimer = new NetworkVariable<float>(3f);
    public NetworkVariable<ulong> winnerClientId = new NetworkVariable<ulong>(9999);
    public NetworkVariable<int> connectedPlayerCount = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> readyPlayerCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action<RaceState> OnRaceStateChanged;
    public event Action<int> OnCountdownTick;
    public event Action<ulong> OnPlayerFinished;
    public event Action<int> OnPlayerCountChanged;
    public event Action<int, int> OnReadyPlayerCountChanged;

    private readonly List<ulong> finishedPlayers = new List<ulong>();
    private readonly HashSet<ulong> readyPlayersSet = new HashSet<ulong>();

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
        currentRaceState.OnValueChanged += HandleStateChanged;
        countdownTimer.OnValueChanged += HandleCountdownChanged;
        connectedPlayerCount.OnValueChanged += HandlePlayerCountChanged;
        readyPlayerCount.OnValueChanged += HandleReadyPlayerCountChanged;

        if (IsServer)
        {
            currentRaceState.Value = RaceState.LobbyWaiting;
            readyPlayersSet.Clear();
            readyPlayerCount.Value = 0;
            UpdatePlayerCountServer();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedServer;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedServer;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentRaceState.OnValueChanged -= HandleStateChanged;
        countdownTimer.OnValueChanged -= HandleCountdownChanged;
        connectedPlayerCount.OnValueChanged -= HandlePlayerCountChanged;
        readyPlayerCount.OnValueChanged -= HandleReadyPlayerCountChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedServer;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedServer;
        }
    }

    private void OnClientConnectedServer(ulong clientId)
    {
        UpdatePlayerCountServer();
    }

    private void OnClientDisconnectedServer(ulong clientId)
    {
        readyPlayersSet.Remove(clientId);
        readyPlayerCount.Value = readyPlayersSet.Count;
        UpdatePlayerCountServer();
    }

    private void UpdatePlayerCountServer()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        connectedPlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
    }

    private void HandleStateChanged(RaceState previousState, RaceState newState)
    {
        OnRaceStateChanged?.Invoke(newState);
    }

    private void HandleCountdownChanged(float previousVal, float newVal)
    {
        int second = Mathf.CeilToInt(newVal);
        OnCountdownTick?.Invoke(second);
    }

    private void HandlePlayerCountChanged(int previousVal, int newVal)
    {
        OnPlayerCountChanged?.Invoke(newVal);
        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.UpdatePlayerCountDisplay(newVal);
        }
    }

    private void HandleReadyPlayerCountChanged(int previousVal, int newVal)
    {
        int total = connectedPlayerCount.Value;
        OnReadyPlayerCountChanged?.Invoke(newVal, total);
    }

    public void StartCountdownServer()
    {
        if (!IsServer) return;
        if (currentRaceState.Value != RaceState.LobbyWaiting) return;

        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        currentRaceState.Value = RaceState.Countdown;
        float remaining = countdownDuration;

        while (remaining > 0f)
        {
            countdownTimer.Value = remaining;
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        countdownTimer.Value = 0f;
        currentRaceState.Value = RaceState.Racing;

        // Start 5-minute level timer ONLY when countdown reaches 0 (GO!)
        if (LevelTimer.Instance != null && IsServer)
        {
            LevelTimer.Instance.StartTimerServer();
        }
    }

    public void PlayerCrossedFinishServer(ulong clientId)
    {
        if (!IsServer) return;
        if (finishedPlayers.Contains(clientId)) return;

        finishedPlayers.Add(clientId);

        if (finishedPlayers.Count == 1)
        {
            winnerClientId.Value = clientId;
            currentRaceState.Value = RaceState.Finished;
        }

        NotifyPlayerFinishedClientRpc(clientId);
    }

    [ClientRpc]
    private void NotifyPlayerFinishedClientRpc(ulong clientId)
    {
        OnPlayerFinished?.Invoke(clientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSetReadyServerRpc(ulong clientId)
    {
        if (!IsServer) return;

        if (!readyPlayersSet.Contains(clientId))
        {
            readyPlayersSet.Add(clientId);
            readyPlayerCount.Value = readyPlayersSet.Count;
            int totalRequired = NetworkManager.Singleton.ConnectedClientsIds.Count;

            Debug.Log($"[NetworkRaceManager] Client {clientId} marked READY. Total Ready: {readyPlayersSet.Count}/{totalRequired}");

            if (readyPlayersSet.Count >= totalRequired)
            {
                Debug.Log("[NetworkRaceManager] ALL PLAYERS READY! Loading next scene now...");
                readyPlayersSet.Clear();
                readyPlayerCount.Value = 0;
                LoadNextLevelServer();
            }
        }
    }

    public void LoadNextLevelServer()
    {
        if (!IsServer) return;

        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            string sceneName = SceneUtility.GetScenePathByBuildIndex(nextSceneIndex);
            sceneName = System.IO.Path.GetFileNameWithoutExtension(sceneName);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(nextSceneIndex);
            }
        }
        else
        {
            Debug.LogWarning("No next scene available in Build Settings.");
        }
    }
}
