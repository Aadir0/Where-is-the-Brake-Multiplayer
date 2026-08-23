using System;
using System.Collections;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LevelTimer : NetworkBehaviour
{
    public static LevelTimer Instance { get; private set; }

    [Header("Timer Config")]
    [SerializeField] private float levelDurationSeconds = 300f; // 5 minutes

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

    private void Awake()
    {
        Instance = this;
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
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        FindOrSetupUIReferences();

        if (IsServer && !scene.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            timerRunning = false;
            timeRemaining.Value = levelDurationSeconds;
            isTimeOver.Value = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        timeRemaining.OnValueChanged += OnTimeRemainingChanged;
        isTimeOver.OnValueChanged += OnTimeOverChanged;

        if (IsServer)
        {
            timerRunning = false;
            timeRemaining.Value = levelDurationSeconds;
            isTimeOver.Value = false;
        }

        FindOrSetupUIReferences();
        UpdateTimerDisplay(timeRemaining.Value);
    }

    public override void OnNetworkDespawn()
    {
        timeRemaining.OnValueChanged -= OnTimeRemainingChanged;
        isTimeOver.OnValueChanged -= OnTimeOverChanged;
    }

    public float GetElapsedTime()
    {
        return Mathf.Max(0f, levelDurationSeconds - timeRemaining.Value);
    }

    public void StartTimerServer()
    {
        if (!IsServer) return;
        Debug.Log("[LevelTimer] Race started! 5-minute timer is NOW RUNNING!");
        timerRunning = true;
    }

    public void StopTimerServer()
    {
        if (!IsServer) return;
        timerRunning = false;
    }

    private void Update()
    {
        if (IsServer && timerRunning && !isTimeOver.Value)
        {
            timeRemaining.Value = Mathf.Max(0f, timeRemaining.Value - Time.deltaTime);

            if (timeRemaining.Value <= 0f)
            {
                timerRunning = false;
                isTimeOver.Value = true;
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
            ShowTimeOverUI();
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
        if (timerText == null)
        {
            GameObject timerObj = GameObject.FindGameObjectWithTag("Timer");
            if (timerObj != null)
            {
                timerText = timerObj.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                TextMeshProUGUI[] foundTexts = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var txt in foundTexts)
                {
                    if (txt.gameObject.name.Contains("Timer", StringComparison.OrdinalIgnoreCase))
                    {
                        timerText = txt;
                        break;
                    }
                }
            }
        }

        if (timeOverPanel == null)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.CompareTag("TimeOver") && go.scene.isLoaded)
                {
                    timeOverPanel = go;
                    break;
                }
                else if (go.name.Equals("TimeOverPanel", StringComparison.OrdinalIgnoreCase) && go.scene.isLoaded)
                {
                    timeOverPanel = go;
                    break;
                }
            }
        }

        if (timeOverPanel != null)
        {
            timeOverPanel.SetActive(false);
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
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}
