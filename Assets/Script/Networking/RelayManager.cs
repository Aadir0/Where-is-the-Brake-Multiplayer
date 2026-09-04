using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    [SerializeField] private float clientConnectionTimeout = 60f;
    private const string relayProtocol = "dtls";
    private const int clientConnectionBufferTimeoutSeconds = 30;

    public static RelayManager Instance { get; private set; }

    public string JoinCode { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsConnecting { get; private set; }

    public event Action<string> OnStatusChanged;
    public event Action<string> OnErrorEncountered;
    public event Action OnClientConnectedToHost;
    public event Action<string> OnClientDisconnectedFromHost;

    private Coroutine waitConnectionCoroutine;
    private Task<bool> initializeServicesTask;
    private readonly string servicesProfileName = CreateServicesProfileName();
    private bool clientConnectionCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        clientConnectionTimeout = Mathf.Max(clientConnectionTimeout, clientConnectionBufferTimeoutSeconds);

        StartCoroutine(SubscribeTransportFailureWhenReady());
    }

    private IEnumerator SubscribeTransportFailureWhenReady()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        HookTransportFailure();
        HookConnectionCallbacks();
    }

    private void HookTransportFailure()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
            NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
        }
    }

    private void HookConnectionCallbacks()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    // Transport failure callback
    private void HandleTransportFailure()
    {
        string msg = "Network Transport failure occurred.";
        Debug.LogError($"[RelayManager] {msg}");
        OnErrorEncountered?.Invoke(msg);
        IsConnecting = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.IsHost || clientId != networkManager.LocalClientId)
        {
            return;
        }

        CompleteClientConnection();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.IsHost || clientId != networkManager.LocalClientId)
        {
            return;
        }

        string reason = networkManager.DisconnectReason;
        string msg = string.IsNullOrEmpty(reason) ? "Disconnected from host." : $"Connection failed: {reason}";
        IsConnecting = false;
        clientConnectionCompleted = false;
        waitConnectionCoroutine = null;
        OnClientDisconnectedFromHost?.Invoke(msg);
    }

    private void CompleteClientConnection()
    {
        if (clientConnectionCompleted)
        {
            return;
        }

        clientConnectionCompleted = true;
        IsConnecting = false;
        waitConnectionCoroutine = null;
        Debug.Log("[RelayManager] Successfully connected to Relay Host!");
        OnStatusChanged?.Invoke("Connected to Relay Host.");
        OnClientConnectedToHost?.Invoke();
    }

    // Coroutine to wait for the client to actually connect within timeout
    private IEnumerator WaitForClientConnection()
    {
        float timer = clientConnectionTimeout;
        float nextLogTime = 2f;
        while (timer > 0 && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            if (!NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsClient)
            {
                string reason = NetworkManager.Singleton.DisconnectReason;
                string abortMsg = string.IsNullOrEmpty(reason)
                    ? "Connection was rejected or dropped by the host."
                    : $"Connection failed: {reason}";

                Debug.LogWarning($"[RelayManager] {abortMsg}");
                OnErrorEncountered?.Invoke(abortMsg);
                IsConnecting = false;
                waitConnectionCoroutine = null;
                yield break;
            }

            timer -= Time.unscaledDeltaTime;
            nextLogTime -= Time.unscaledDeltaTime;
            if (nextLogTime <= 0f)
            {
                nextLogTime = 2f;
                string statusMsg = $"Still connecting to host... ({Mathf.CeilToInt(timer)}s remaining)";
                Debug.Log($"[RelayManager] {statusMsg}");
                OnStatusChanged?.Invoke(statusMsg);
            }
            yield return null;
        }

        // If the loop ends without a successful connection, we simply exit.
        // The higher level UI can decide whether to retry.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            CompleteClientConnection();
        }
        else if (NetworkManager.Singleton == null)
        {
            const string missingManagerMsg = "Connection aborted because NetworkManager is no longer available.";
            Debug.LogWarning($"[RelayManager] {missingManagerMsg}");
            OnErrorEncountered?.Invoke(missingManagerMsg);
        }
        // No timeout error is reported here.

    }

    public async Task<bool> InitializeServicesAsync()
    {
        if (IsInitialized && AuthenticationService.Instance.IsSignedIn) return true;
        if (initializeServicesTask != null) return await initializeServicesTask;

        initializeServicesTask = InitializeServicesInternalAsync();
        bool success = await initializeServicesTask;
        if (!success)
        {
            initializeServicesTask = null;
        }

        return success;
    }

    private async Task<bool> InitializeServicesInternalAsync()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                OnStatusChanged?.Invoke("Initializing Unity Services...");
                var options = new InitializationOptions();
                options.SetProfile(servicesProfileName);
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                OnStatusChanged?.Invoke("Signing in anonymously...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[RelayManager] Signed in anonymously. Profile: {AuthenticationService.Instance.Profile}, Player ID: {AuthenticationService.Instance.PlayerId}");
            }

            IsInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Failed to initialize Unity Services: {ex.Message}");
            OnErrorEncountered?.Invoke($"Services error: {ex.Message}");
            IsInitialized = false;
            return false;
        }
    }

    public async Task<string> StartHostWithRelay(int customMaxPlayers = 4)
    {
        if (IsConnecting)
        {
            Debug.LogWarning("[RelayManager] Ignored host request - already connecting.");
            return null;
        }
        IsConnecting = true;
        clientConnectionCompleted = false;

        try
        {
            bool initSuccess = await InitializeServicesAsync();
            if (!initSuccess)
            {
                IsConnecting = false;
                return null;
            }

            if (NetworkManager.Singleton == null)
            {
                string errMsg = "NetworkManager instance not found in scene!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return null;
            }

            await PrepareNetworkManagerForNewSessionAsync();

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                string errMsg = "UnityTransport component missing on NetworkManager GameObject!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return null;
            }

            OnStatusChanged?.Invoke("Allocating Relay Host...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(customMaxPlayers);

            JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"[RelayManager] Configuring Host with Relay protocol '{relayProtocol}', Join Code: {JoinCode}");

            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, relayProtocol);
            ConfigureTransportForRelay(transport);
            transport.SetRelayServerData(relayServerData);
            ConfigureNetworkForRelay(transport);

            OnStatusChanged?.Invoke($"Starting Host... Room Code: {JoinCode}");
            bool success = NetworkManager.Singleton.StartHost();

            if (success)
            {
                Debug.Log($"[RelayManager] Host Started successfully! Room Code: {JoinCode}");
                OnStatusChanged?.Invoke($"Host Started! Room Code: {JoinCode}");
                IsConnecting = false;
                return JoinCode;
            }
            else
            {
                string errMsg = "NetworkManager.StartHost() returned false. Check UnityTransport settings.";
                Debug.LogError($"[RelayManager] {errMsg}");
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Unexpected error starting Relay host: {ex.Message}\n{ex.StackTrace}");
            OnErrorEncountered?.Invoke($"Relay Error: {ex.Message}");
            IsConnecting = false;
            return null;
        }
    }

    public async Task<bool> StartClientWithRelay(string inputJoinCode)
    {
        if (IsConnecting)
        {
            Debug.LogWarning("[RelayManager] Ignored join request - already connecting.");
            return false;
        }
        IsConnecting = true;
        clientConnectionCompleted = false;

        string code = string.IsNullOrWhiteSpace(inputJoinCode) ? "" : inputJoinCode.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            OnErrorEncountered?.Invoke("Please enter a valid Join Code.");
            IsConnecting = false;
            return false;
        }

        try
        {
            bool initSuccess = await InitializeServicesAsync();
            if (!initSuccess)
            {
                IsConnecting = false;
                return false;
            }

            if (NetworkManager.Singleton == null)
            {
                string errMsg = "NetworkManager instance not found in scene!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return false;
            }

            await PrepareNetworkManagerForNewSessionAsync();

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                string errMsg = "UnityTransport component missing on NetworkManager GameObject!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return false;
            }

            OnStatusChanged?.Invoke($"Joining Relay Session with Code {code}...");
            Debug.Log($"[RelayManager] Requesting JoinAllocation for code: {code}");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

            // Determine protocol string based on setting (default udp)
            string proto = string.IsNullOrWhiteSpace(relayProtocol) ? "udp" : relayProtocol.Trim().ToLowerInvariant();
            Debug.Log($"[RelayManager] Configuring Client with Relay protocol '{proto}'");

            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, proto);
            ConfigureTransportForRelay(transport);
            transport.SetRelayServerData(relayServerData);
            ConfigureNetworkForRelay(transport);

            JoinCode = code;

            OnStatusChanged?.Invoke("Connecting to Relay Host...");
            await Task.Yield();

            bool success = NetworkManager.Singleton.StartClient();
            if (success)
            {
                Debug.Log($"[RelayManager] StartClient() started successfully. Awaiting handshake with host...");
                if (waitConnectionCoroutine != null) StopCoroutine(waitConnectionCoroutine);
                waitConnectionCoroutine = StartCoroutine(WaitForClientConnection());
            }
            else
            {
                string reason = NetworkManager.Singleton.DisconnectReason;
                string errMsg = string.IsNullOrEmpty(reason) ? "Failed to start client transport." : $"Connection failed: {reason}";
                Debug.LogError($"[RelayManager] {errMsg}");
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
            }

            return success;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Unexpected error joining Relay client: {ex.Message}\n{ex.StackTrace}");
            OnErrorEncountered?.Invoke($"Relay Error: {ex.Message}");
            IsConnecting = false;
            return false;
        }
    }

    public void ShutdownSession()
    {
        if (waitConnectionCoroutine != null)
        {
            StopCoroutine(waitConnectionCoroutine);
            waitConnectionCoroutine = null;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        JoinCode = null;
        IsConnecting = false;
        clientConnectionCompleted = false;
        OnStatusChanged?.Invoke("Disconnected.");
    }

    private async Task PrepareNetworkManagerForNewSessionAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        HookTransportFailure();
        HookConnectionCallbacks();

        if (waitConnectionCoroutine != null)
        {
            StopCoroutine(waitConnectionCoroutine);
            waitConnectionCoroutine = null;
        }

        if (networkManager.IsListening || networkManager.ShutdownInProgress)
        {
            networkManager.Shutdown();

            const int maxWaitMs = 5000;
            int elapsedMs = 0;
            while ((networkManager.IsListening || networkManager.ShutdownInProgress) && elapsedMs < maxWaitMs)
            {
                await Task.Delay(50);
                elapsedMs += 50;
            }

            if (networkManager.IsListening || networkManager.ShutdownInProgress)
            {
                Debug.LogWarning("[RelayManager] NetworkManager did not finish shutting down before starting a new Relay session.");
            }
            else
            {
                await Task.Delay(100);
            }
        }

        HookTransportFailure();
        HookConnectionCallbacks();
    }

    private static void ConfigureNetworkForRelay(UnityTransport transport)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.ConnectionApproval = false;
        networkManager.NetworkConfig.ClientConnectionBufferTimeout = clientConnectionBufferTimeoutSeconds;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.ForceSamePrefabs = false;
    }

    private static void ConfigureTransportForRelay(UnityTransport transport)
    {
        transport.UseWebSockets = relayProtocol == "wss";
    }

    private static string CreateServicesProfileName()
    {
        int processId = 0;
        try
        {
            processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        catch
        {
            processId = UnityEngine.Random.Range(1, 999999);
        }

        return $"relay_{processId}";
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }
}
