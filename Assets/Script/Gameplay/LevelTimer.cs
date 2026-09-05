using System;
using System.Collections;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelTimer : NetworkBehaviour
{
    public static LevelTimer Instance { get; private set; }

    [Header("Run Timer Config")]
    [SerializeField] private float overallRunDurationSeconds = 300f; // 5 minutes total for all levels combined

    [Header("UI References (Optional Inspector Assignment)")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject timeOverPanel;
    [SerializeField] private Button mainMenuButton;

    public NetworkVariable<float> timeRemaining = new NetworkVariable<float>(
        300f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> isTimeOver = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool timerRunning = false;
    private float localRunStartTime = 0f;
    private float currentLevelStartTime = 0f;
    private bool isLocalPlayerFinishedLevel = false;
    private float frozenLocalLevelTime = 0f;
    private float offlineTimeRemaining = 300f;
    private bool offlineIsTimeOver = false;
    private bool hasInitializedRun = false;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool IsServerOrOffline => !IsNetworkActive || (IsNetworkActive && IsServer);
    public bool IsTimeOver => IsNetworkActive ? isTimeOver.Value : offlineIsTimeOver;

    private void Awake()
    {
        // LevelTimer lives in-scene only in Level 1 and is DontDestroyOnLoad so the cumulative run
        // time carries across levels. On a replay (return to menu -> start again) a stale timer from
        // the previous session can survive in DontDestroyOnLoad. The freshly loaded Level 1 instance
        // is the one that gets network-spawned this session, so it must take over. Destroying the NEW
        // instance instead would leave a dead, un-networked timer as Instance and break timer sync.
        if (Instance != null && Instance != this)
        {
            Destroy(Instance.gameObject);
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        localRunStartTime = Time.time;
        currentLevelStartTime = Time.time;
        isLocalPlayerFinishedLevel = false;
        frozenLocalLevelTime = 0f;
        offlineTimeRemaining = overallRunDurationSeconds;
        offlineIsTimeOver = false;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        FindOrSetupUIReferences();
        CheckSceneTimerState(SceneManager.GetActiveScene().name);
    }

    public void ResetRunTimer()
    {
        localRunStartTime = Time.time;
        currentLevelStartTime = Time.time;
        isLocalPlayerFinishedLevel = false;
        frozenLocalLevelTime = 0f;
        offlineTimeRemaining = overallRunDurationSeconds;
        offlineIsTimeOver = false;
        hasInitializedRun = true;
        timerRunning = true;

        if (IsNetworkActive && IsServer)
        {
            timeRemaining.Value = overallRunDurationSeconds;
            isTimeOver.Value = false;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentLevelStartTime = Time.time;
        isLocalPlayerFinishedLevel = false;
        frozenLocalLevelTime = 0f;

        FindOrSetupUIReferences();
        CheckSceneTimerState(scene.name);
    }

    public override void OnNetworkSpawn()
    {
        timeRemaining.OnValueChanged += OnTimeRemainingChanged;
        isTimeOver.OnValueChanged += OnTimeOverChanged;

        FindOrSetupUIReferences();

        if (IsServer && !hasInitializedRun)
        {
            ResetRunTimer();
        }

        UpdateTimerDisplay(GetRemainingTime());
    }

    public override void OnNetworkDespawn()
    {
        timeRemaining.OnValueChanged -= OnTimeRemainingChanged;
        isTimeOver.OnValueChanged -= OnTimeOverChanged;
    }

    private void CheckSceneTimerState(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) sceneName = SceneManager.GetActiveScene().name;

        if (sceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            timerRunning = false;
            hasInitializedRun = false;
            isLocalPlayerFinishedLevel = false;
            offlineTimeRemaining = overallRunDurationSeconds;
            offlineIsTimeOver = false;
        }
        else if (sceneName.Equals("Ending", StringComparison.OrdinalIgnoreCase))
        {
            timerRunning = false;
            hasInitializedRun = false;
        }
        else if (sceneName.Equals("Level 1", StringComparison.OrdinalIgnoreCase))
        {
            ResetRunTimer();
            if (LeaderboardManager.Instance != null)
            {
                LeaderboardManager.Instance.ResetRun();
            }
        }
        else
        {
            timerRunning = true;
            currentLevelStartTime = Time.time;
            isLocalPlayerFinishedLevel = false;
        }
    }

    public float GetRemainingTime()
    {
        if (IsNetworkActive)
        {
            return timeRemaining.Value;
        }
        return offlineTimeRemaining;
    }

    public float GetCurrentLevelElapsedTime()
    {
        if (isLocalPlayerFinishedLevel)
        {
            return Mathf.Max(0.1f, frozenLocalLevelTime);
        }
        return Mathf.Max(0.1f, Time.time - currentLevelStartTime);
    }

    public void StopLocalTimerForPlayer(float finalLevelTime = -1f)
    {
        isLocalPlayerFinishedLevel = true;
        frozenLocalLevelTime = (finalLevelTime > 0f) ? finalLevelTime : (Time.time - currentLevelStartTime);
    }

    public float GetElapsedTime()
    {
        return GetCurrentLevelElapsedTime();
    }

    public void StartTimerServer()
    {
        timerRunning = true;
        currentLevelStartTime = Time.time;
        isLocalPlayerFinishedLevel = false;
        if (!hasInitializedRun)
        {
            ResetRunTimer();
        }
    }

    public void StopTimerServer()
    {
        timerRunning = false;
    }

    private void Update()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) || activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsServerOrOffline && timerRunning && !IsTimeOver)
        {
            if (IsNetworkActive && IsServer)
            {
                timeRemaining.Value = Mathf.Max(0f, timeRemaining.Value - Time.deltaTime);
                if (timeRemaining.Value <= 0f)
                {
                    timerRunning = false;
                    isTimeOver.Value = true;
                    HandleTimerExpiration();
                }
            }
            else if (!IsNetworkActive)
            {
                offlineTimeRemaining = Mathf.Max(0f, offlineTimeRemaining - Time.deltaTime);
                if (offlineTimeRemaining <= 0f)
                {
                    timerRunning = false;
                    offlineIsTimeOver = true;
                    HandleTimerExpiration();
                }
            }
        }

        if (timerText == null)
        {
            FindOrSetupUIReferences();
        }

        UpdateTimerDisplay(GetRemainingTime());

        if (IsTimeOver && mainMenuButton != null && mainMenuButton.gameObject.activeInHierarchy)
        {
            float rotZ = Mathf.Sin(Time.unscaledTime * 4.0f) * 2.0f;
            mainMenuButton.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            bool submitPressed = false;
            if (UnityEngine.InputSystem.Keyboard.current != null && (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)) submitPressed = true;
            if (UnityEngine.InputSystem.Gamepad.current != null && UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame) submitPressed = true;
            if (submitPressed && mainMenuButton.interactable)
            {
                OnMainMenuButtonClicked();
            }
        }
    }

    public void TriggerTimeOverFromMatchEnd()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) ||
            activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Do not trigger TimeOver if this player has won
        if (FinishLine.LocalPlayerHasWon || (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.hasWonPlayer))
        {
            return;
        }

        timerRunning = false;
        offlineIsTimeOver = true;

        if (LeaderboardManager.Instance != null)
        {
            float elapsedTime = GetCurrentLevelElapsedTime();
            int deaths = CarHealth.LocalPlayerHealth != null ? CarHealth.LocalPlayerHealth.deathCount.Value : 0;
            LeaderboardManager.Instance.RecordLevelCompletion(activeScene, elapsedTime, deaths, isTimeout: true);
            LeaderboardManager.Instance.EnsureAllLevelsRecorded();
        }

        ShowTimeOverUI();
        StartCoroutine(DelayedSwitchToEndingRoutine(2.5f));
    }

    private void HandleTimerExpiration()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene.Equals("Ending", StringComparison.OrdinalIgnoreCase) || activeScene.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (LeaderboardManager.Instance != null)
        {
            float elapsedTime = GetCurrentLevelElapsedTime();
            int deaths = CarHealth.LocalPlayerHealth != null ? CarHealth.LocalPlayerHealth.deathCount.Value : 0;
            LeaderboardManager.Instance.RecordLevelCompletion(activeScene, elapsedTime, deaths, isTimeout: true);
            LeaderboardManager.Instance.EnsureAllLevelsRecorded();
        }

        ShowTimeOverUI();
        StartCoroutine(DelayedSwitchToEndingRoutine(2.5f));
    }

    private IEnumerator DelayedSwitchToEndingRoutine(float delay = 2.5f)
    {
        yield return new WaitForSeconds(delay);

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

    private void OnTimeRemainingChanged(float oldVal, float newVal)
    {
        UpdateTimerDisplay(newVal);
    }

    private void OnTimeOverChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            HandleTimerExpiration();
        }
    }

    private void UpdateTimerDisplay(float seconds)
    {
        if (timerText == null)
        {
            FindOrSetupUIReferences();
        }

        if (timerText != null)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            timerText.text = string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);

            if (seconds <= 30f)
            {
                timerText.color = Color.red;
            }
            else
            {
                timerText.color = Color.white;
            }
        }
    }

    public void FindOrSetupUIReferences()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName.Equals("Ending", StringComparison.OrdinalIgnoreCase) || sceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            timerText = null;
            timeOverPanel = null;
            mainMenuButton = null;
            return;
        }

        if (timerText == null || !timerText.gameObject.scene.isLoaded)
        {
            timerText = null;

            GameObject timerObj = GameObject.FindGameObjectWithTag("Timer");
            if (timerObj != null)
            {
                timerText = timerObj.GetComponent<TextMeshProUGUI>();
            }

            if (timerText == null)
            {
                TextMeshProUGUI[] foundTexts = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var txt in foundTexts)
                {
                    if (txt.gameObject.scene.isLoaded)
                    {
                        string txtName = txt.gameObject.name.ToLower();
                        if (txtName.Contains("timer") || txtName.Contains("time") || txt.CompareTag("Timer"))
                        {
                            timerText = txt;
                            break;
                        }
                    }
                }

                if (timerText == null)
                {
                    foreach (var txt in foundTexts)
                    {
                        if (txt.gameObject.scene.isLoaded &&
                            txt.GetComponentInParent<Canvas>() != null &&
                            txt.GetComponentInParent<Button>() == null &&
                            !txt.gameObject.name.ToLower().Contains("prompt") &&
                            !txt.gameObject.name.ToLower().Contains("start"))
                        {
                            timerText = txt;
                            break;
                        }
                    }
                }
            }
        }

        if (timeOverPanel == null || !timeOverPanel.scene.isLoaded)
        {
            timeOverPanel = null;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.isLoaded && (go.CompareTag("TimeOver") || go.name.Equals("TimeOverPanel", StringComparison.OrdinalIgnoreCase) || go.name.Contains("TimeOver", StringComparison.OrdinalIgnoreCase)))
                {
                    timeOverPanel = go;
                    break;
                }
            }
        }

        if (timeOverPanel != null)
        {
            timeOverPanel.SetActive(IsTimeOver);

            if (mainMenuButton == null)
            {
                mainMenuButton = timeOverPanel.GetComponentInChildren<Button>(true);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveAllListeners();
                mainMenuButton.onClick.AddListener(OnMainMenuButtonClicked);
            }
        }
    }

    private void ShowTimeOverUI()
    {
        FindOrSetupUIReferences();

        FinishLine finishLine = UnityEngine.Object.FindFirstObjectByType<FinishLine>();
        if (finishLine != null)
        {
            GameObject winUI = finishLine.GetWinPanelInScene();
            if (winUI != null) winUI.SetActive(false);

            GameObject tilUI = finishLine.GetTimeIsLessPanelInScene();
            if (tilUI != null) tilUI.SetActive(false);
        }

        if (timeOverPanel != null)
        {
            timeOverPanel.SetActive(true);

            if (mainMenuButton == null)
            {
                mainMenuButton = timeOverPanel.GetComponentInChildren<Button>(true);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveAllListeners();
                mainMenuButton.onClick.AddListener(OnMainMenuButtonClicked);
            }
        }
    }

    private void OnMainMenuButtonClicked()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene("MainMenu");
    }
}
