using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FinishLine : MonoBehaviour
{
    public GameObject winPanel;
    public CameraZoom2D cameraZoom;
    [SerializeField] private InputAction resetAction;

    private bool hasWon;
    private bool isLocalPlayerReady;

    private void Start()
    {
        hasWon = false;
        isLocalPlayerReady = false;

        // Hide Win panel tagged "Win" initially
        GameObject winUI = GetWinPanelInScene();
        if (winUI != null) winUI.SetActive(false);

        if (winPanel != null) winPanel.SetActive(false);
    }

    private void OnEnable()
    {
        resetAction.Enable();

        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnReadyPlayerCountChanged += OnReadyPlayerCountChanged;
        }
    }

    private void OnDisable()
    {
        resetAction.Disable();

        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnReadyPlayerCountChanged -= OnReadyPlayerCountChanged;
        }
    }

    private void Update()
    {
        if (hasWon && !isLocalPlayerReady)
        {
            bool readyPressed = Input.GetKeyDown(KeyCode.R) ||
                               (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) ||
                               resetAction.WasPressedThisFrame();

            if (readyPressed)
            {
                isLocalPlayerReady = true;

                if (NetworkRaceManager.Instance != null && NetworkManager.Singleton != null)
                {
                    NetworkRaceManager.Instance.RequestSetReadyServerRpc(NetworkManager.Singleton.LocalClientId);
                }

                UpdateReadyPromptText("READY! WAITING FOR OTHERS...");
            }
        }
    }

    private void OnReadyPlayerCountChanged(int readyCount, int totalCount)
    {
        if (hasWon)
        {
            if (isLocalPlayerReady)
            {
                UpdateReadyPromptText($"READY! WAITING FOR OTHERS ({readyCount}/{totalCount})...");
            }
            else
            {
                UpdateReadyPromptText($"PRESS 'R' OR [A] BUTTON WHEN READY ({readyCount}/{totalCount})");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Player"))
        {
            NetworkObject netObj = col.GetComponent<NetworkObject>();
            NetworkCarController carCtrl = col.GetComponent<NetworkCarController>();
            CarHealth healthComp = col.GetComponent<CarHealth>();

            // Stop the car at the finish line instantly
            if (carCtrl != null)
            {
                carCtrl.SetCarWonRpc();
            }

            int deaths = healthComp != null ? healthComp.deathCount.Value : 0;
            float elapsedTime = LevelTimer.Instance != null ? LevelTimer.Instance.GetElapsedTime() : 0f;

            if (netObj != null)
            {
                if (NetworkRaceManager.Instance != null)
                {
                    NetworkRaceManager.Instance.PlayerCrossedFinishServer(netObj.OwnerClientId);
                }

                if (netObj.IsOwner && !hasWon)
                {
                    TriggerWinLocal(col.transform, elapsedTime, deaths);
                }
            }
            else if (!hasWon)
            {
                TriggerWinLocal(col.transform, elapsedTime, deaths);
            }
        }
    }

    private GameObject GetWinPanelInScene()
    {
        if (winPanel != null) return winPanel;

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.CompareTag("Win") && go.scene.isLoaded)
            {
                return go;
            }
            else if (go.name.Equals("WinPanel", StringComparison.OrdinalIgnoreCase) && go.scene.isLoaded)
            {
                return go;
            }
        }

        return GameObject.FindGameObjectWithTag("Win");
    }

    private void TriggerWinLocal(Transform winnerTransform, float elapsedTimeSeconds, int deaths)
    {
        hasWon = true;
        isLocalPlayerReady = false;

        GameObject winUI = GetWinPanelInScene();
        if (winUI != null)
        {
            winUI.SetActive(true);
            PopulateWinStatsUI(winUI, elapsedTimeSeconds, deaths);
            UpdateReadyPromptText("PRESS 'R' OR [A] BUTTON WHEN READY TO LOAD NEXT LEVEL!");
        }

        if (cameraZoom != null)
        {
            cameraZoom.StartZoom(winnerTransform);
        }
        else if (Camera.main != null)
        {
            CameraZoom2D camZoom = Camera.main.GetComponent<CameraZoom2D>();
            if (camZoom != null)
            {
                camZoom.StartZoom(winnerTransform);
            }
        }
    }

    private void PopulateWinStatsUI(GameObject winUI, float elapsedTimeSeconds, int deaths)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(elapsedTimeSeconds);
        string formattedTime = string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);

        TextMeshProUGUI[] textComponents = winUI.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in textComponents)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("time") && !objName.Contains("over"))
            {
                txt.text = $"Time: {formattedTime}";
            }
            else if (objName.Contains("death"))
            {
                txt.text = $"Deaths: {deaths}";
            }
        }

        Text[] legacyTexts = winUI.GetComponentsInChildren<Text>(true);
        foreach (var txt in legacyTexts)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("time") && !objName.Contains("over"))
            {
                txt.text = $"Time: {formattedTime}";
            }
            else if (objName.Contains("death"))
            {
                txt.text = $"Deaths: {deaths}";
            }
        }
    }

    private void UpdateReadyPromptText(string promptText)
    {
        GameObject winUI = GetWinPanelInScene();
        if (winUI == null) return;

        TextMeshProUGUI[] textComponents = winUI.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in textComponents)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("ready") || objName.Contains("prompt") || objName.Contains("info") || objName.Contains("next"))
            {
                txt.text = promptText;
            }
        }

        Text[] legacyTexts = winUI.GetComponentsInChildren<Text>(true);
        foreach (var txt in legacyTexts)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("ready") || objName.Contains("prompt") || objName.Contains("info") || objName.Contains("next"))
            {
                txt.text = promptText;
            }
        }
    }
}