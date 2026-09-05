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

    [Header("Dynamic Win Prompt Text UI (Inspector References)")]
    [SerializeField] private TextMeshProUGUI winPromptTMP;
    [SerializeField] private Text winPromptLegacyText;

    [Header("Winning Stats Scale & Wobble Animation")]
    [SerializeField] private float statsPulseSpeed = 3.5f;
    [SerializeField] private float statsPulseAmount = 0.12f;
    [SerializeField] private float statsWobbleSpeed = 5.0f;
    [SerializeField] private float statsWobbleAngle = 4.0f;

    public static bool LocalPlayerHasWon { get; private set; } = false;

    private bool hasWon = false;
    private bool isTransitioningNext = false;
    private Transform localPlayerTransform;

    private readonly List<Transform> statsAnimTransforms = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> statsInitialScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> statsInitialRotations = new Dictionary<Transform, Quaternion>();

    private void Start()
    {
        hasWon = false;
        LocalPlayerHasWon = false;
        isTransitioningNext = false;

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
    }

    private void OnDisable()
    {
        resetAction.Disable();

        if (!hasWon && LeaderboardManager.Instance != null)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (!sceneName.Equals("Ending", StringComparison.OrdinalIgnoreCase) && !sceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                bool isRecorded = false;
                if (LeaderboardManager.Instance.LevelStats != null)
                {
                    foreach (var stat in LeaderboardManager.Instance.LevelStats)
                    {
                        if (string.Equals(stat.levelName, sceneName, StringComparison.OrdinalIgnoreCase))
                        {
                            isRecorded = true;
                            break;
                        }
                    }
                }

                if (!isRecorded)
                {
                    float elapsedTime = LevelTimer.Instance != null ? LevelTimer.Instance.GetCurrentLevelElapsedTime() : 40f;
                    int deaths = CarHealth.LocalPlayerHealth != null ? CarHealth.LocalPlayerHealth.deathCount.Value : 0;
                    LeaderboardManager.Instance.RecordLevelCompletion(sceneName, elapsedTime, deaths, isTimeout: true);
                }
            }
        }
    }

    private void Update()
    {
        if (hasWon && !isTransitioningNext)
        {
            AnimateWinStatsUI();

            Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);

            bool proceedPressed = (Keyboard.current != null && (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) ||
                                 (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame) ||
                                 resetAction.WasPressedThisFrame();

            if (proceedPressed)
            {
                LoadNextLevelLocal();
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

    private void OnTriggerEnter2D(Collider2D col)
    {
        NetworkObject netObj = col.GetComponentInParent<NetworkObject>();
        NetworkCarController carCtrl = col.GetComponentInParent<NetworkCarController>();
        CarHealth healthComp = col.GetComponentInParent<CarHealth>();

        if (col.CompareTag("Player") || (col.transform.root != null && col.transform.root.CompareTag("Player")) || carCtrl != null)
        {
            bool isLocalCar = (netObj != null && netObj.IsOwner) || (netObj == null);
            if (!isLocalCar) return;

            if (carCtrl != null)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && carCtrl.IsSpawned)
                {
                    carCtrl.SetCarWonRpc();
                }
                else
                {
                    carCtrl.SetCarWonLocal();
                }
            }

            int deaths = healthComp != null ? healthComp.deathCount.Value : (CarHealth.LocalPlayerHealth != null ? CarHealth.LocalPlayerHealth.deathCount.Value : 0);
            float elapsedTime = LevelTimer.Instance != null ? LevelTimer.Instance.GetCurrentLevelElapsedTime() : 0f;
            Transform playerT = carCtrl != null ? carCtrl.transform : (netObj != null ? netObj.transform : col.transform.root);

            if (!hasWon)
            {
                if (LevelTimer.Instance != null)
                {
                    LevelTimer.Instance.StopLocalTimerForPlayer(elapsedTime);
                }
                TriggerWinLocal(playerT, elapsedTime, deaths);

                string currentScene = SceneManager.GetActiveScene().name;
                string nextScene = GetNextSceneName(currentScene);
                if (nextScene.Equals("Ending", StringComparison.OrdinalIgnoreCase))
                {
                    ulong localId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? NetworkManager.Singleton.LocalClientId : 0;
                    if (carCtrl != null && carCtrl.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    {
                        carCtrl.NotifyMatchEndedRpc(localId);
                    }
                    if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    {
                        NetworkRaceManager.Instance.NotifyMatchEndedRpc(localId);
                    }
                }
            }
        }
    }

    public GameObject GetWinPanelInScene()
    {
        if (winPanel != null && winPanel.scene.isLoaded) return winPanel;

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene.isLoaded &&
                (go.CompareTag("Winning") ||
                 go.CompareTag("Win") ||
                 go.name.Equals("WinningScene", StringComparison.OrdinalIgnoreCase) ||
                 go.name.Equals("WinPanel", StringComparison.OrdinalIgnoreCase)))
            {
                winPanel = go;
                return go;
            }
        }

        GameObject tagged = GameObject.FindGameObjectWithTag("Winning");
        if (tagged != null)
        {
            winPanel = tagged;
            return tagged;
        }

        tagged = GameObject.FindGameObjectWithTag("Win");
        if (tagged != null)
        {
            winPanel = tagged;
            return tagged;
        }

        return null;
    }

    public GameObject GetTimeIsLessPanelInScene()
    {
        if (timeIsLessPanel != null && timeIsLessPanel.scene.isLoaded) return timeIsLessPanel;

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.scene.isLoaded && (go.CompareTag("TimeIsLess") || go.name.Equals("TimeIsLessPanel", StringComparison.OrdinalIgnoreCase) || go.name.Contains("TimeIsLess", StringComparison.OrdinalIgnoreCase)))
            {
                timeIsLessPanel = go;
                return go;
            }
        }

        return null;
    }

    private void TriggerWinLocal(Transform winnerTransform, float elapsedTimeSeconds, int deaths)
    {
        hasWon = true;
        LocalPlayerHasWon = true;
        isTransitioningNext = false;
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
            UpdateReadyPromptText("PRESS 'R' OR [A] BUTTON TO LOAD NEXT LEVEL!");

            Button nextBtn = winUI.GetComponentInChildren<Button>(true);
            if (nextBtn != null)
            {
                nextBtn.onClick.RemoveAllListeners();
                nextBtn.onClick.AddListener(LoadNextLevelLocal);
            }
        }

        string currentScene = SceneManager.GetActiveScene().name;
        string nextScene = GetNextSceneName(currentScene);
        if (nextScene.Equals("Ending", StringComparison.OrdinalIgnoreCase))
        {
            StartCoroutine(DelayedAutoLoadEndingRoutine(2.5f));
        }
    }

    private IEnumerator DelayedAutoLoadEndingRoutine(float delay = 2.5f)
    {
        yield return new WaitForSeconds(delay);
        LoadNextLevelLocal();
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
            LeaderboardManager.Instance.RecordLevelCompletion(SceneManager.GetActiveScene().name, elapsedTimeSeconds, deaths, isTimeout: true);
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
        if (winPromptTMP != null)
        {
            winPromptTMP.text = promptText;
        }

        if (winPromptLegacyText != null)
        {
            winPromptLegacyText.text = promptText;
        }

        GameObject winUI = GetWinPanelInScene();
        if (winUI == null) return;

        TextMeshProUGUI[] textComponents = winUI.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in textComponents)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("ready") || objName.Contains("prompt") || objName.Contains("info") ||
                objName.Contains("next") || objName.Contains("sub") ||
                objName.Contains("guide") || objName.Contains("hint") || objName.Contains("continue") ||
                objName.Contains("desc"))
            {
                txt.text = promptText;
            }
        }

        Text[] legacyTexts = winUI.GetComponentsInChildren<Text>(true);
        foreach (var txt in legacyTexts)
        {
            string objName = txt.gameObject.name.ToLower();
            if (objName.Contains("ready") || objName.Contains("prompt") || objName.Contains("info") ||
                objName.Contains("next") || objName.Contains("sub") ||
                objName.Contains("guide") || objName.Contains("hint") || objName.Contains("continue") ||
                objName.Contains("desc"))
            {
                txt.text = promptText;
            }
        }
    }

    public void LoadNextLevelLocal()
    {
        if (isTransitioningNext) return;
        isTransitioningNext = true;
        StopAllCoroutines();

        string currentScene = SceneManager.GetActiveScene().name;
        string nextScene = GetNextSceneName(currentScene);

        // If reaching Ending in multiplayer, notify other clients
        if (nextScene.Equals("Ending", StringComparison.OrdinalIgnoreCase))
        {
            ulong localId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? NetworkManager.Singleton.LocalClientId : 0;
            if (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkCarController.LocalPlayerInstance.NotifyMatchEndedRpc(localId);
            }
            if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkRaceManager.Instance.NotifyMatchEndedRpc(localId);
            }
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadSceneWithTransition(nextScene);
        }
        else
        {
            SceneManager.LoadScene(nextScene);
        }
    }

    private string GetNextSceneName(string currentSceneName)
    {
        if (currentSceneName.Equals("Level 1", StringComparison.OrdinalIgnoreCase)) return "Level 2";
        if (currentSceneName.Equals("Level 2", StringComparison.OrdinalIgnoreCase)) return "Level 3";
        if (currentSceneName.Equals("Level 3", StringComparison.OrdinalIgnoreCase)) return "Level 4";
        if (currentSceneName.Equals("Level 4", StringComparison.OrdinalIgnoreCase)) return "Ending";

        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(nextSceneIndex);
            return System.IO.Path.GetFileNameWithoutExtension(scenePath);
        }
        return "Ending";
    }
}