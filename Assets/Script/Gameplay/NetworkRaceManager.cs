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
    public event Action<int> OnPlayerCountChanged;
    public event Action<int, int> OnReadyPlayerCountChanged;

    private readonly HashSet<ulong> readyPlayersSet = new HashSet<ulong>();
    private readonly Dictionary<ulong, int> playerCurrentLevelIndex = new Dictionary<ulong, int>();
    private readonly HashSet<ulong> playersReachedEnding = new HashSet<ulong>();
    private readonly Dictionary<ulong, string> playerCurrentScene = new Dictionary<ulong, string>();

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

        if (IsServer)
        {
            connectedPlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
            readyPlayerCount.Value = 0;
            readyPlayersSet.Clear();
            playerCurrentLevelIndex.Clear();
            playerCurrentScene.Clear();
            playersReachedEnding.Clear();
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                playerCurrentLevelIndex[clientId] = GetSceneBuildIndex(SceneManager.GetActiveScene().name);
                playerCurrentScene[clientId] = SceneManager.GetActiveScene().name;
            }
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnNetworkDespawn()
    {
        currentRaceState.OnValueChanged -= HandleRaceStateChanged;
        countdownTimer.OnValueChanged -= HandleCountdownTimerChanged;
        connectedPlayerCount.OnValueChanged -= HandleConnectedPlayerCountChanged;
        readyPlayerCount.OnValueChanged -= HandleReadyPlayerCountChanged;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsServer)
        {
            readyPlayersSet.Clear();
            readyPlayerCount.Value = 0;
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
    public void NotifyPlayerFinishedLevelRpc(ulong clientId, string finishedSceneName)
    {
        if (!IsServer) return;

        string nextSceneName = GetNextSceneName(finishedSceneName);
        if (string.IsNullOrEmpty(nextSceneName)) return;

        int nextIndex = GetSceneBuildIndex(nextSceneName);
        playerCurrentLevelIndex[clientId] = nextIndex;
        playerCurrentScene[clientId] = nextSceneName;

        bool isEndingScene = string.Equals(nextSceneName, "Ending", StringComparison.OrdinalIgnoreCase);
        if (isEndingScene)
        {
            if (!playersReachedEnding.Contains(clientId))
            {
                playersReachedEnding.Add(clientId);
                NotifyOtherPlayersTimeOverClientRpc(clientId);
            }
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyPlayerReachedEndingRpc(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == clientId)
        {
            return;
        }

        if (playersReachedEnding.Contains(clientId)) return;
        playersReachedEnding.Add(clientId);

        TriggerTimeOverForNonWinners();
    }

    [ClientRpc]
    public void NotifyOtherPlayersTimeOverClientRpc(ulong winnerClientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == winnerClientId)
        {
            return;
        }

        TriggerTimeOverForNonWinners();
    }

    private void TriggerTimeOverForNonWinners()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (!currentScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
            !currentScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (LevelTimer.Instance != null)
            {
                LevelTimer.Instance.TriggerTimeOverFromMatchEnd();
            }
            else
            {
                StartCoroutine(FallbackTimeOverEndingRoutine());
            }
        }
    }

    private IEnumerator FallbackTimeOverEndingRoutine()
    {
        // Try finding any TimeOver UI panel in scene
        GameObject timeOverPanel = GameObject.FindGameObjectWithTag("TimeOver");
        if (timeOverPanel == null)
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (!canvas.gameObject.scene.isLoaded) continue;
                Transform[] children = canvas.GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    string n = child.name.ToLower();
                    if (child.CompareTag("TimeOver") || n.Contains("timeover") || n.Contains("time over") || n.Contains("timeout"))
                    {
                        timeOverPanel = child.gameObject;
                        break;
                    }
                }
                if (timeOverPanel != null) break;
            }
        }

        if (timeOverPanel != null)
        {
            timeOverPanel.SetActive(true);
            timeOverPanel.transform.SetAsLastSibling();
        }

        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.EnsureAllLevelsRecorded();
        }

        yield return new WaitForSeconds(2.0f);

        string activeScene = SceneManager.GetActiveScene().name;
        if (!activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
            !activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.LoadSceneWithTransition("Ending");
            }
            else
            {
                SceneManager.LoadScene("Ending");
            }
        }
    }

    [ClientRpc]
    public void ForcePlayerToEndingClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.LocalClientId == clientId)
        {
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.LoadSceneWithTransition("Ending");
            }
            else
            {
                SceneManager.LoadScene("Ending");
            }
        }
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
    public void RequestLoadNextLevelServerRpc()
    {
        if (IsServer)
        {
            LoadNextLevelServer();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestAdvancePlayerLevelServerRpc(ulong clientId)
    {
        if (!IsServer) return;
        AdvancePlayerToNextLevel(clientId);
    }

    public void AdvancePlayerToNextLevel(ulong clientId)
    {
        if (!IsServer) return;

        string currentScene = playerCurrentScene.TryGetValue(clientId, out var s) ? s : SceneManager.GetActiveScene().name;
        string nextSceneName = GetNextSceneName(currentScene);
        if (string.IsNullOrEmpty(nextSceneName)) return;

        int nextIndex = GetSceneBuildIndex(nextSceneName);
        playerCurrentLevelIndex[clientId] = nextIndex;
        playerCurrentScene[clientId] = nextSceneName;

        bool isEndingScene = string.Equals(nextSceneName, "Ending", StringComparison.OrdinalIgnoreCase);
        if (isEndingScene)
        {
            if (!playersReachedEnding.Contains(clientId))
            {
                playersReachedEnding.Add(clientId);
                NotifyOtherPlayersTimeOverClientRpc(clientId);
            }
        }
    }

    public void LoadNextLevelServer()
    {
        if (!IsServer) return;

        ShowTransitionCoverClientRpc();

        string currentSceneName = SceneManager.GetActiveScene().name;
        string nextSceneName = GetNextSceneName(currentSceneName);

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadSceneWithTransition(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    public void NotifyPlayerSceneChanged(ulong clientId, string sceneName)
    {
        if (!IsServer) return;
        playerCurrentScene[clientId] = sceneName;
    }

    private int GetPlayerCurrentLevelIndex(ulong clientId)
    {
        if (playerCurrentLevelIndex.TryGetValue(clientId, out int index))
        {
            return index;
        }
        return GetSceneBuildIndex(SceneManager.GetActiveScene().name);
    }

    private int GetSceneBuildIndex(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    private int GetEndingSceneIndex()
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, "Ending", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private string GetSceneNameByBuildIndex(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings) return null;
        string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public static string GetNextSceneName(string currentSceneName)
    {
        if (string.IsNullOrEmpty(currentSceneName)) return "Level 1";

        string cleanName = currentSceneName.Trim();

        // 1. Explicit canonical level progression
        if (string.Equals(cleanName, "Level 1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanName, "Level1", StringComparison.OrdinalIgnoreCase))
        {
            return "Level 2";
        }
        if (string.Equals(cleanName, "Level 2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanName, "Level2", StringComparison.OrdinalIgnoreCase))
        {
            return "Level 3";
        }
        if (string.Equals(cleanName, "Level 3", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanName, "Level3", StringComparison.OrdinalIgnoreCase))
        {
            return "Level 4";
        }
        if (string.Equals(cleanName, "Level 4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanName, "Level4", StringComparison.OrdinalIgnoreCase))
        {
            return "Level 5";
        }
        if (string.Equals(cleanName, "Level 5", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanName, "Level5", StringComparison.OrdinalIgnoreCase))
        {
            return "Ending";
        }

        // 2. Generic numbered "Level X" progression
        if (cleanName.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
        {
            string numStr = cleanName.Substring(5).Trim();
            if (int.TryParse(numStr, out int lvlNum))
            {
                if (lvlNum >= 1 && lvlNum < 5)
                {
                    return "Level " + (lvlNum + 1);
                }
                return "Ending";
            }
        }

        // 3. Fallback from Build Settings index
        int totalScenes = SceneManager.sceneCountInBuildSettings;
        if (totalScenes > 0)
        {
            int currentIndex = -1;
            for (int i = 0; i < totalScenes; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(name, cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                for (int i = currentIndex + 1; i < totalScenes; i++)
                {
                    string path = SceneUtility.GetScenePathByBuildIndex(i);
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrEmpty(name) && !name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
        }

        return "Ending";
    }

    private static int GetBuildIndexByName(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    private static string GetFirstLevelSceneName()
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name) && !name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && !name.Equals("Ending", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return "Level 1";
    }
}