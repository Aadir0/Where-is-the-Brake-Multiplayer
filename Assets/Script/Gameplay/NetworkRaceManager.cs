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
    public event Action OnBothPlayersFinished;
    public event Action<ulong> OnPlayerTimeIsLess;

    private readonly List<ulong> finishedPlayers = new List<ulong>();
    private readonly HashSet<ulong> readyPlayersSet = new HashSet<ulong>();

    [Header("Finish Window Timer")]
    [SerializeField] private float finishWindowDuration = 40.0f;
    private Coroutine finishWindowCoroutine;

    public NetworkVariable<float> finishWindowTimer = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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
        currentRaceState.OnValueChanged += HandleRaceStateChanged;
        countdownTimer.OnValueChanged += HandleCountdownTimerChanged;
        connectedPlayerCount.OnValueChanged += HandleConnectedPlayerCountChanged;
        readyPlayerCount.OnValueChanged += HandleReadyPlayerCountChanged;

        if (IsServer)
        {
            connectedPlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
            readyPlayerCount.Value = 0;
            readyPlayersSet.Clear();
            finishedPlayers.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        currentRaceState.OnValueChanged -= HandleRaceStateChanged;
        countdownTimer.OnValueChanged -= HandleCountdownTimerChanged;
        connectedPlayerCount.OnValueChanged -= HandleConnectedPlayerCountChanged;
        readyPlayerCount.OnValueChanged -= HandleReadyPlayerCountChanged;
    }

    private void HandleRaceStateChanged(RaceState previousVal, RaceState newVal)
    {
        OnRaceStateChanged?.Invoke(newVal);
    }

    private void HandleCountdownTimerChanged(float previousVal, float newVal)
    {
        OnCountdownTick?.Invoke(Mathf.CeilToInt(newVal));
    }

    private void HandleConnectedPlayerCountChanged(int previousVal, int newVal)
    {
        OnPlayerCountChanged?.Invoke(newVal);
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

            if (finishWindowCoroutine != null) StopCoroutine(finishWindowCoroutine);
            finishWindowCoroutine = StartCoroutine(FinishWindowRoutine());
        }

        NotifyPlayerFinishedClientRpc(clientId);

        // If ALL connected players crossed the finish line before window expired:
        if (NetworkManager.Singleton != null && finishedPlayers.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
        {
            if (finishWindowCoroutine != null) StopCoroutine(finishWindowCoroutine);
            NotifyBothFinishedClientRpc();
            StartCoroutine(DelayedTransitionNextLevelRoutine(3.0f));
        }
    }

    private IEnumerator FinishWindowRoutine()
    {
        float remaining = finishWindowDuration;
        while (remaining > 0f)
        {
            finishWindowTimer.Value = remaining;
            yield return new WaitForSeconds(1.0f);
            remaining -= 1.0f;
        }

        finishWindowTimer.Value = 0f;

        // Window expired! For any connected player who didn't finish, notify TimeIsLess and load next level
        if (NetworkManager.Singleton != null)
        {
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (!finishedPlayers.Contains(id))
                {
                    NotifyTimeIsLessClientRpc(id);
                }
            }
        }

        StartCoroutine(DelayedTransitionNextLevelRoutine(2.5f));
    }

    [ClientRpc]
    private void NotifyPlayerFinishedClientRpc(ulong clientId)
    {
        OnPlayerFinished?.Invoke(clientId);
    }

    [ClientRpc]
    private void NotifyBothFinishedClientRpc()
    {
        OnBothPlayersFinished?.Invoke();
    }

    [ClientRpc]
    private void NotifyTimeIsLessClientRpc(ulong clientId)
    {
        OnPlayerTimeIsLess?.Invoke(clientId);
    }

    private IEnumerator DelayedTransitionNextLevelRoutine(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        LoadNextLevelServer();
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

            if (readyPlayersSet.Count >= totalRequired)
            {
                if (finishWindowCoroutine != null) StopCoroutine(finishWindowCoroutine);
                readyPlayersSet.Clear();
                readyPlayerCount.Value = 0;
                LoadNextLevelServer();
            }
        }
    }

    public void LoadNextLevelServer()
    {
        if (!IsServer) return;

        if (finishWindowCoroutine != null) StopCoroutine(finishWindowCoroutine);

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
    }
}
