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
    public NetworkVariable<int> playAgainReadyCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action<RaceState> OnRaceStateChanged;
    public event Action<int> OnCountdownTick;
    public event Action<int> OnPlayerCountChanged;
    public event Action<int, int> OnReadyPlayerCountChanged;
    public event Action<int, int> OnPlayAgainCountChanged;

    private readonly HashSet<ulong> readyPlayersSet = new HashSet<ulong>();
    private readonly HashSet<ulong> playAgainPlayersSet = new HashSet<ulong>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void OnNetworkSpawn()
    {
        currentRaceState.OnValueChanged += HandleRaceStateChanged;
        countdownTimer.OnValueChanged += HandleCountdownTimerChanged;
        connectedPlayerCount.OnValueChanged += HandleConnectedPlayerCountChanged;
        readyPlayerCount.OnValueChanged += HandleReadyPlayerCountChanged;
        playAgainReadyCount.OnValueChanged += HandlePlayAgainReadyCountChanged;

        if (IsServer)
        {
            connectedPlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
            readyPlayerCount.Value = 0;
            playAgainReadyCount.Value = 0;
            readyPlayersSet.Clear();
            playAgainPlayersSet.Clear();
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnNetworkDespawn()
    {
        currentRaceState.OnValueChanged -= HandleRaceStateChanged;
        countdownTimer.OnValueChanged -= HandleCountdownTimerChanged;
        connectedPlayerCount.OnValueChanged -= HandleConnectedPlayerCountChanged;
        readyPlayerCount.OnValueChanged -= HandleReadyPlayerCountChanged;
        playAgainReadyCount.OnValueChanged -= HandlePlayAgainReadyCountChanged;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsServer)
        {
            readyPlayersSet.Clear();
            playAgainPlayersSet.Clear();
            readyPlayerCount.Value = 0;
            playAgainReadyCount.Value = 0;
            currentRaceState.Value = RaceState.LobbyWaiting;
            winnerClientId.Value = 9999;
        }
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

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyMatchEndedRpc(ulong winnerClientId = 9999)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == winnerClientId)
        {
            return;
        }

        if (FinishLine.LocalPlayerHasWon || (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.hasWonPlayer))
        {
            return;
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (!currentScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
            !currentScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (LevelTimer.Instance != null)
            {
                LevelTimer.Instance.TriggerTimeOverFromMatchEnd();
            }
            else if (LeaderboardManager.Instance != null)
            {
                LeaderboardManager.Instance.EnsureAllLevelsRecorded();
            }
        }
    }

    public void NotifyPlayerReachedEndingServer(ulong clientId)
    {
        NotifyMatchEndedRpc(clientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlayerReachedEndingServerRpc(ulong clientId)
    {
        NotifyMatchEndedRpc(clientId);
    }

    [ClientRpc]
    public void NotifyMatchEndedTimeOverClientRpc(ulong winnerClientId)
    {
        NotifyMatchEndedRpc(winnerClientId);
    }

    [ClientRpc]
    public void ForceAllPlayersToEndingClientRpc()
    {
        NotifyMatchEndedRpc(9999);
    }

    private void HandlePlayAgainReadyCountChanged(int previousVal, int newVal)
    {
        int total = connectedPlayerCount.Value;
        OnPlayAgainCountChanged?.Invoke(newVal, total);
    }

    [ClientRpc]
    public void ShowTransitionCoverClientRpc()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.ShowTransitionCover();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlayAgainServerRpc(ulong clientId)
    {
        if (!IsServer) return;

        if (!playAgainPlayersSet.Contains(clientId))
        {
            playAgainPlayersSet.Add(clientId);
            playAgainReadyCount.Value = playAgainPlayersSet.Count;
            int totalRequired = NetworkManager.Singleton.ConnectedClientsIds.Count;

            if (playAgainPlayersSet.Count >= totalRequired)
            {
                playAgainPlayersSet.Clear();
                playAgainReadyCount.Value = 0;

                ShowTransitionCoverClientRpc();
                StartCoroutine(DelayedPlayAgainLoadRoutine(0.4f));
            }
        }
    }

    private IEnumerator DelayedPlayAgainLoadRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.ResetRun();
        }

        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.ResetRunTimer();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null && NetworkManager.Singleton.IsListening)
        {
            var status = NetworkManager.Singleton.SceneManager.LoadScene("Level 1", LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                SceneManager.LoadScene("Level 1");
            }
        }
        else
        {
            SceneManager.LoadScene("Level 1");
        }
    }

    public void LoadNextLevelServer()
    {
        if (!IsServer) return;

        ShowTransitionCoverClientRpc();

        string currentSceneName = SceneManager.GetActiveScene().name;
        string nextSceneName = GetNextSceneName(currentSceneName);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null && NetworkManager.Singleton.IsListening)
        {
            var status = NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    private string GetNextSceneName(string currentSceneName)
    {
        if (currentSceneName.Equals("Level 1", StringComparison.OrdinalIgnoreCase)) return "Level 2";
        if (currentSceneName.Equals("Level 2", StringComparison.OrdinalIgnoreCase)) return "Level 3";
        if (currentSceneName.Equals("Level 3", StringComparison.OrdinalIgnoreCase)) return "Level 4";
        if (currentSceneName.Equals("Level 4", StringComparison.OrdinalIgnoreCase)) return "Ending";

        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(nextSceneIndex);
            return System.IO.Path.GetFileNameWithoutExtension(scenePath);
        }
        return "Ending";
    }
}
