using System;
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

    public async Task<string> StartHostWithRelay(int customMaxPlayers = 0)
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
                Debug.Log("Shutting down active network session before starting host...");
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

            // Direct Host Transport Setup (No UGS Authentication required)
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
                JoinCode = "HOST ROOM";
                OnStatusChanged?.Invoke("Host Started Successfully!");
                IsConnecting = false;
                return JoinCode;
            }
            else
            {
                string errMsg = "NetworkManager.StartHost() returned false. Check UnityTransport setting and NetworkPrefabs in NetworkManager Inspector.";
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

    public async Task<bool> StartClientWithRelay(string inputAddress)
    {
        if (IsConnecting) return false;
        IsConnecting = true;

        string targetAddress = string.IsNullOrWhiteSpace(inputAddress) ? defaultAddress : inputAddress.Trim();

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

            // Direct Client Transport Setup (No UGS Authentication required)
            transport.SetConnectionData(targetAddress, defaultPort);

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != transport)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
            }

            JoinCode = targetAddress;

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
}
