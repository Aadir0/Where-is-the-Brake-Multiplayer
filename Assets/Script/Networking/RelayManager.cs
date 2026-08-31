using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("Network Settings")]
    [SerializeField] private ushort defaultPort = 7777;
    [SerializeField] private string defaultAddress = "127.0.0.1";

    public string JoinCode { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsConnecting { get; private set; }

    public event Action<string> OnStatusChanged;
    public event Action<string> OnErrorEncountered;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        IsInitialized = true;
    }

    public async Task<bool> InitializeServicesAsync()
    {
        IsInitialized = true;
        return await Task.FromResult(true);
    }

    public async Task<string> StartHostWithRelay(int customMaxPlayers = 4)
    {
        if (IsConnecting) return null;
        IsConnecting = true;

        try
        {
            if (NetworkManager.Singleton == null)
            {
                string errMsg = "NetworkManager instance not found in scene!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return null;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                await Task.Delay(200);
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                string errMsg = "UnityTransport component missing on NetworkManager GameObject!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return null;
            }

            // Direct Host Transport Setup with 6-Character Room Code
            transport.SetConnectionData(defaultAddress, defaultPort, "0.0.0.0");
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != transport)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }

            OnStatusChanged?.Invoke("Starting Host...");
            bool success = NetworkManager.Singleton.StartHost();

            if (success)
            {
                JoinCode = GenerateRandomRoomCode(6);
                OnStatusChanged?.Invoke($"Host Started! Room Code: {JoinCode}");
                IsConnecting = false;
                return JoinCode;
            }
            else
            {
                string errMsg = "NetworkManager.StartHost() returned false. Check UnityTransport setting in NetworkManager.";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke("Failed to start host session.");
                IsConnecting = false;
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error starting host: {ex.Message}\n{ex.StackTrace}");
            OnErrorEncountered?.Invoke($"Error: {ex.Message}");
            IsConnecting = false;
            return null;
        }
    }

    public async Task<bool> StartClientWithRelay(string inputAddressOrCode)
    {
        if (IsConnecting) return false;
        IsConnecting = true;

        string code = string.IsNullOrWhiteSpace(inputAddressOrCode) ? "" : inputAddressOrCode.Trim().ToUpper();
        string targetAddress = (code.Length == 6 && !code.Contains(".")) ? defaultAddress : (string.IsNullOrEmpty(code) ? defaultAddress : code);

        try
        {
            if (NetworkManager.Singleton == null)
            {
                string errMsg = "NetworkManager instance not found in scene!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return false;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                await Task.Delay(200);
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                string errMsg = "UnityTransport component missing on NetworkManager GameObject!";
                Debug.LogError(errMsg);
                OnErrorEncountered?.Invoke(errMsg);
                IsConnecting = false;
                return false;
            }

            // Direct Transport Client Setup
            transport.SetConnectionData(targetAddress, defaultPort);
            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != transport)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }

            JoinCode = string.IsNullOrEmpty(code) ? defaultAddress : code;

            OnStatusChanged?.Invoke($"Connecting to Host at {targetAddress}...");
            bool success = NetworkManager.Singleton.StartClient();

            if (!success)
            {
                OnErrorEncountered?.Invoke("Failed to start client connection.");
            }

            IsConnecting = false;
            return success;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error joining client: {ex.Message}\n{ex.StackTrace}");
            OnErrorEncountered?.Invoke($"Error: {ex.Message}");
            IsConnecting = false;
            return false;
        }
    }

    public void ShutdownSession()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        JoinCode = null;
        IsConnecting = false;
        OnStatusChanged?.Invoke("Disconnected.");
    }

    private string GenerateRandomRoomCode(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        StringBuilder sb = new StringBuilder(length);
        System.Random rng = new System.Random();
        for (int i = 0; i < length; i++)
        {
            sb.Append(chars[rng.Next(chars.Length)]);
        }
        return sb.ToString();
    }
}
