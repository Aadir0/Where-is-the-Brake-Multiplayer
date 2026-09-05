using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private TMP_InputField joinCodeInput; // Room Code / Join Code Input Field
    [SerializeField] private Button joinGameButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveRoomButton;

    [Header("Lobby Button Scale Animation & Input")]
    [SerializeField] private bool enableScaleAnimation = true;
    [SerializeField] private Vector3 selectedScale = new Vector3(1.18f, 1.18f, 1f);
    [SerializeField] private Vector3 unselectedScale = new Vector3(0.85f, 0.85f, 1f);
    [SerializeField] private float scaleSpeed = 12f;
    [SerializeField] private float moveRepeatDelay = 0.22f;
    [SerializeField] private float gamepadDeadzone = 0.4f;

    [Header("Animation Reference")]
    [SerializeField] private JustAButton menuButtonAnimator;

    private const string PLAYER_COUNT_MSG = "UpdatePlayerCountMsg";

    private List<Button> activeLobbyButtons = new List<Button>();
    private Dictionary<Transform, Vector3> originalScales = new Dictionary<Transform, Vector3>();
    private int selectedLobbyIndex = 0;
    private float nextMoveTime;

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

        CacheOriginalScales();
    }

    private void CacheOriginalScales()
    {
        Button[] allButtons = new Button[] { joinGameButton, startGameButton, leaveRoomButton, createRoomButton, joinRoomButton };
        foreach (Button btn in allButtons)
        {
            if (btn != null && !originalScales.ContainsKey(btn.transform))
            {
                originalScales[btn.transform] = btn.transform.localScale;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(SubscribeRelayManagerWhenReady());
        StartCoroutine(SubscribeNetworkCallbacksWhenReady());
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

        StartCoroutine(SubscribeRelayManagerWhenReady());
        StartCoroutine(SubscribeNetworkCallbacksWhenReady());
        CacheOriginalScales();
    }

    private IEnumerator SubscribeRelayManagerWhenReady()
    {
        while (RelayManager.Instance == null)
        {
            yield return null;
        }

        SubscribeToRelayManager();
    }

    private void SubscribeToRelayManager()
    {
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnStatusChanged -= UpdateStatusText;
            RelayManager.Instance.OnErrorEncountered -= HandleError;
            RelayManager.Instance.OnClientConnectedToHost -= HandleRelayClientConnected;
            RelayManager.Instance.OnClientDisconnectedFromHost -= HandleRelayClientDisconnected;

            RelayManager.Instance.OnStatusChanged += UpdateStatusText;
            RelayManager.Instance.OnErrorEncountered += HandleError;
            RelayManager.Instance.OnClientConnectedToHost += HandleRelayClientConnected;
            RelayManager.Instance.OnClientDisconnectedFromHost += HandleRelayClientDisconnected;
        }
    }

    private IEnumerator SubscribeNetworkCallbacksWhenReady()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        EnsureNetworkCallbacksSubscribed();
    }

    public void EnsureNetworkCallbacksSubscribed()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
            RelayManager.Instance.OnErrorEncountered -= HandleError;
            RelayManager.Instance.OnClientConnectedToHost -= HandleRelayClientConnected;
            RelayManager.Instance.OnClientDisconnectedFromHost -= HandleRelayClientDisconnected;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        UnregisterMessagingHandlers();
    }

    private void Update()
    {
        if (lobbyPanel == null || !lobbyPanel.activeInHierarchy) return;

        UpdateLobbyButtonSelectionInput();
        UpdateLobbyButtonScaleAnimation();
    }

    private void UpdateLobbyButtonSelectionInput()
    {
        if (activeLobbyButtons.Count == 0) return;

        if (Time.unscaledTime >= nextMoveTime)
        {
            float vertical = 0f;
            float horizontal = 0f;

            // 1. Keyboard Arrow Keys ONLY (WASD disabled for UI navigation)
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.isPressed) vertical = 1f;
                else if (Keyboard.current.downArrowKey.isPressed) vertical = -1f;

                if (Keyboard.current.leftArrowKey.isPressed) horizontal = -1f;
                else if (Keyboard.current.rightArrowKey.isPressed) horizontal = 1f;
            }

            // 2. Gamepad D-Pad ONLY (Left Stick strictly disabled)
            Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
            if (gamepad != null)
            {
                float dpadY = gamepad.dpad.ReadValue().y;
                float dpadX = gamepad.dpad.ReadValue().x;

                if (Mathf.Abs(dpadY) >= gamepadDeadzone) vertical = dpadY;
                if (Mathf.Abs(dpadX) >= gamepadDeadzone) horizontal = dpadX;
            }

            if (vertical > gamepadDeadzone || horizontal < -gamepadDeadzone)
            {
                MoveLobbySelection(-1);
            }
            else if (vertical < -gamepadDeadzone || horizontal > gamepadDeadzone)
            {
                MoveLobbySelection(1);
            }
        }

        // 3. Selection Submit Input: Enter / Space / Gamepad buttonSouth
        bool submitPressed = false;
        if (Keyboard.current != null)
        {
            submitPressed = Keyboard.current.enterKey.wasPressedThisFrame ||
                            Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                            Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        Gamepad activeGamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
        if (activeGamepad != null && activeGamepad.buttonSouth.wasPressedThisFrame)
        {
            submitPressed = true;
        }

        if (submitPressed && selectedLobbyIndex >= 0 && selectedLobbyIndex < activeLobbyButtons.Count)
        {
            Button btn = activeLobbyButtons[selectedLobbyIndex];
            if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
            {
                btn.onClick.Invoke();
            }
        }
    }

    private void MoveLobbySelection(int direction)
    {
        if (activeLobbyButtons.Count == 0) return;

        selectedLobbyIndex += direction;
        if (selectedLobbyIndex < 0) selectedLobbyIndex = activeLobbyButtons.Count - 1;
        else if (selectedLobbyIndex >= activeLobbyButtons.Count) selectedLobbyIndex = 0;

        nextMoveTime = Time.unscaledTime + moveRepeatDelay;
    }

    private void UpdateLobbyButtonScaleAnimation()
    {
        if (!enableScaleAnimation || activeLobbyButtons.Count == 0) return;

        float rotZ = Mathf.Sin(Time.unscaledTime * 4.0f) * 2.0f;

        for (int i = 0; i < activeLobbyButtons.Count; i++)
        {
            Button btn = activeLobbyButtons[i];
            if (btn == null || !btn.gameObject.activeInHierarchy) continue;

            Vector3 baseScale = originalScales.ContainsKey(btn.transform) ? originalScales[btn.transform] : Vector3.one;
            bool isSelected = (i == selectedLobbyIndex);
            Vector3 targetScale = isSelected ? Vector3.Scale(baseScale, selectedScale) : Vector3.Scale(baseScale, unselectedScale);

            btn.transform.localScale = Vector3.Lerp(btn.transform.localScale, targetScale, scaleSpeed * Time.unscaledDeltaTime);

            btn.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(btn.transform.localRotation, Quaternion.identity, 10f * Time.unscaledDeltaTime);
        }
    }

    public void CreateRoom()
    {
        OnCreateRoomClicked();
    }

    public void UpdatePlayerCountDisplay(int count)
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"PLAYERS: {count} / 2";
        }
    }

    public void OpenJoinUI()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                ExecuteOpenJoinUI();
            });
        }
        else
        {
            ExecuteOpenJoinUI();
        }
    }

    private void ExecuteOpenJoinUI()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(true);
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.characterLimit = 15;
            joinCodeInput.gameObject.SetActive(true);
            joinCodeInput.text = "";
            joinCodeInput.Select();
            joinCodeInput.ActivateInputField();
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
        if (leaveRoomButton != null) leaveRoomButton.gameObject.SetActive(true);

        // Update active lobby button selection list
        activeLobbyButtons.Clear();
        if (joinGameButton != null && joinGameButton.gameObject.activeInHierarchy) activeLobbyButtons.Add(joinGameButton);
        if (leaveRoomButton != null && leaveRoomButton.gameObject.activeInHierarchy) activeLobbyButtons.Add(leaveRoomButton);
        selectedLobbyIndex = 0;

        UpdateStatusText("Enter Room Code (or IP) and click Join Game.");
    }

    public void JoinRoom()
    {
        OpenJoinUI();
    }

    private void OnCreateRoomClicked()
    {
        ExecuteCreateRoom();
    }

    private async void ExecuteCreateRoom()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.ShowTransitionCover();
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        UpdateStatusText("Creating Room...");

        if (RelayManager.Instance == null)
        {
            UpdateStatusText("RelayManager instance not found!");
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.HideTransitionCover();
            }
            return;
        }

        SetInteractable(false);
        string code = await RelayManager.Instance.StartHostWithRelay();
        EnsureNetworkCallbacksSubscribed();
        SetInteractable(true);

        if (!string.IsNullOrEmpty(code))
        {
            ShowHostLobbyUI(code);
            RegisterMessagingHandlers();
            BroadcastPlayerCountServer();
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.HideTransitionCover();
        }
    }

    private void OnJoinGameButtonClicked()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                ExecuteJoinGame();
            });
        }
        else
        {
            ExecuteJoinGame();
        }
    }

    private async void ExecuteJoinGame()
    {
        if (RelayManager.Instance == null)
        {
            UpdateStatusText("RelayManager instance not found!");
            return;
        }

        string code = joinCodeInput != null ? joinCodeInput.text : "";
        SetInteractable(false);
        UpdateStatusText("Connecting to room...");

        // Ensure callbacks are set before attempting connection
        EnsureNetworkCallbacksSubscribed();
        bool success = await RelayManager.Instance.StartClientWithRelay(code);
        EnsureNetworkCallbacksSubscribed();

        if (!success)
        {
            SetInteractable(true);
        }
    }

    private void OnStartGameClicked()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
                {
                    NetworkManager.Singleton.SceneManager.LoadScene("Level 1", UnityEngine.SceneManagement.LoadSceneMode.Single);
                }
            });
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.SceneManager.LoadScene("Level 1", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    private void OnLeaveRoomClicked()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                ExecuteLeaveRoom();
            });
        }
        else
        {
            ExecuteLeaveRoom();
        }
    }

    private void ExecuteLeaveRoom()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        UnregisterMessagingHandlers();
        ShowMainMenuUI();

        if (menuButtonAnimator == null)
        {
            menuButtonAnimator = UnityEngine.Object.FindFirstObjectByType<JustAButton>();
        }

        if (menuButtonAnimator != null)
        {
            menuButtonAnimator.SelectButton(0);
        }
    }

    private void ShowMainMenuUI()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(false);
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        activeLobbyButtons.Clear();
        SetInteractable(true);
        UpdateStatusText("Ready.");
    }

    private void ShowHostLobbyUI(string roomCode)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (roomCodeText != null)
        {
            roomCodeText.gameObject.SetActive(true);
            roomCodeText.text = $"ROOM CODE: {roomCode}";
        }

        if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(false);
        if (joinGameButton != null) joinGameButton.gameObject.SetActive(false);

        if (playerCountText != null)
        {
            playerCountText.gameObject.SetActive(true);
            playerCountText.text = "PLAYERS: 1 / 2";
        }

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(true);
            startGameButton.interactable = true;
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.gameObject.SetActive(true);
        }

        // Active lobby buttons for Host: [Start Game, Leave Room]
        activeLobbyButtons.Clear();
        if (startGameButton != null && startGameButton.gameObject.activeInHierarchy) activeLobbyButtons.Add(startGameButton);
        if (leaveRoomButton != null && leaveRoomButton.gameObject.activeInHierarchy) activeLobbyButtons.Add(leaveRoomButton);
        selectedLobbyIndex = 0;

        UpdateStatusText($"Room created! Share Room Code: {roomCode}");
    }

    private void ShowClientLobbyUI(string roomCode)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (roomCodeText != null)
        {
            roomCodeText.gameObject.SetActive(true);
            roomCodeText.text = $"CONNECTED ROOM: {roomCode}";
        }

        if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(false);
        if (joinGameButton != null) joinGameButton.gameObject.SetActive(false);

        if (playerCountText != null)
        {
            playerCountText.gameObject.SetActive(true);
            playerCountText.text = "PLAYERS: 2 / 2";
        }

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(false);
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.gameObject.SetActive(true);
        }

        // Active lobby buttons for Client: [Leave Room]
        activeLobbyButtons.Clear();
        if (leaveRoomButton != null && leaveRoomButton.gameObject.activeInHierarchy) activeLobbyButtons.Add(leaveRoomButton);
        selectedLobbyIndex = 0;

        UpdateStatusText("Waiting for Host to start game...");
    }

    private void OnClientConnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        if (networkManager.IsHost)
        {
            BroadcastPlayerCountServer();
        }
        else if (clientId == networkManager.LocalClientId)
        {
            HandleRelayClientConnected();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        if (networkManager.IsHost)
        {
            BroadcastPlayerCountServer();
        }
        else if (clientId == networkManager.LocalClientId)
        {
            string reason = networkManager.DisconnectReason;
            string msg = string.IsNullOrEmpty(reason) ? "Disconnected from host." : $"Connection failed: {reason}";
            HandleRelayClientDisconnected(msg);
        }
    }

    private void HandleRelayClientConnected()
    {
        ShowClientLobbyUI(RelayManager.Instance != null ? RelayManager.Instance.JoinCode : "CONNECTED");
        RegisterMessagingHandlers();
    }

    private void HandleRelayClientDisconnected(string message)
    {
        ShowMainMenuUI();
        UpdateStatusText(message);
    }

    private void RegisterMessagingHandlers()
    {
        if (NetworkManager.Singleton == null) return;
        var customMessaging = NetworkManager.Singleton.CustomMessagingManager;
        if (customMessaging != null)
        {
            customMessaging.RegisterNamedMessageHandler(PLAYER_COUNT_MSG, OnPlayerCountMessageReceived);
        }
    }

    private void UnregisterMessagingHandlers()
    {
        if (NetworkManager.Singleton == null) return;
        var customMessaging = NetworkManager.Singleton.CustomMessagingManager;
        if (customMessaging != null)
        {
            customMessaging.UnregisterNamedMessageHandler(PLAYER_COUNT_MSG);
        }
    }

    private void BroadcastPlayerCountServer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.CustomMessagingManager == null) return;

        try
        {
            int count = NetworkManager.Singleton.ConnectedClientsList != null ? NetworkManager.Singleton.ConnectedClientsList.Count : 0;
            UpdatePlayerCountDisplay(count);

            FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<int>(), Allocator.Temp);
            using (writer)
            {
                writer.WriteValueSafe(count);
                if (NetworkManager.Singleton.CustomMessagingManager != null)
                {
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(PLAYER_COUNT_MSG, writer);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LobbyUI] BroadcastPlayerCountServer exception: {ex.Message}");
        }
    }

    private void OnPlayerCountMessageReceived(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        UpdatePlayerCountDisplay(count);
    }

    private void UpdateStatusText(string msg)
    {
        if (statusText != null)
        {
            statusText.text = msg;
        }
    }

    private void HandleError(string errorMsg)
    {
        UpdateStatusText($"Error: {errorMsg}");
        SetInteractable(true);
    }

    private void SetInteractable(bool state)
    {
        if (createRoomButton != null) createRoomButton.interactable = state;
        if (joinRoomButton != null) joinRoomButton.interactable = state;
        if (joinGameButton != null) joinGameButton.interactable = state;
    }
}
