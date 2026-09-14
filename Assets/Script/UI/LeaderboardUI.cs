using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;

public class LeaderboardUI : MonoBehaviour
{
    public static LeaderboardUI Instance { get; private set; }

    [Header("UI Canvas / Panel References")]
    [SerializeField] private GameObject leaderboardPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI currentGradeText;
    [SerializeField] private TextMeshProUGUI currentSummaryText;
    [SerializeField] private TextMeshProUGUI levelBreakdownText;
    [SerializeField] private TextMeshProUGUI topScoresText;

    [Header("Interactive Buttons")]
    [SerializeField] private Button quitButton;
    [SerializeField] private Button mainMenuButton;

    // Backward compatibility for existing scene Inspector reference
    [SerializeField, HideInInspector] private Button playAgainButton;

    [Header("Button Scale Animation & Navigation")]
    [SerializeField, Min(1f)] private float selectedScaleMultiplier = 1.12f;
    [SerializeField, Min(0.1f)] private float unselectedScaleMultiplier = 0.94f;
    [SerializeField, Min(0f)] private float scaleLerpSpeed = 12f;
    [SerializeField] private float moveRepeatDelay = 0.22f;

    [Header("Grade Badge Animation Settings")]
    [SerializeField] private float pulseSpeed = 3.5f;
    [SerializeField] private float pulseAmount = 0.12f;

    private Vector3 initialGradeScale = Vector3.one;
    private Vector3 quitBaseScale = Vector3.one;
    private Vector3 mainMenuBaseScale = Vector3.one;

    private int selectedIndex = 1; // 0 = Quit, 1 = Main Menu
    private float nextMoveTime = 0f;
    private EventSystem eventSystem;

    private void Awake()
    {
        Instance = this;
        eventSystem = EventSystem.current;
    }

    private void Start()
    {
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.EnsureAllLevelsRecorded();
            string savedName = PlayerPrefs.GetString("PlayerName", "Player");
            LeaderboardManager.Instance.SaveCurrentRun(savedName);
        }

