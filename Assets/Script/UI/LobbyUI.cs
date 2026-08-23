using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("Main Menu Controls")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;

    [Header("Lobby Panel Controls")]
    [SerializeField] private TextMeshProUGUI roomCodeText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TMP_InputField joinCodeInput; // IP Address Input Field (e.g. 127.0.0.1)
    [SerializeField] private Button joinGameButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveRoomButton;

    [Header("Animation Reference")]
    [SerializeField] private JustAButton menuButtonAnimator;

    private const string PLAYER_COUNT_MSG = "UpdatePlayerCountMsg";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.characterLimit = 15;
            joinCodeInput.characterValidation = TMP_InputField.CharacterValidation.None;
        }
    }

    private void OnEnable()
    {
        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (createRoomButton != null) createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        if (joinRoomButton != null) joinRoomButton.onClick.AddListener(OpenJoinUI);
        if (joinGameButton != null) joinGameButton.onClick.AddListener(OnJoinGameButtonClicked);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (leaveRoomButton != null) leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnStatusChanged += UpdateStatusText;
            RelayManager.Instance.OnErrorEncountered += UpdateStatusText;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        ShowMainMenuPanel();
    }

    private void OnDisable()
    {
        if (createRoomButton != null) createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
        if (joinRoomButton != null) joinRoomButton.onClick.RemoveListener(OpenJoinUI);
        if (joinGameButton != null) joinGameButton.onClick.RemoveListener(OnJoinGameButtonClicked);
        if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGameClicked);
        if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnStatusChanged -= UpdateStatusText;
            RelayManager.Instance.OnErrorEncountered -= UpdateStatusText;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(PLAYER_COUNT_MSG);
            }
        }
    }

    public void CreateRoom()
    {
        OnCreateRoomClicked();
    }

    public void OpenJoinUI()
    {
        StartCoroutine(OpenJoinUIRoutine());
    }

    private IEnumerator OpenJoinUIRoutine()
    {
        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (menuButtonAnimator != null)
        {
            menuButtonAnimator.PlayPressAnimation();
            yield return menuButtonAnimator.WaitForPlayAnimation();
        }

        ExecuteOpenJoinUI();
    }

    private void ExecuteOpenJoinUI()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (joinCodeInput != null)
        {
            joinCodeInput.characterLimit = 15;
            joinCodeInput.gameObject.SetActive(true);
            joinCodeInput.text = "127.0.0.1";
        }

        if (joinGameButton != null)
        {
            joinGameButton.gameObject.SetActive(true);
            joinGameButton.interactable = true;
            TextMeshProUGUI btnText = joinGameButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "JOIN GAME";
        }

        if (roomCodeText != null) roomCodeText.gameObject.SetActive(false);
        if (startGameButton != null) startGameButton.gameObject.SetActive(false);
        if (playerCountText != null) playerCountText.gameObject.SetActive(false);

        UpdateStatusText("Click Join Game to connect (default IP: 127.0.0.1).");
    }

    public void JoinRoom()
    {
        OpenJoinUI();
    }

    private void OnCreateRoomClicked()
    {
        StartCoroutine(CreateRoomRoutine());
    }

    private IEnumerator CreateRoomRoutine()
    {
        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (menuButtonAnimator != null)
        {
            menuButtonAnimator.PlayPressAnimation();
            yield return menuButtonAnimator.WaitForPlayAnimation();
        }

        ExecuteCreateRoom();
    }

    private async void ExecuteCreateRoom()
    {
        if (RelayManager.Instance == null)
        {
            UpdateStatusText("RelayManager instance not found!");
            return;
        }

        SetInteractable(false);
        string code = await RelayManager.Instance.StartHostWithRelay();
        SetInteractable(true);

        if (!string.IsNullOrEmpty(code))
        {
            ShowHostLobbyUI(code);
            RegisterMessagingHandlers();
            BroadcastPlayerCountServer();
        }
    }

    private async void OnJoinGameButtonClicked()
    {
        if (RelayManager.Instance == null)
        {
            UpdateStatusText("RelayManager instance not found!");
            return;
        }

        string ip = joinCodeInput != null ? joinCodeInput.text : "127.0.0.1";
        if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

        SetInteractable(false);
        UpdateStatusText($"Connecting to Host at {ip}...");
        bool success = await RelayManager.Instance.StartClientWithRelay(ip);
        SetInteractable(true);

        if (success)
        {
            ShowClientConnectedLobbyUI(ip);
            RegisterMessagingHandlers();
            UpdatePlayerCountDisplay(1);
        }
    }

    private void RegisterMessagingHandlers()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(PLAYER_COUNT_MSG, (ulong senderClientId, FastBufferReader reader) =>
            {
                reader.ReadValueSafe(out int count);
                UpdatePlayerCountDisplay(count);
            });
        }
    }

    private void BroadcastPlayerCountServer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        UpdatePlayerCountDisplay(count);

        using FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<int>(), Allocator.Temp);
        writer.WriteValueSafe(count);

        if (NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(PLAYER_COUNT_MSG, writer);
        }
    }

    private void OnStartGameClicked()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            StartCoroutine(StartMatchRoutine());
        }
    }

    private IEnumerator StartMatchRoutine()
    {
        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.StartCountdownServer();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene("Level 1", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Level 1");
        }

        yield break;
    }

    private void OnLeaveRoomClicked()
    {
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (menuButtonAnimator != null)
        {
            menuButtonAnimator.ResetMenuAnimation();
        }

        ShowMainMenuPanel();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[LobbyUI] Client Connected callback: {clientId}");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            BroadcastPlayerCountServer();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[LobbyUI] Client Disconnected callback: {clientId}");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            BroadcastPlayerCountServer();
        }

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsConnectedClient)
        {
            if (menuButtonAnimator == null)
            {
                menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
            }

            if (menuButtonAnimator != null)
            {
                menuButtonAnimator.ResetMenuAnimation();
            }
            ShowMainMenuPanel();
            UpdateStatusText("Disconnected from Host.");
        }
    }

    public void UpdatePlayerCountDisplay(int count)
    {
        if (playerCountText == null) return;
        playerCountText.text = $"Players: {count} / 4";
        playerCountText.gameObject.SetActive(true);
    }

    private void ShowMainMenuPanel()
    {
        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (menuButtonAnimator != null)
        {
            menuButtonAnimator.ResetMenuAnimation();
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        if (createRoomButton != null)
        {
            createRoomButton.gameObject.SetActive(true);
            createRoomButton.interactable = true;
        }

        if (joinRoomButton != null)
        {
            joinRoomButton.gameObject.SetActive(true);
            joinRoomButton.interactable = true;
        }

        if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(false);
        if (roomCodeText != null) roomCodeText.gameObject.SetActive(false);
        if (startGameButton != null) startGameButton.gameObject.SetActive(false);
        if (joinGameButton != null) joinGameButton.gameObject.SetActive(false);
    }

    private void ShowHostLobbyUI(string info)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (roomCodeText != null)
        {
            roomCodeText.gameObject.SetActive(true);
            roomCodeText.text = $"ROOM: {info}";
        }

        if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(false);
        if (joinGameButton != null) joinGameButton.gameObject.SetActive(false);
        if (startGameButton != null) startGameButton.gameObject.SetActive(true);
    }

    private void ShowClientConnectedLobbyUI(string ip)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(false);

        if (joinGameButton != null)
        {
            joinGameButton.gameObject.SetActive(true);
            joinGameButton.interactable = false;
            TextMeshProUGUI btnText = joinGameButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "WAITING FOR HOST...";
        }

        if (roomCodeText != null)
        {
            roomCodeText.gameObject.SetActive(true);
            roomCodeText.text = $"CONNECTED TO: {ip}";
        }

        if (startGameButton != null) startGameButton.gameObject.SetActive(false);
    }

    private void SetInteractable(bool state)
    {
        if (createRoomButton != null) createRoomButton.interactable = state;
        if (joinRoomButton != null) joinRoomButton.interactable = state;
        if (joinGameButton != null) joinGameButton.interactable = state;
        if (joinCodeInput != null) joinCodeInput.interactable = state;
    }

    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[LobbyUI Status] {message}");
    }
}
