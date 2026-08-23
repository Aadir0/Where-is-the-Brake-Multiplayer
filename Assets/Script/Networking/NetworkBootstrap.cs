using UnityEngine;
using Unity.Netcode;

public class NetworkBootstrap : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        if (RelayManager.Instance != null)
        {
            await RelayManager.Instance.InitializeServicesAsync();
        }
    }
}