        ulong localClientId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? NetworkManager.Singleton.LocalClientId : 0;
        if (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkCarController.LocalPlayerInstance.NotifyMatchEndedRpc(localClientId);
        }
        if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkRaceManager.Instance.NotifyPlayerReachedEndingRpc(localClientId);
        }

        SetupUIReferences();
        DisplayLeaderboard();
        SelectButton(1);
    }

    public void SetupUIReferences()
    {
        if (eventSystem == null) eventSystem = EventSystem.current;

        if (leaderboardPanel == null)
        {
            leaderboardPanel = gameObject;
        }

        // Auto-find TextMeshProUGUI components if not assigned in Inspector
        if (currentGradeText == null || currentSummaryText == null || levelBreakdownText == null || topScoresText == null || titleText == null)
        {
            TextMeshProUGUI[] foundTMPs = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var tmp in foundTMPs)
            {
                string objName = tmp.gameObject.name.ToLower();
                if ((objName.Contains("title") || objName.Contains("header")) && titleText == null)
                {
                    titleText = tmp;
                }
                else if ((objName.Contains("grade") || objName.Contains("rank")) && currentGradeText == null)
                {
                    currentGradeText = tmp;
                }
                else if ((objName.Contains("score") || objName.Contains("final") || objName.Contains("summary") || objName.Contains("time")) && !objName.Contains("top") && currentSummaryText == null)
                {
                    currentSummaryText = tmp;
                }
                else if ((objName.Contains("breakdown") || objName.Contains("stage") || objName.Contains("level")) && levelBreakdownText == null)
                {
                    levelBreakdownText = tmp;
                }
                else if ((objName.Contains("top") || objName.Contains("fame") || objName.Contains("hall") || objName.Contains("leaderboard")) && topScoresText == null)
                {
                    topScoresText = tmp;
                }
            }
        }

        if (currentGradeText != null)
        {
            currentGradeText.gameObject.SetActive(true);
            initialGradeScale = currentGradeText.transform.localScale;
            if (initialGradeScale.sqrMagnitude < 0.01f)
            {
                initialGradeScale = Vector3.one;
            }
        }
        if (currentSummaryText != null) currentSummaryText.gameObject.SetActive(true);
        if (levelBreakdownText != null) levelBreakdownText.gameObject.SetActive(true);
        if (topScoresText != null) topScoresText.gameObject.SetActive(false); // Hide Top Records from ending screen

        // Fallback for button references
        if (quitButton == null)
        {
            if (playAgainButton != null)
            {
                quitButton = playAgainButton;
            }
            else
            {
                Button[] foundButtons = GetComponentsInChildren<Button>(true);
                foreach (var btn in foundButtons)
                {
                    string bName = btn.gameObject.name.ToLower();
                    if ((bName.Contains("quit") || bName.Contains("exit") || bName.Contains("play") || bName.Contains("again")) && quitButton == null)
                    {
                        quitButton = btn;
                    }
                    else if ((bName.Contains("main") || bName.Contains("menu")) && mainMenuButton == null)
                    {
                        mainMenuButton = btn;
                    }
                }
            }
        }

        if (mainMenuButton == null)
        {
            Button[] foundButtons = GetComponentsInChildren<Button>(true);
            foreach (var btn in foundButtons)
            {
                string bName = btn.gameObject.name.ToLower();
                if ((bName.Contains("main") || bName.Contains("menu")) && mainMenuButton == null)
                {
                    mainMenuButton = btn;
                }
            }
        }

        // Configure Quit Button
        if (quitButton != null)
        {
            quitBaseScale = quitButton.transform.localScale;
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);

            Navigation nav = quitButton.navigation;
            nav.mode = Navigation.Mode.None;
            quitButton.navigation = nav;

            SetButtonText(quitButton, "QUIT GAME");
            AddPointerEnterCallback(quitButton.gameObject, 0);
        }

        // Configure Main Menu Button
        if (mainMenuButton != null)
        {
            mainMenuBaseScale = mainMenuButton.transform.localScale;
            mainMenuButton.onClick.RemoveAllListeners();
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);

            Navigation nav = mainMenuButton.navigation;
            nav.mode = Navigation.Mode.None;
            mainMenuButton.navigation = nav;

            SetButtonText(mainMenuButton, "MAIN MENU");
            AddPointerEnterCallback(mainMenuButton.gameObject, 1);
        }

        // Disable Left Stick move action on InputModule so UI navigation is D-Pad and Keyboard ONLY
        InputSystemUIInputModule uiInputModule = UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>();
        if (uiInputModule != null)
        {
            uiInputModule.move = null;
        }
    }

    private void SetButtonText(Button btn, string text)
    {
        if (btn == null) return;
        TextMeshProUGUI tmpText = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmpText != null)
        {
            tmpText.text = text;
            return;
        }
        Text legacyText = btn.GetComponentInChildren<Text>(true);
        if (legacyText != null)
        {
            legacyText.text = text;
        }
    }

    private void AddPointerEnterCallback(GameObject targetObj, int targetIndex)
    {
        EventTrigger trigger = targetObj.GetComponent<EventTrigger>();
        if (trigger == null) trigger = targetObj.AddComponent<EventTrigger>();

        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        entry.callback.AddListener((data) => { SelectButton(targetIndex); });
        trigger.triggers.Add(entry);
    }

    private void Update()
    {
        if (eventSystem == null) eventSystem = EventSystem.current;

        // Animate Rank Grade Badge Pulsing
        if (currentGradeText != null)
        {
            float pulse = 1f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
            currentGradeText.transform.localScale = initialGradeScale * pulse;
        }

        ReadNavigationInput();
        ReadSubmitInput();
        AnimateButtonScale();
    }

    private void ReadNavigationInput()
    {
        if (Time.unscaledTime < nextMoveTime) return;
        if (quitButton == null && mainMenuButton == null) return;

        bool moveTriggered = false;

        // 1. Keyboard Arrow Keys
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame ||
                Keyboard.current.downArrowKey.wasPressedThisFrame ||
                Keyboard.current.leftArrowKey.wasPressedThisFrame ||
                Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                moveTriggered = true;
            }
        }

        // 2. Gamepad D-Pad ONLY
        Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
        if (gamepad != null)
        {
            Vector2 dpadVec = gamepad.dpad.ReadValue();
            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame ||
                gamepad.dpad.left.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame ||
                Mathf.Abs(dpadVec.x) >= 0.4f || Mathf.Abs(dpadVec.y) >= 0.4f)
            {
                moveTriggered = true;
            }
        }

        if (moveTriggered)
        {
            selectedIndex = (selectedIndex == 0) ? 1 : 0;
            SelectButton(selectedIndex);
            nextMoveTime = Time.unscaledTime + moveRepeatDelay;
        }
    }

    public void SelectButton(int index)
    {
        selectedIndex = Mathf.Clamp(index, 0, 1);
        Button targetBtn = (selectedIndex == 0) ? quitButton : mainMenuButton;
        if (targetBtn == null) targetBtn = (selectedIndex == 0) ? mainMenuButton : quitButton;

        if (eventSystem != null && targetBtn != null)
        {
            eventSystem.SetSelectedGameObject(targetBtn.gameObject);
            targetBtn.Select();
        }
    }

    private void ReadSubmitInput()
    {
        bool submitPressed = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                submitPressed = true;
            }
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            submitPressed = true;
        }

        if (submitPressed)
        {
            if (selectedIndex == 0)
            {
                OnQuitClicked();
            }
            else
            {
                OnMainMenuClicked();
            }
        }
    }

    private void AnimateButtonScale()
    {
        GameObject selectedObj = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        float rotZ = Mathf.Sin(Time.unscaledTime * 4.0f) * 1.8f;

        if (quitButton != null)
        {
            bool isSelected = (selectedObj == quitButton.gameObject || selectedIndex == 0);
            Vector3 targetScale = isSelected
                ? quitBaseScale * selectedScaleMultiplier
                : quitBaseScale * unselectedScaleMultiplier;

            quitButton.transform.localScale = Vector3.Lerp(
                quitButton.transform.localScale,
                targetScale,
                Time.unscaledDeltaTime * scaleLerpSpeed
            );

            quitButton.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(quitButton.transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 10f);
        }

        if (mainMenuButton != null)
        {
            bool isSelected = (selectedObj == mainMenuButton.gameObject || selectedIndex == 1);
            Vector3 targetScale = isSelected
                ? mainMenuBaseScale * selectedScaleMultiplier
                : mainMenuBaseScale * unselectedScaleMultiplier;

            mainMenuButton.transform.localScale = Vector3.Lerp(
                mainMenuButton.transform.localScale,
                targetScale,
                Time.unscaledDeltaTime * scaleLerpSpeed
            );

            mainMenuButton.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(mainMenuButton.transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 10f);
        }
    }

    public void DisplayLeaderboard()
    {
        if (LeaderboardManager.Instance == null) return;

        float maxBudget = LeaderboardManager.Instance.GetOverallRunBudgetSeconds();
        float totalTime = Mathf.Clamp(LeaderboardManager.Instance.TotalRunTime, 0f, maxBudget);
        int totalDeaths = LeaderboardManager.Instance.TotalRunDeaths;
        int totalTimeouts = LeaderboardManager.Instance.TotalRunTimeouts;
        float score = LeaderboardManager.Instance.CalculatePerformanceScore(totalTime, totalDeaths, totalTimeouts);
        string grade = LeaderboardManager.Instance.CalculateGrade(totalTime, totalDeaths, totalTimeouts);

        TimeSpan totalSpan = TimeSpan.FromSeconds(totalTime);
        int displayMinutes = Mathf.Min(5, (int)totalSpan.TotalMinutes);
        int displaySeconds = totalSpan.Seconds;
        string formattedTotalTime = string.Format("{0:D2}:{1:D2}", displayMinutes, displaySeconds);

        if (titleText != null)
        {
            titleText.text = "<b><color=#00FFA3>PERFORMANCE</color> <color=#FFFFFF>SUMMARY</color></b>";
        }

        if (currentGradeText != null)
        {
            string gradeColor = grade switch
            {
                "S" => "#FFD700",
                "A" => "#00FFA3",
                "B" => "#00D2FF",
                "C" => "#FF9900",
                _   => "#FF4D6D"
            };

            string gradeComment = grade switch
            {
                "S" => "SUPREME DRIFTER",
                "A" => "MASTER DRIVER",
                "B" => "SOLID RUNNER",
                "C" => "SURVIVOR",
                _   => "NEEDS PRACTICE"
            };

            currentGradeText.text = $"<size=65%><color=#8E9BAE>OVERALL RATING</color></size>\n<size=180%><b><color={gradeColor}>RANK {grade}</color></b></size>\n<size=60%><color=#6B7C93>{gradeComment}</color></size>";
        }

        if (currentSummaryText != null)
        {
            string timeDisplayStr = (totalTimeouts > 0)
                ? $"<color=#FFFFFF>{formattedTotalTime} (TIMED OUT)</color>"
                : $"<color=#FFFFFF>{formattedTotalTime}</color>";

            currentSummaryText.text = $"<color=#8E9BAE>TOTAL TIME</color>  <b>{timeDisplayStr}</b>      <color=#415064>|</color>      <color=#8E9BAE>TOTAL DEATHS</color>  <b><color=#FFFFFF>{totalDeaths}</color></b>";
        }

        if (levelBreakdownText != null)
        {
            string breakdown = "<b><color=#8E9BAE>STAGE BREAKDOWN</color></b>\n\n";
            var stats = LeaderboardManager.Instance.LevelStats;
            if (stats != null && stats.Count > 0)
            {
                for (int i = 0; i < stats.Count; i++)
                {
                    var st = stats[i];
                    TimeSpan stSpan = TimeSpan.FromSeconds(st.timeSeconds);
                    string tStr = string.Format("{0:D2}:{1:D2}", stSpan.Minutes, stSpan.Seconds);
                    string statusTag = st.isTimeout
                        ? "<color=#FF4D6D>TIMEOUT</color>"
                        : "<color=#00FFA3>CLEARED</color>";

                    string displayName = !string.IsNullOrEmpty(st.levelName) ? st.levelName.ToUpper() : $"LEVEL {i + 1}";
                    string deathStr = (st.deaths == 0 || st.isTimeout) ? "<color=#00FFA3>0</color>" : $"<color=#FF6B6B>{st.deaths}</color>";

                    breakdown += $"<pos=0%><b><color=#FFFFFF>{displayName}</color></b><pos=26%><color=#CBD5E1>{tStr}</color><pos=50%><color=#8E9BAE>DEATHS:</color> {deathStr}<pos=76%>[{statusTag}]\n";
                }
            }
            else
            {
                breakdown += "<color=#6B7C93>No stage records available.</color>\n";
            }
            levelBreakdownText.text = breakdown;
        }

        if (topScoresText != null)
        {
            topScoresText.gameObject.SetActive(false);
        }
    }

    public void OnQuitClicked()
    {
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private bool isReturningToMenu = false;

    public void OnMainMenuClicked()
    {
        if (isReturningToMenu) return;
        isReturningToMenu = true;

        Time.timeScale = 1f;

        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.ResetRun();
        }
        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.ResetRunTimer();
        }

        try
        {
            if (RelayManager.Instance != null)
            {
                RelayManager.Instance.ShutdownSession();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LeaderboardUI] Relay shutdown: {ex.Message}");
        }

        try
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LeaderboardUI] NetworkManager shutdown: {ex.Message}");
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadSceneWithTransition("MainMenu");
        }
        else
        {
            SceneManager.LoadScene("MainMenu");
        }
    }
}
