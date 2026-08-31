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
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Button Scale Animation & Navigation")]
    [SerializeField, Min(1f)] private float selectedScaleMultiplier = 1.12f;
    [SerializeField, Min(0.1f)] private float unselectedScaleMultiplier = 0.94f;
    [SerializeField, Min(0f)] private float scaleLerpSpeed = 12f;
    [SerializeField] private float moveRepeatDelay = 0.22f;

    [Header("Grade Badge Animation Settings")]
    [SerializeField] private float pulseSpeed = 4.0f;
    [SerializeField] private float pulseAmount = 0.15f;
    private Vector3 initialGradeScale = Vector3.one;

    private Vector3 playAgainBaseScale = Vector3.one;
    private Vector3 mainMenuBaseScale = Vector3.one;
    private int selectedIndex = 0; // 0 = Play Again, 1 = Main Menu
    private float nextMoveTime = 0f;
    private EventSystem eventSystem;

    private void Awake()
    {
        Instance = this;
        eventSystem = EventSystem.current;
    }

    private void Start()
    {
        // Auto-save current run when leaderboard loads
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.SaveCurrentRun("Player 1");
        }

        SetupUIReferences();
        DisplayLeaderboard();
        SelectButton(0);
    }

    public void SetupUIReferences()
    {
        if (eventSystem == null) eventSystem = EventSystem.current;

        if (leaderboardPanel == null)
        {
            leaderboardPanel = gameObject;
        }

        if (currentGradeText != null)
        {
            initialGradeScale = currentGradeText.transform.localScale;
        }

        // Auto-find buttons if not assigned
        if (playAgainButton == null || mainMenuButton == null)
        {
            Button[] foundButtons = GetComponentsInChildren<Button>(true);
            foreach (var btn in foundButtons)
            {
                string bName = btn.gameObject.name.ToLower();
                if ((bName.Contains("play") || bName.Contains("restart") || bName.Contains("again")) && playAgainButton == null)
                {
                    playAgainButton = btn;
                }
                else if ((bName.Contains("main") || bName.Contains("menu")) && mainMenuButton == null)
                {
                    mainMenuButton = btn;
                }
            }
        }

        if (playAgainButton != null)
        {
            playAgainBaseScale = playAgainButton.transform.localScale;
            playAgainButton.onClick.RemoveAllListeners();
            playAgainButton.onClick.AddListener(OnPlayAgainClicked);

            Navigation nav = playAgainButton.navigation;
            nav.mode = Navigation.Mode.None;
            playAgainButton.navigation = nav;

            AddPointerEnterCallback(playAgainButton.gameObject, 0);
        }

        if (mainMenuButton != null)
        {
            mainMenuBaseScale = mainMenuButton.transform.localScale;
            mainMenuButton.onClick.RemoveAllListeners();
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);

            Navigation nav = mainMenuButton.navigation;
            nav.mode = Navigation.Mode.None;
            mainMenuButton.navigation = nav;

            AddPointerEnterCallback(mainMenuButton.gameObject, 1);
        }

        // Disable Left Stick move action on InputModule so UI navigation is D-Pad and Keyboard ONLY
        InputSystemUIInputModule uiInputModule = UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>();
        if (uiInputModule != null)
        {
            uiInputModule.move = null;
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

        // Animate Rank Grade Badge
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
        if (playAgainButton == null && mainMenuButton == null) return;

        bool moveTriggered = false;

        // 1. Keyboard WASD + Arrow Keys
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame ||
                Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame ||
                Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame ||
                Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame)
            {
                moveTriggered = true;
            }
        }

        // 2. Gamepad D-Pad ONLY (Left Stick navigation disabled)
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
        Button targetBtn = (selectedIndex == 0) ? playAgainButton : mainMenuButton;
        if (targetBtn == null) targetBtn = (selectedIndex == 0) ? mainMenuButton : playAgainButton;

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
                OnPlayAgainClicked();
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

        if (playAgainButton != null)
        {
            bool isSelected = (selectedObj == playAgainButton.gameObject || selectedIndex == 0);
            Vector3 targetScale = isSelected
                ? playAgainBaseScale * selectedScaleMultiplier
                : playAgainBaseScale * unselectedScaleMultiplier;

            playAgainButton.transform.localScale = Vector3.Lerp(
                playAgainButton.transform.localScale,
                targetScale,
                Time.unscaledDeltaTime * scaleLerpSpeed
            );
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
        }
    }

    public void DisplayLeaderboard()
    {
        if (LeaderboardManager.Instance == null) return;

        float totalTime = LeaderboardManager.Instance.TotalRunTime;
        int totalDeaths = LeaderboardManager.Instance.TotalRunDeaths;
        string grade = LeaderboardManager.Instance.CalculateGrade(totalTime, totalDeaths);

        TimeSpan totalSpan = TimeSpan.FromSeconds(totalTime);
        string formattedTotalTime = string.Format("{0:D2}:{1:D2}", totalSpan.Minutes, totalSpan.Seconds);

        if (currentGradeText != null)
        {
            currentGradeText.text = $"RANK  [ {grade} ]";
            switch (grade)
            {
                case "S": currentGradeText.color = new Color(1f, 0.85f, 0.2f); break; // Warm Gold
                case "A": currentGradeText.color = new Color(0.3f, 0.9f, 0.5f); break; // Mint Green
                case "B": currentGradeText.color = new Color(0.3f, 0.7f, 1f); break; // Soft Blue
                default: currentGradeText.color = new Color(0.8f, 0.8f, 0.8f); break; // Muted Silver
            }
        }

        if (currentSummaryText != null)
        {
            currentSummaryText.text = $"TIME: {formattedTotalTime}    •    DEATHS: {totalDeaths}";
        }

        if (levelBreakdownText != null)
        {
            string breakdown = "<color=#FFD700>LEVELS</color>\n";
            var stats = LeaderboardManager.Instance.LevelStats;
            if (stats.Count > 0)
            {
                foreach (var st in stats)
                {
                    TimeSpan stSpan = TimeSpan.FromSeconds(st.timeSeconds);
                    string tStr = string.Format("{0:D2}:{1:D2}", stSpan.Minutes, stSpan.Seconds);
                    breakdown += $"{st.levelName}   {tStr}   ({st.deaths} deaths)\n";
                }
            }
            else
            {
                breakdown += "No levels completed yet.";
            }
            levelBreakdownText.text = breakdown;
        }

        if (topScoresText != null)
        {
            string topText = "<color=#FFD700>BEST RUNS</color>\n";
            List<LeaderboardEntry> entries = LeaderboardManager.Instance.GetTopEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                TimeSpan eSpan = TimeSpan.FromSeconds(entry.totalTimeSeconds);
                string eTimeStr = string.Format("{0:D2}:{1:D2}", eSpan.Minutes, eSpan.Seconds);

                string rankPrefix = (i == 0) ? "#1" : ((i == 1) ? "#2" : ((i == 2) ? "#3" : $"#{i + 1}"));
                topText += $"{rankPrefix}   [{entry.grade}]   {eTimeStr}   •   {entry.totalDeaths} Deaths\n";
            }

            if (entries.Count == 0)
            {
                topText += "No records yet.";
            }
            topScoresText.text = topText;
        }
    }

    public void OnPlayAgainClicked()
    {
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.ResetRun();
        }
        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.ResetRunTimer();
        }
        SceneManager.LoadScene("Level 1");
    }

    public void OnMainMenuClicked()
    {
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.ResetRun();
        }
        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.ResetRunTimer();
        }
        SceneManager.LoadScene("MainMenu");
    }
}
