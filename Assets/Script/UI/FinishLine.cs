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

    [Header("Celebration / Win Particle Effect (Inspector Assignment)")]
    [SerializeField] private GameObject winParticleEffect;
    [SerializeField] private float particleLifetime = 3.0f;

    [Header("Dynamic Win Prompt Text UI (Inspector References)")]
    [SerializeField] private TextMeshProUGUI winPromptTMP;
    [SerializeField] private Text winPromptLegacyText;

    [Header("Winning Stats Scale & Wobble Animation")]
    [SerializeField] private float statsPulseSpeed = 3.5f;
    [SerializeField] private float statsPulseAmount = 0.12f;
    [SerializeField] private float statsWobbleSpeed = 5.0f;
    [SerializeField] private float statsWobbleAngle = 4.0f;

    public static bool LocalPlayerHasWon { get; private set; } = false;
    public static FinishLine Instance { get; private set; }

    private bool hasWon = false;
    private bool isTransitioningNext = false;
    private Transform localPlayerTransform;

    private readonly List<Transform> statsAnimTransforms = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> statsInitialScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> statsInitialRotations = new Dictionary<Transform, Quaternion>();

    private void Awake()
    {
        Instance = this;
        CacheUIReferences();
    }

    private void Start()
    {
        Instance = this;
        hasWon = false;
        LocalPlayerHasWon = false;
        isTransitioningNext = false;

        CacheUIReferences();

        if (winPanel != null) winPanel.SetActive(false);
        if (timeIsLessPanel != null) timeIsLessPanel.SetActive(false);
    }

    private void CacheUIReferences()
    {
        if (winPanel == null || !winPanel.scene.isLoaded)
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Winning");
            if (tagged != null && tagged != gameObject && tagged.GetComponentInParent<Canvas>() != null)
            {
                winPanel = tagged;
            }
            else
            {
                Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var canvas in canvases)
                {
                    foreach (Transform child in canvas.transform)
                    {
                        string n = child.name.ToLower();
                        if (child.CompareTag("Winning") || n.Contains("win") || n.Contains("victory"))
                        {
                            winPanel = child.gameObject;
                            break;
                        }
                    }
                    if (winPanel != null) break;
                }
            }
        }

        if (timeIsLessPanel == null || !timeIsLessPanel.scene.isLoaded)
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("TimeIsLess");
            if (tagged != null && tagged != gameObject && tagged.GetComponentInParent<Canvas>() != null)
            {
                timeIsLessPanel = tagged;
            }
            else
            {
                Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var canvas in canvases)
                {
                    foreach (Transform child in canvas.transform)
                    {
                        string n = child.name.ToLower();
                        if (child.CompareTag("TimeIsLess") || n.Contains("timeisless"))
                        {
                            timeIsLessPanel = child.gameObject;
                            break;
                        }
                    }
                    if (timeIsLessPanel != null) break;
                }
            }
        }
    }

    private void OnEnable()
    {
        try
        {
            if (resetAction != null && !resetAction.enabled)
            {
                resetAction.Enable();
            }
        }
        catch { }
    }

    private void OnDisable()
    {
        try
        {
            if (resetAction != null && resetAction.enabled)
            {
                resetAction.Disable();
            }
        }
        catch { }

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

            bool proceedPressed = false;

            // 1. Keyboard (Space, R, Enter) - keep only Space and R as valid keys
            if (Keyboard.current != null)
            {
                if (Keyboard.current.spaceKey.wasPressedThisFrame ||
                    Keyboard.current.rKey.wasPressedThisFrame)
                {
                    proceedPressed = true;
                }
            }

            // 2. Gamepad (buttonSouth only)
            Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
            if (!proceedPressed && gamepad != null)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame)
                {
                    proceedPressed = true;
                }
            }

            // 3. Input Action (if configured)
            if (!proceedPressed && resetAction != null)
            {
                try
                {
                    if (resetAction.WasPressedThisFrame()) proceedPressed = true;
                }
                catch { }
            }

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

        for (int i = 0; i < statsAnimTransforms.Count; i++)
        {
            Transform t = statsAnimTransforms[i];
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
        CarControllerSingle singleCtrl = col.GetComponentInParent<CarControllerSingle>();
        CarHealth healthComp = col.GetComponentInParent<CarHealth>();

        if (col.CompareTag("Player") || (col.transform.root != null && col.transform.root.CompareTag("Player")) || carCtrl != null || singleCtrl != null)
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

            if (singleCtrl != null)
            {
                singleCtrl.SetCarWon();
            }

            int deaths = healthComp != null ? healthComp.deathCount.Value : (CarHealth.LocalPlayerHealth != null ? CarHealth.LocalPlayerHealth.deathCount.Value : 0);
            float elapsedTime = LevelTimer.Instance != null ? LevelTimer.Instance.GetCurrentLevelElapsedTime() : 0f;
            Transform playerT = carCtrl != null ? carCtrl.transform : (singleCtrl != null ? singleCtrl.transform : (netObj != null ? netObj.transform : col.transform.root));

            if (!hasWon)
            {
                if (LevelTimer.Instance != null)
                {
                    LevelTimer.Instance.StopLocalTimerForPlayer(elapsedTime);
                }
                TriggerWinLocal(playerT, elapsedTime, deaths);
            }
        }
    }

    public GameObject GetWinPanelInScene()
    {
        if (winPanel != null && winPanel.scene.isLoaded)
        {
            return winPanel;
        }

        CacheUIReferences();
        return winPanel;
    }

    public GameObject GetTimeIsLessPanelInScene()
    {
        if (timeIsLessPanel != null && timeIsLessPanel.scene.isLoaded)
        {
            return timeIsLessPanel;
        }

        CacheUIReferences();
        return timeIsLessPanel;
    }

    public static void PlayWinParticlesGlobal(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.PlayWinParticles(position);
        }
    }

    public void PlayWinParticles(Vector3 fallbackPosition)
    {
        if (winParticleEffect == null)
        {
            winParticleEffect = Resources.Load<GameObject>("WinPop");
            if (winParticleEffect == null)
            {
                winParticleEffect = Resources.Load<GameObject>("Prefabs/WinPop");
            }
        }

        if (winParticleEffect != null)
        {
            Vector3 finishLineSpawnPos = new Vector3(transform.position.x, transform.position.y, -1.0f);
            Quaternion spawnRot = winParticleEffect.transform.rotation;

            GameObject spawnedFX = Instantiate(winParticleEffect, finishLineSpawnPos, spawnRot);
            spawnedFX.SetActive(true);

            ParticleSystem[] pss = spawnedFX.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in pss)
            {
                ps.gameObject.SetActive(true);
                ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    rend.sortingOrder = 300;
                }
                ps.Clear();
                ps.Play(true);
            }
            Destroy(spawnedFX, particleLifetime);
        }
    }

    private void TriggerWinLocal(Transform winnerTransform, float elapsedTimeSeconds, int deaths)
    {
        hasWon = true;
        LocalPlayerHasWon = true;
        isTransitioningNext = false;
        localPlayerTransform = winnerTransform;

        // Play Win Particle System at Finish Line place
        PlayWinParticles(transform.position);

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

            // Instant zero-delay visual display
            CanvasGroup cg = winUI.GetComponent<CanvasGroup>() ?? winUI.GetComponentInChildren<CanvasGroup>(true);
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            // Immediately snap any sliding background rects to on-screen center
            RectTransform[] rects = winUI.GetComponentsInChildren<RectTransform>(true);
            foreach (var r in rects)
            {
                if (r != null && r.name.ToLower().Contains("background"))
                {
                    Vector2 pos = r.anchoredPosition;
                    pos.x = 0f;
                    r.anchoredPosition = pos;
                }
            }

            Animator anim = winUI.GetComponent<Animator>() ?? winUI.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.enabled = false; // Disable animator to prevent 0.5s slide lag
            }

            PopulateWinStatsUI(winUI, elapsedTimeSeconds, deaths);
            UpdateReadyPromptText("PRESS [SPACE] / [R] / (A) / CLICK FOR NEXT LEVEL!");

            Button[] buttons = winUI.GetComponentsInChildren<Button>(true);
            foreach (var nextBtn in buttons)
            {
                if (nextBtn != null)
                {
                    nextBtn.onClick.RemoveListener(LoadNextLevelLocal);
                    nextBtn.onClick.AddListener(LoadNextLevelLocal);
                }
            }

            // Also attach a click listener to winUI itself so clicking anywhere on the screen triggers next level
            Button panelBtn = winUI.GetComponent<Button>();
            if (panelBtn == null)
            {
                panelBtn = winUI.AddComponent<Button>();
            }
            if (panelBtn != null)
            {
                panelBtn.onClick.RemoveListener(LoadNextLevelLocal);
                panelBtn.onClick.AddListener(LoadNextLevelLocal);
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

        Time.timeScale = 1.0f;

        string currentScene = SceneManager.GetActiveScene().name;
        string nextScene = GetNextSceneName(currentScene);

        Debug.Log($"[FinishLine] Proceeding to next level: from '{currentScene}' to '{nextScene}'");

        StartCoroutine(TransitionWatchdogRoutine(nextScene));

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (nextScene.Equals("Ending", StringComparison.OrdinalIgnoreCase))
            {
                if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsSpawned)
                {
                    NetworkRaceManager.Instance.NotifyPlayerReachedEndingRpc(localId);
                }
            }

            if (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.IsSpawned)
            {
                NetworkCarController.LocalPlayerInstance.UpdateCurrentSceneServerRpc(nextScene);
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

    private IEnumerator TransitionWatchdogRoutine(string targetScene)
    {
        yield return new WaitForSecondsRealtime(1.2f);
        if (SceneManager.GetActiveScene().name.Equals(targetScene, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        // If still on the same scene after timeout, force load directly
        Debug.LogWarning($"[FinishLine] Transition watchdog triggered for '{targetScene}' -- forcing direct load.");
        isTransitioningNext = false;
        SceneManager.LoadScene(targetScene);
    }

    public string GetNextSceneName(string currentSceneName)
    {
        return NetworkRaceManager.GetNextSceneName(currentSceneName);
    }
}