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

    private void OnEnable()
    {
        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnPlayAgainCountChanged += HandlePlayAgainCountChanged;
        }
    }

    private void OnDisable()
    {
        if (NetworkRaceManager.Instance != null)
        {
            NetworkRaceManager.Instance.OnPlayAgainCountChanged -= HandlePlayAgainCountChanged;
        }
    }

    private void Start()
    {
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.EnsureAllLevelsRecorded();
            LeaderboardManager.Instance.SaveCurrentRun("Player 1");
        }

        ulong localClientId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? NetworkManager.Singleton.LocalClientId : 0;
        if (NetworkCarController.LocalPlayerInstance != null && NetworkCarController.LocalPlayerInstance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkCarController.LocalPlayerInstance.NotifyMatchEndedRpc(localClientId);
        }
        if (NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkRaceManager.Instance.NotifyMatchEndedRpc(localClientId);
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
        if (topScoresText != null) topScoresText.gameObject.SetActive(true);

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

        // 1. Keyboard Arrow Keys ONLY
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
        float rotZ = Mathf.Sin(Time.unscaledTime * 4.0f) * 2.0f;

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

            playAgainButton.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(playAgainButton.transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 10f);
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

        float totalTime = LeaderboardManager.Instance.TotalRunTime;
        int totalDeaths = LeaderboardManager.Instance.TotalRunDeaths;
        int totalTimeouts = LeaderboardManager.Instance.TotalRunTimeouts;
        float score = LeaderboardManager.Instance.CalculatePerformanceScore(totalTime, totalDeaths, totalTimeouts);
        string grade = LeaderboardManager.Instance.CalculateGrade(totalTime, totalDeaths, totalTimeouts);

        TimeSpan totalSpan = TimeSpan.FromSeconds(totalTime);
        string formattedTotalTime = string.Format("{0:D2}:{1:D2}", totalSpan.Minutes, totalSpan.Seconds);

        if (currentGradeText != null)
        {
            currentGradeText.text = $"RANK  [ {grade} ]";
            switch (grade)
            {
                case "S": currentGradeText.color = new Color(1.0f, 0.85f, 0.15f); break; // Radiant Gold
                case "A": currentGradeText.color = new Color(0.1f, 1.0f, 0.6f); break;   // Neon Mint
                case "B": currentGradeText.color = new Color(0.3f, 0.75f, 1.0f); break;  // Sapphire Cyan
                case "C": currentGradeText.color = new Color(1.0f, 0.6f, 0.2f); break;   // Vivid Orange
                default:  currentGradeText.color = new Color(0.9f, 0.35f, 0.35f); break; // Coral Crimson
            }
        }

        if (currentSummaryText != null)
        {
            currentSummaryText.text = $"TIME: {formattedTotalTime}    •    DEATHS: {totalDeaths}";
        }

        if (levelBreakdownText != null)
        {
            string breakdown = "<color=#FFD700>─── STAGE BREAKDOWN ───</color>\n";
            var stats = LeaderboardManager.Instance.LevelStats;
            if (stats != null && stats.Count > 0)
            {
                for (int i = 0; i < stats.Count; i++)
                {
                    var st = stats[i];
                    TimeSpan stSpan = TimeSpan.FromSeconds(st.timeSeconds);
                    string tStr = string.Format("{0:D2}:{1:D2}", stSpan.Minutes, stSpan.Seconds);
                    string statusTag = st.isTimeout
                        ? "<color=#FF8800>[TIMEOUT]</color>"
                        : "<color=#00FFA3>[CLEAR]</color>";

                    string displayName = !string.IsNullOrEmpty(st.levelName) ? st.levelName.ToUpper() : $"LEVEL {i + 1}";
                    breakdown += $"{displayName,-7}  {tStr}   {st.deaths} DEATHS   {statusTag}\n";
                }
            }
            else
            {
                breakdown += "<color=#888888>No level records available.</color>";
            }
            levelBreakdownText.text = breakdown;
        }

        if (topScoresText != null)
        {
            string topText = "<color=#FFD700>─── HALL OF FAME ───</color>\n";
            List<LeaderboardEntry> entries = LeaderboardManager.Instance.GetTopEntries();
            if (entries != null && entries.Count > 0)
            {
                int displayCount = Mathf.Min(5, entries.Count);
                for (int i = 0; i < displayCount; i++)
                {
                    var entry = entries[i];
                    TimeSpan eSpan = TimeSpan.FromSeconds(entry.totalTimeSeconds);
                    string eTimeStr = string.Format("{0:D2}:{1:D2}", eSpan.Minutes, eSpan.Seconds);

                    string medal = i switch
                    {
                        0 => "<color=#FFE84D>1ST</color>",
                        1 => "<color=#C0C0C0>2ND</color>",
                        2 => "<color=#CD7F32>3RD</color>",
                        _ => $"#{i + 1}"
                    };

                    string gradeColor = entry.grade switch
                    {
                        "S" => "#FFE84D",
                        "A" => "#00FFA3",
                        "B" => "#4DA6FF",
                        _ => "#E0E0E0"
                    };

                    topText += $"{medal,-4} <color={gradeColor}>[{entry.grade}]</color>  {eTimeStr}  •  {entry.totalDeaths} DEATHS\n";
                }
            }
            else
            {
                topText += "<color=#888888>No best runs recorded yet.</color>";
            }
            topScoresText.text = topText;
        }
    }

    private void HandlePlayAgainCountChanged(int readyCount, int totalCount)
    {
        if (playAgainButton != null)
        {
            TextMeshProUGUI btnText = playAgainButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (btnText != null)
            {
                btnText.text = $"READY ({readyCount}/{totalCount})...";
            }
            else
            {
                Text legacyText = playAgainButton.GetComponentInChildren<Text>(true);
                if (legacyText != null)
                {
                    legacyText.text = $"READY ({readyCount}/{totalCount})...";
                }
            }
        }
    }

    public void OnPlayAgainClicked()
    {
        bool isMultiplayer = Unity.Netcode.NetworkManager.Singleton != null &&
                             Unity.Netcode.NetworkManager.Singleton.IsListening &&
                             Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count > 1;

        if (isMultiplayer)
        {
            if (NetworkRaceManager.Instance != null && Unity.Netcode.NetworkManager.Singleton != null)
            {
                NetworkRaceManager.Instance.RequestPlayAgainServerRpc(Unity.Netcode.NetworkManager.Singleton.LocalClientId);

                if (playAgainButton != null)
                {
                    TextMeshProUGUI btnText = playAgainButton.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (btnText != null) btnText.text = "WAITING FOR OTHERS...";
                    Text legacyText = playAgainButton.GetComponentInChildren<Text>(true);
                    if (legacyText != null) legacyText.text = "WAITING FOR OTHERS...";
                }
            }
            return;
        }

        // Singleplayer:
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.ResetRun();
        }
        if (LevelTimer.Instance != null)
        {
            LevelTimer.Instance.ResetRunTimer();
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadSceneWithTransition("Level 1");
        }
        else
        {
            SceneManager.LoadScene("Level 1");
        }
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

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
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
