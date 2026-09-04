using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FinishLine : MonoBehaviour
{
    public GameObject winPanel;
    [SerializeField] private GameObject timeIsLessPanel;
    [SerializeField] private InputAction resetAction;

    [Header("Winning Stats Scale & Wobble Animation")]
    [SerializeField] private float statsPulseSpeed = 3.5f;
    [SerializeField] private float statsPulseAmount = 0.12f;
    [SerializeField] private float statsWobbleSpeed = 5.0f;
    [SerializeField] private float statsWobbleAngle = 4.0f;

    private bool hasWon;
    private bool isLocalPlayerReady;
    private bool isSpectating;
    private Transform localPlayerTransform;
    private Transform otherPlayerTransform;

    private readonly List<Transform> statsAnimTransforms = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> statsInitialScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> statsInitialRotations = new Dictionary<Transform, Quaternion>();

    private bool IsSinglePlayer => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ConnectedClientsIds.Count <= 1;

    private void Start()
    {
        hasWon = false;
        isLocalPlayerReady = false;
        isSpectating = false;

        GameObject winUI = GetWinPanelInScene();
        if (winUI != null) winUI.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);

        GameObject tilUI = GetTimeIsLessPanelInScene();
        if (tilUI != null) tilUI.SetActive(false);
        if (timeIsLessPanel != null) timeIsLessPanel.SetActive(false);
    }

    private void OnEnable()
    {
        resetAction.Enable();

        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnReadyPlayerCountChanged += OnReadyPlayerCountChanged;
            NetworkRaceManager.Instance.OnBothPlayersFinished += OnBothPlayersFinished;
            NetworkRaceManager.Instance.OnPlayerTimeIsLess += OnPlayerTimeIsLess;
        }
    }

    private void OnDisable()
    {
        resetAction.Disable();

        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnReadyPlayerCountChanged -= OnReadyPlayerCountChanged;
            NetworkRaceManager.Instance.OnBothPlayersFinished -= OnBothPlayersFinished;
            NetworkRaceManager.Instance.OnPlayerTimeIsLess -= OnPlayerTimeIsLess;
        }
    }

    private void Update()
    {
        if (hasWon)
        {
            AnimateWinStatsUI();

            // 1. Singleplayer: Press R / ButtonSouth to proceed directly to next level
            if (IsSinglePlayer)
            {
                bool proceedPressed = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ||
                                     (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) ||
                                     resetAction.WasPressedThisFrame();

                if (proceedPressed)
                {
                    LoadNextLevelLocal();
                }
            }
            else
            {
                // 2. Multiplayer: Ready / Spectate Toggle via P key or Gamepad button
                bool spectatePressed = (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame);
                if (spectatePressed)
                {
                    ToggleSpectateMode();
                }

                if (!isLocalPlayerReady)
                {
                    bool readyPressed = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ||
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
        }
    }

    private void ToggleSpectateMode()
    {
        if (CameraFollow.Instance == null) return;

        if (otherPlayerTransform == null)
        {
            FindOtherPlayerTransform();
        }

        if (otherPlayerTransform != null)
        {
            isSpectating = !isSpectating;
            if (isSpectating)
            {
                CameraFollow.Instance.SetTarget(otherPlayerTransform);
                UpdateReadyPromptText("SPECTATING OTHER PLAYER... PRESS 'P' OR [A] BUTTON TO RETURN");
            }
            else
            {
                CameraFollow.Instance.SetTarget(localPlayerTransform);
                UpdateReadyPromptText(IsSinglePlayer ? "PRESS 'R' OR [A] BUTTON TO LOAD NEXT LEVEL!" : "PRESS 'P' OR [A] BUTTON TO SPECTATE OTHER PLAYER!");
            }
        }
    }

    private void FindOtherPlayerTransform()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject p in players)
        {
            if (p.transform != localPlayerTransform)
            {
                otherPlayerTransform = p.transform;
                break;
            }
        }
    }

    private void AnimateWinStatsUI()
    {
        if (statsAnimTransforms.Count == 0) return;

        float pulseOffset = Mathf.Sin(Time.unscaledTime * statsPulseSpeed) * statsPulseAmount;
        float wobbleZ = Mathf.Sin(Time.unscaledTime * statsWobbleSpeed) * statsWobbleAngle;

        foreach (Transform t in statsAnimTransforms)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;

            Vector3 baseScale = statsInitialScales.ContainsKey(t) ? statsInitialScales[t] : Vector3.one;
            Quaternion baseRot = statsInitialRotations.ContainsKey(t) ? statsInitialRotations[t] : Quaternion.identity;

            t.localScale = baseScale * (1f + pulseOffset);
            t.localRotation = baseRot * Quaternion.Euler(0f, 0f, wobbleZ);
        }
    }

    private void OnReadyPlayerCountChanged(int readyCount, int totalCount)
    {
        if (hasWon && !IsSinglePlayer)
        {
            if (isLocalPlayerReady)
            {
                UpdateReadyPromptText($"READY! WAITING FOR OTHERS ({readyCount}/{totalCount})...");
            }
            else
            {
                UpdateReadyPromptText("PRESS 'P' OR [A] BUTTON TO SPECTATE OTHER PLAYER!");
            }
        }
    }

    private void OnBothPlayersFinished()
    {
    }

    private void OnPlayerTimeIsLess(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId && !hasWon)
        {
            float elapsedTime = LevelTimer.Instance != null ? LevelTimer.Instance.GetElapsedTime() : 300f;
            CarHealth healthComp = localPlayerTransform != null ? localPlayerTransform.GetComponent<CarHealth>() : null;
            int deaths = healthComp != null ? healthComp.deathCount.Value : 0;

            ShowTimeIsLessPanelLocal(elapsedTime, deaths);
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Player"))
        {
            NetworkObject netObj = col.GetComponent<NetworkObject>();
            NetworkCarController carCtrl = col.GetComponent<NetworkCarController>();
            CarHealth healthComp = col.GetComponent<CarHealth>();

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
                    NetworkRaceManager.Instance.RequestPlayerCrossedFinishServerRpc(netObj.OwnerClientId);
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

    public GameObject GetWinPanelInScene()
    {
        if (winPanel != null) return winPanel;

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene.isLoaded && (go.CompareTag("Win") || go.name.Equals("WinPanel", StringComparison.OrdinalIgnoreCase)))
            {
                return go;
            }
        }

        return GameObject.FindGameObjectWithTag("Win");
    }

    public GameObject GetTimeIsLessPanelInScene()
    {
        if (timeIsLessPanel != null) return timeIsLessPanel;

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene.isLoaded && (go.CompareTag("TimeIsLess") || go.name.Equals("TimeIsLessPanel", StringComparison.OrdinalIgnoreCase) || go.name.Contains("TimeIsLess", StringComparison.OrdinalIgnoreCase)))
            {
                return go;
            }
        }

        return null;
    }

    private void TriggerWinLocal(Transform winnerTransform, float elapsedTimeSeconds, int deaths)
    {
        hasWon = true;
        isLocalPlayerReady = false;
        localPlayerTransform = winnerTransform;

        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.RecordLevelCompletion(SceneManager.GetActiveScene().name, elapsedTimeSeconds, deaths);
        }

        CameraZoom2D camZoom = Camera.main != null ? Camera.main.GetComponent<CameraZoom2D>() : UnityEngine.Object.FindFirstObjectByType<CameraZoom2D>();
        if (camZoom != null && winnerTransform != null)
        {
            camZoom.StartZoom(winnerTransform);
        }

        GameObject winUI = GetWinPanelInScene();
        if (winUI != null)
        {
            winUI.SetActive(true);
            PopulateWinStatsUI(winUI, elapsedTimeSeconds, deaths);

            if (IsSinglePlayer)
            {
                UpdateReadyPromptText("PRESS 'R' OR [A] BUTTON TO LOAD NEXT LEVEL!");
            }
            else
            {
                UpdateReadyPromptText("PRESS 'P' OR [A] BUTTON TO SPECTATE OTHER PLAYER!");
            }
        }
    }

    public void ShowTimeIsLessPanelLocal(float elapsedTimeSeconds, int deaths)
    {
        GameObject tilUI = GetTimeIsLessPanelInScene();
        if (tilUI != null)
        {
            tilUI.SetActive(true);
            PopulateWinStatsUI(tilUI, elapsedTimeSeconds, deaths);
        }

        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.RecordLevelCompletion(SceneManager.GetActiveScene().name, elapsedTimeSeconds, deaths);
        }
    }

    private void PopulateWinStatsUI(GameObject targetUI, float elapsedTimeSeconds, int deaths)
    {
        if (targetUI == null) return;

        TimeSpan timeSpan = TimeSpan.FromSeconds(elapsedTimeSeconds);
        string formattedTime = string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);

        statsAnimTransforms.Clear();
        statsInitialScales.Clear();
        statsInitialRotations.Clear();

        TextMeshProUGUI[] textComponents = targetUI.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in textComponents)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("time") && !objName.Contains("over") && !objName.Contains("less"))
            {
                txt.text = $"Time: {formattedTime}";
                RegisterStatAnimTransform(txt.transform);
            }
            else if (objName.Contains("death"))
            {
                txt.text = $"Deaths: {deaths}";
                RegisterStatAnimTransform(txt.transform);
            }
        }

        Text[] legacyTexts = targetUI.GetComponentsInChildren<Text>(true);
        foreach (var txt in legacyTexts)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("time") && !objName.Contains("over") && !objName.Contains("less"))
            {
                txt.text = $"Time: {formattedTime}";
                RegisterStatAnimTransform(txt.transform);
            }
            else if (objName.Contains("death"))
            {
                txt.text = $"Deaths: {deaths}";
                RegisterStatAnimTransform(txt.transform);
            }
        }
    }

    private void RegisterStatAnimTransform(Transform t)
    {
        if (t == null || statsAnimTransforms.Contains(t)) return;

        statsAnimTransforms.Add(t);
        statsInitialScales[t] = t.localScale;
        statsInitialRotations[t] = t.localRotation;
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

    private void LoadNextLevelLocal()
    {
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            SceneManager.LoadScene("Ending");
        }
    }
}