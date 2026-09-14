using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class JustAButton : MonoBehaviour
{
    public static JustAButton Instance { get; private set; }

    [Header("Buttons")]
    [SerializeField] private List<Button> buttons = new List<Button>();
    [SerializeField] private Button hostGameButton;
    [SerializeField] private Button joinGameButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button leaderboardButton;
    [SerializeField] private int firstSelectedIndex = 0;
    [SerializeField] private bool wrapSelection = true;

    [Header("Animator")]
    [SerializeField] private Animator anim;
    [SerializeField] private Animator carAnimator;
    [SerializeField] private Animator panelAnim;
    [SerializeField] private string selectedBoolPrefix = "isSelected";
    [SerializeField] private AnimationClip playAnimation;
    [SerializeField] private AnimationClip stopAnimation;

    [Header("Input")]
    [SerializeField] private float moveRepeatDelay = 0.25f;
    [SerializeField] private float gamepadDeadzone = 0.5f;
    [SerializeField] private InputSystemUIInputModule uiInputModule;

    [Header("Options Menu & Volume Sliders")]
    [SerializeField] private GameObject OptionMenu;
    [SerializeField] private Button optionBackButton;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;

    [Header("Player Name Modal & Global Leaderboard")]
    [SerializeField] private GameObject nameInputModal;
    [SerializeField] private TMPro.TMP_InputField nameInputField;
    [SerializeField] private Button nameConfirmButton;
    [SerializeField] private Button nameCancelButton;
    [SerializeField] private GameObject globalLeaderboardModal;
    [SerializeField] private TMPro.TextMeshProUGUI globalLeaderboardText;
    [SerializeField] private Button globalLeaderboardBackButton;

    private int selectedIndex = 0;
    private int optionsFocusIndex = 0; // 0 = Music, 1 = SFX, 2 = Back Button
    private float nextMoveTime;
    private float nextOptionsMoveTime;
    private bool isBusy;
    private bool isOptionMenuOpen;
    private bool isNameModalOpen;
    private bool isLeaderboardModalOpen;
    private System.Action pendingNameAction;

    private readonly Dictionary<Transform, Vector3> initialButtonScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Coroutine> activePunchCoroutines = new Dictionary<Transform, Coroutine>();

    private void Awake()
    {
        Instance = this;
        DisableUIControllerSubmit();
        CacheInitialButtonScales();
    }

    private void CacheInitialButtonScales()
    {
        initialButtonScales.Clear();
        foreach (Button b in buttons)
        {
            if (b != null && !initialButtonScales.ContainsKey(b.transform))
            {
                initialButtonScales[b.transform] = b.transform.localScale;
            }
        }

        if (hostGameButton != null && !initialButtonScales.ContainsKey(hostGameButton.transform))
            initialButtonScales[hostGameButton.transform] = hostGameButton.transform.localScale;

        if (joinGameButton != null && !initialButtonScales.ContainsKey(joinGameButton.transform))
            initialButtonScales[joinGameButton.transform] = joinGameButton.transform.localScale;

        if (optionsButton != null && !initialButtonScales.ContainsKey(optionsButton.transform))
            initialButtonScales[optionsButton.transform] = optionsButton.transform.localScale;

        if (optionBackButton != null && !initialButtonScales.ContainsKey(optionBackButton.transform))
            initialButtonScales[optionBackButton.transform] = optionBackButton.transform.localScale;
    }

    private void OnEnable()
    {
        isOptionMenuOpen = false;
        isBusy = false;
        optionsFocusIndex = 0;

        if (OptionMenu != null)
        {
            OptionMenu.SetActive(false);
        }

        EnableMainButtons();

        selectedIndex = Mathf.Clamp(firstSelectedIndex, 0, Mathf.Max(0, buttons.Count - 1));
        SelectButton(selectedIndex);

        SetupVolumeSliders();
    }

    private void SetupVolumeSliders()
    {
        if (OptionMenu != null)
        {
            Slider[] foundSliders = OptionMenu.GetComponentsInChildren<Slider>(true);
            if (foundSliders != null)
            {
                foreach (Slider s in foundSliders)
                {
                    string sName = s.gameObject.name.ToLower();
                    if ((sName.Contains("sfx") || sName.Contains("sound") || sName.Contains("effect")) && sfxVolumeSlider == null)
                    {
                        sfxVolumeSlider = s;
                    }
                    else if ((sName.Contains("music") || sName.Contains("bgm")) && musicVolumeSlider == null)
                    {
                        musicVolumeSlider = s;
                    }
                }

                if (musicVolumeSlider == null && foundSliders.Length > 0)
                {
                    musicVolumeSlider = foundSliders[0];
                }
                if (sfxVolumeSlider == null && foundSliders.Length > 1)
                {
                    sfxVolumeSlider = foundSliders[1];
                }
            }
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.RemoveAllListeners();
            if (AudioManager.Instance != null)
            {
                musicVolumeSlider.value = AudioManager.Instance.GetMusicVolume();
            }
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.RemoveAllListeners();
            if (AudioManager.Instance != null)
            {
                sfxVolumeSlider.value = AudioManager.Instance.GetSfxVolume();
            }
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        }
    }

    private void OnMusicVolumeChanged(float val)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMusicVolume(val);
        }
    }

    private void OnSfxVolumeChanged(float val)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSfxVolume(val);
        }
    }

    private void Start()
    {
        selectedIndex = Mathf.Clamp(firstSelectedIndex, 0, Mathf.Max(0, buttons.Count - 1));
        SelectButton(selectedIndex);
    }

    private void Update()
    {
        if (isBusy)
        {
            return;
        }

        if (isNameModalOpen)
        {
            ReadNameModalInput();
            return;
        }

        if (isLeaderboardModalOpen)
        {
            ReadLeaderboardModalInput();
            return;
        }

        if (isOptionMenuOpen)
        {
            ReadOptionsInput();
            return;
        }

        if (Keyboard.current != null && (Keyboard.current.lKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
        {
            OpenGlobalLeaderboard();
            return;
        }

        if (buttons.Count == 0)
        {
            return;
        }

        ReadSelectionInput();
        ReadSubmitInput();
    }

    public void HostGame()
    {
        if (hostGameButton != null) AnimateButtonPress(hostGameButton);
        PromptNameModal(() =>
        {
            if (LobbyUI.Instance != null)
            {
                LobbyUI.Instance.CreateRoom();
            }
        });
    }

    public void JoinGame()
    {
        if (joinGameButton != null) AnimateButtonPress(joinGameButton);
        PromptNameModal(() =>
        {
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.TriggerTransition(() =>
                {
                    if (LobbyUI.Instance != null) LobbyUI.Instance.OpenJoinUI();
                });
            }
            else if (LobbyUI.Instance != null)
            {
                LobbyUI.Instance.OpenJoinUI();
            }
        });
    }

    public void PromptNameModal(System.Action onConfirmed)
    {
        pendingNameAction = onConfirmed;
        isNameModalOpen = true;
        DisableMainButtons();
        EnsureNameModalBuilt();

        if (nameInputModal != null)
        {
            nameInputModal.SetActive(true);
        }

        if (nameInputField != null)
        {
            nameInputField.text = PlayerPrefs.GetString("PlayerName", "Player");
            nameInputField.Select();
            nameInputField.ActivateInputField();
        }

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(true);
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    public void OnNameConfirmClicked()
    {
        string name = nameInputField != null ? nameInputField.text.Trim() : "Player";
        if (string.IsNullOrEmpty(name)) name = "Player";
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();

        CloseNameModal();
        var act = pendingNameAction;
        pendingNameAction = null;
        act?.Invoke();
    }

    public void OnNameCancelClicked()
    {
        CloseNameModal();
        pendingNameAction = null;
        EnableMainButtons();
        SelectButton(selectedIndex);
    }

    public void CloseNameModal()
    {
        isNameModalOpen = false;
        if (nameInputModal != null)
        {
            nameInputModal.SetActive(false);
        }

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(false);
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    private void ReadNameModalInput()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                OnNameConfirmClicked();
                return;
            }
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                OnNameCancelClicked();
                return;
            }
        }

        Gamepad gamepad = GetGamepad();
        if (gamepad != null)
        {
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                OnNameConfirmClicked();
                return;
            }
            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                OnNameCancelClicked();
                return;
            }
        }
    }

    public void OpenGlobalLeaderboard()
    {
        if (isBusy || isOptionMenuOpen || isNameModalOpen) return;

        isLeaderboardModalOpen = true;
        DisableMainButtons();
        EnsureLeaderboardModalBuilt();

        if (globalLeaderboardModal != null)
        {
            globalLeaderboardModal.SetActive(true);
        }

        PopulateGlobalLeaderboardUI();

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(true);
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (globalLeaderboardBackButton != null)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(globalLeaderboardBackButton.gameObject);
            }
            globalLeaderboardBackButton.Select();
        }
    }

    public void CloseGlobalLeaderboard()
    {
        isLeaderboardModalOpen = false;
        if (globalLeaderboardModal != null)
        {
            globalLeaderboardModal.SetActive(false);
        }

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(false);
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        EnableMainButtons();
        SelectButton(selectedIndex);
    }

    private void ReadLeaderboardModalInput()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                CloseGlobalLeaderboard();
                return;
            }
        }

        Gamepad gamepad = GetGamepad();
        if (gamepad != null)
        {
            if (gamepad.buttonEast.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame)
            {
                CloseGlobalLeaderboard();
                return;
            }
        }
    }

    private void PopulateGlobalLeaderboardUI()
    {
        if (globalLeaderboardText == null) return;

        globalLeaderboardText.alignment = TMPro.TextAlignmentOptions.TopLeft;

        if (LeaderboardManager.Instance == null)
        {
            globalLeaderboardText.text = "<color=#6B7C93>Leaderboard unavailable.</color>";
            return;
        }

        List<LeaderboardEntry> entries = LeaderboardManager.Instance.GetTopEntries();
        if (entries == null || entries.Count == 0)
        {
            globalLeaderboardText.alignment = TMPro.TextAlignmentOptions.Center;
            globalLeaderboardText.text = "<size=110%><color=#8E9BAE>NO CLEAN RUNS REGISTERED YET</color></size>\n\n<size=85%><color=#6B7C93>Clear all stages without timing out to qualify for the Global Leaderboard!</color></size>";
            return;
        }

        string table = "<size=105%><b><color=#8E9BAE>" +
                       "<pos=15%>RANK" +
                       "<pos=27%>DRIVER" +
                       "<pos=53%>TIME" +
                       "<pos=66.5%>DEATHS" +
                       "<pos=79.5%>GRADE" +
                       "</color></b></size>\n\n";

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            System.TimeSpan tSpan = System.TimeSpan.FromSeconds(e.totalTimeSeconds);
            string tStr = string.Format("{0:D2}:{1:D2}", tSpan.Minutes, tSpan.Seconds);
            string rankMedal = i switch
            {
                0 => "<color=#FFD700>#1</color>",
                1 => "<color=#E2E8F0>#2</color>",
                2 => "<color=#CD7F32>#3</color>",
                _ => $"<color=#8E9BAE>#{(i + 1)}</color>"
            };
            string gradeColor = e.grade switch
            {
                "S" => "#FFD700",
                "A" => "#00FFA3",
                "B" => "#00D2FF",
                "C" => "#FF9900",
                _   => "#FF4D6D"
            };
            string deathColor = e.totalDeaths == 0 ? "#00FFA3" : "#FF6B6B";
            string nameTruncated = (e.playerName.Length > 16) ? e.playerName.Substring(0, 16) : e.playerName;

            string deathsStr = e.totalDeaths.ToString();
            string deathPos = (deathsStr.Length > 1) ? "<pos=68.6%>" : "<pos=69.3%>";

            table += $"<pos=15%><b>{rankMedal}</b>" +
                     $"<pos=27%><color=#FFFFFF>{nameTruncated}</color>" +
                     $"<pos=53%><b><color=#00FFA3>{tStr}</color></b>" +
                     $"{deathPos}<color={deathColor}>{deathsStr}</color>" +
                     $"<pos=81.2%><color={gradeColor}>[{e.grade}]</color>\n\n";
        }
        globalLeaderboardText.text = table;
    }

    private void EnsureNameModalBuilt()
    {
        if (nameInputModal != null) return;

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject overlay = new GameObject("NameInputModalOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        RectTransform rt = overlay.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = overlay.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.8f);

        GameObject box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(overlay.transform, false);
        RectTransform boxRt = box.GetComponent<RectTransform>();
        boxRt.sizeDelta = new Vector2(520f, 260f);
        Image boxImg = box.GetComponent<Image>();
        boxImg.color = new Color(0.09f, 0.11f, 0.14f, 0.98f);

        // Title
        GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        titleObj.transform.SetParent(box.transform, false);
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0f, 80f);
        titleRt.sizeDelta = new Vector2(480f, 40f);
        TMPro.TextMeshProUGUI titleTmp = titleObj.GetComponent<TMPro.TextMeshProUGUI>();
        titleTmp.text = "<b><color=#00FFA3>ENTER YOUR</color> <color=#FFFFFF>NAME</color></b>";
        titleTmp.fontSize = 24;
        titleTmp.alignment = TMPro.TextAlignmentOptions.Center;

        // Subtitle
        GameObject subObj = new GameObject("Subtitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        subObj.transform.SetParent(box.transform, false);
        RectTransform subRt = subObj.GetComponent<RectTransform>();
        subRt.anchoredPosition = new Vector2(0f, 48f);
        subRt.sizeDelta = new Vector2(480f, 30f);
        TMPro.TextMeshProUGUI subTmp = subObj.GetComponent<TMPro.TextMeshProUGUI>();
        subTmp.text = "<color=#8E9BAE>Name will be recorded on the Global Leaderboard</color>";
        subTmp.fontSize = 14;
        subTmp.alignment = TMPro.TextAlignmentOptions.Center;

        // InputField Box
        GameObject inputObj = new GameObject("InputField", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMPro.TMP_InputField));
        inputObj.transform.SetParent(box.transform, false);
        RectTransform inputRt = inputObj.GetComponent<RectTransform>();
        inputRt.anchoredPosition = new Vector2(0f, 0f);
        inputRt.sizeDelta = new Vector2(360f, 45f);
        Image inputImg = inputObj.GetComponent<Image>();
        inputImg.color = new Color(0.05f, 0.07f, 0.09f, 1f);
        nameInputField = inputObj.GetComponent<TMPro.TMP_InputField>();

        // Text Component
        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        textObj.transform.SetParent(inputObj.transform, false);
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10f, 0f);
        textRt.offsetMax = new Vector2(-10f, 0f);
        TMPro.TextMeshProUGUI tmp = textObj.GetComponent<TMPro.TextMeshProUGUI>();
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        nameInputField.textComponent = tmp;

        // Confirm Button
        GameObject confirmBtnObj = new GameObject("ConfirmBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        confirmBtnObj.transform.SetParent(box.transform, false);
        RectTransform confirmRt = confirmBtnObj.GetComponent<RectTransform>();
        confirmRt.anchoredPosition = new Vector2(-90f, -70f);
        confirmRt.sizeDelta = new Vector2(150f, 40f);
        Image confirmImg = confirmBtnObj.GetComponent<Image>();
        confirmImg.color = new Color(0.14f, 0.52f, 0.21f, 1f);
        nameConfirmButton = confirmBtnObj.GetComponent<Button>();
        nameConfirmButton.onClick.AddListener(OnNameConfirmClicked);

        GameObject confirmTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        confirmTextObj.transform.SetParent(confirmBtnObj.transform, false);
        RectTransform ctRt = confirmTextObj.GetComponent<RectTransform>();
        ctRt.anchorMin = Vector2.zero;
        ctRt.anchorMax = Vector2.one;
        ctRt.offsetMin = Vector2.zero;
        ctRt.offsetMax = Vector2.zero;
        TMPro.TextMeshProUGUI ctTmp = confirmTextObj.GetComponent<TMPro.TextMeshProUGUI>();
        ctTmp.text = "<b>CONTINUE</b>";
        ctTmp.fontSize = 16;
        ctTmp.alignment = TMPro.TextAlignmentOptions.Center;
        ctTmp.color = Color.white;

        // Cancel Button
        GameObject cancelBtnObj = new GameObject("CancelBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        cancelBtnObj.transform.SetParent(box.transform, false);
        RectTransform cancelRt = cancelBtnObj.GetComponent<RectTransform>();
        cancelRt.anchoredPosition = new Vector2(90f, -70f);
        cancelRt.sizeDelta = new Vector2(150f, 40f);
        Image cancelImg = cancelBtnObj.GetComponent<Image>();
        cancelImg.color = new Color(0.2f, 0.23f, 0.27f, 1f);
        nameCancelButton = cancelBtnObj.GetComponent<Button>();
        nameCancelButton.onClick.AddListener(OnNameCancelClicked);

        GameObject cancelTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        cancelTextObj.transform.SetParent(cancelBtnObj.transform, false);
        RectTransform cancelTRt = cancelTextObj.GetComponent<RectTransform>();
        cancelTRt.anchorMin = Vector2.zero;
        cancelTRt.anchorMax = Vector2.one;
        cancelTRt.offsetMin = Vector2.zero;
        cancelTRt.offsetMax = Vector2.zero;
        TMPro.TextMeshProUGUI cancelTmp = cancelTextObj.GetComponent<TMPro.TextMeshProUGUI>();
        cancelTmp.text = "<b>CANCEL</b>";
        cancelTmp.fontSize = 16;
        cancelTmp.alignment = TMPro.TextAlignmentOptions.Center;
        cancelTmp.color = Color.white;

        nameInputModal = overlay;
    }

    private void EnsureLeaderboardModalBuilt()
    {
        if (globalLeaderboardModal != null) return;

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject overlay = new GameObject("GlobalLeaderboardModalOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        RectTransform rt = overlay.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = overlay.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.88f);

        GameObject box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(overlay.transform, false);
        RectTransform boxRt = box.GetComponent<RectTransform>();
        boxRt.sizeDelta = new Vector2(680f, 460f);
        Image boxImg = box.GetComponent<Image>();
        boxImg.color = new Color(0.09f, 0.11f, 0.14f, 0.98f);

        // Title
        GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        titleObj.transform.SetParent(box.transform, false);
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0f, 180f);
        titleRt.sizeDelta = new Vector2(640f, 50f);
        TMPro.TextMeshProUGUI titleTmp = titleObj.GetComponent<TMPro.TextMeshProUGUI>();
        titleTmp.text = "<b><color=#00FFA3>GLOBAL</color> <color=#FFFFFF>LEADERBOARD</color></b>\n<size=50%><color=#8E9BAE>TOP DRIVERS (CLEAN RUNS ONLY)</color></size>";
        titleTmp.fontSize = 24;
        titleTmp.alignment = TMPro.TextAlignmentOptions.Center;

        // Content
        GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        contentObj.transform.SetParent(box.transform, false);
        RectTransform contentRt = contentObj.GetComponent<RectTransform>();
        contentRt.anchoredPosition = new Vector2(0f, 10f);
        contentRt.sizeDelta = new Vector2(620f, 260f);
        globalLeaderboardText = contentObj.GetComponent<TMPro.TextMeshProUGUI>();
        globalLeaderboardText.fontSize = 17;
        globalLeaderboardText.alignment = TMPro.TextAlignmentOptions.TopLeft;

        // Back Button
        GameObject backBtnObj = new GameObject("BackBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        backBtnObj.transform.SetParent(box.transform, false);
        RectTransform backRt = backBtnObj.GetComponent<RectTransform>();
        backRt.anchoredPosition = new Vector2(0f, -180f);
        backRt.sizeDelta = new Vector2(200f, 45f);
        Image backImg = backBtnObj.GetComponent<Image>();
        backImg.color = new Color(0.18f, 0.22f, 0.28f, 1f);
        globalLeaderboardBackButton = backBtnObj.GetComponent<Button>();
        globalLeaderboardBackButton.onClick.AddListener(CloseGlobalLeaderboard);

        GameObject backTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        backTextObj.transform.SetParent(backBtnObj.transform, false);
        RectTransform backTRt = backTextObj.GetComponent<RectTransform>();
        backTRt.anchorMin = Vector2.zero;
        backTRt.anchorMax = Vector2.one;
        backTRt.offsetMin = Vector2.zero;
        backTRt.offsetMax = Vector2.zero;
        TMPro.TextMeshProUGUI backTmp = backTextObj.GetComponent<TMPro.TextMeshProUGUI>();
        backTmp.text = "<b>BACK TO MENU</b>";
        backTmp.fontSize = 16;
        backTmp.alignment = TMPro.TextAlignmentOptions.Center;
        backTmp.color = Color.white;

        globalLeaderboardModal = overlay;
    }

    public void Quit()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                Application.Quit();
            });
        }
        else
        {
            Application.Quit();
        }
    }

    public void Options()
    {
        if (optionsButton != null) AnimateButtonPress(optionsButton);
        if (isBusy || isOptionMenuOpen || isNameModalOpen || isLeaderboardModalOpen) return;

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                OpenOptions();
            });
        }
        else
        {
            OpenOptions();
        }
    }

    public void BackToOptions()
    {
        if (optionBackButton != null) AnimateButtonPress(optionBackButton);
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TriggerTransition(() =>
            {
                CloseOptions();
            });
        }
        else
        {
            CloseOptions();
        }
    }

    private float nextSliderAdjustTime;

    private void OpenOptions()
    {
        if (isOptionMenuOpen)
        {
            return;
        }

        isOptionMenuOpen = true;
        optionsFocusIndex = 0;

        DisableMainButtons();

        if (OptionMenu != null)
        {
            OptionMenu.SetActive(true);
        }

        SetupVolumeSliders();

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(true);
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (optionBackButton != null)
        {
            optionBackButton.interactable = true;

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(optionBackButton.gameObject);
            }

            optionBackButton.Select();
        }
    }

    private void CloseOptions()
    {
        isOptionMenuOpen = false;

        if (OptionMenu != null)
        {
            OptionMenu.SetActive(false);
        }

        if (CursorManager.Instance != null)
        {
            CursorManager.Instance.SetCursorVisibility(false);
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        EnableMainButtons();

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        SelectButton(selectedIndex);
    }

    private void DisableMainButtons()
    {
        foreach (Button button in buttons)
        {
            if (button != null)
            {
                button.interactable = false;
            }
        }
    }

    private void EnableMainButtons()
    {
        foreach (Button button in buttons)
        {
            if (button != null)
            {
                button.interactable = true;
            }
        }
    }

    private void ReadOptionsInput()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseOptions();
                return;
            }
        }

        Gamepad gamepad = GetGamepad();

        if (gamepad != null)
        {
            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                CloseOptions();
                return;
            }
        }

        // Navigate between options items (0: Music, 1: SFX, 2: Back Button)
        if (Time.unscaledTime >= nextOptionsMoveTime)
        {
            int verticalMove = 0;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame)
                    verticalMove = -1;
                else if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame)
                    verticalMove = 1;
            }

            if (gamepad != null)
            {
                Vector2 dpadVal = gamepad.dpad.ReadValue();
                Vector2 stickVal = gamepad.leftStick.ReadValue();
                if (gamepad.dpad.up.wasPressedThisFrame || dpadVal.y >= 0.4f || stickVal.y >= 0.4f)
                    verticalMove = -1;
                else if (gamepad.dpad.down.wasPressedThisFrame || dpadVal.y <= -0.4f || stickVal.y <= -0.4f)
                    verticalMove = 1;
            }

            if (verticalMove != 0)
            {
                int maxItems = sfxVolumeSlider != null ? 3 : 2;
                optionsFocusIndex = (optionsFocusIndex + verticalMove + maxItems) % maxItems;
                nextOptionsMoveTime = Time.unscaledTime + 0.2f;
            }
        }

        // Handle Gamepad / Keyboard Horizontal Slider Control
        if (Time.unscaledTime >= nextSliderAdjustTime)
        {
            float horizontal = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
                    horizontal = -1f;
                else if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
                    horizontal = 1f;
            }

            if (gamepad != null)
            {
                Vector2 dpadVal = gamepad.dpad.ReadValue();
                Vector2 stickVal = gamepad.leftStick.ReadValue();

                if (gamepad.dpad.left.isPressed || dpadVal.x <= -0.4f || stickVal.x <= -0.4f)
                    horizontal = -1f;
                else if (gamepad.dpad.right.isPressed || dpadVal.x >= 0.4f || stickVal.x >= 0.4f)
                    horizontal = 1f;
            }

            if (Mathf.Abs(horizontal) > 0.1f)
            {
                float step = 0.05f * Mathf.Sign(horizontal);

                // Determine target slider based on focus or availability
                Slider targetSlider = null;
                if (optionsFocusIndex == 1 && sfxVolumeSlider != null)
                {
                    targetSlider = sfxVolumeSlider;
                }
                else if (optionsFocusIndex == 0 && musicVolumeSlider != null)
                {
                    targetSlider = musicVolumeSlider;
                }
                else if (musicVolumeSlider != null)
                {
                    targetSlider = musicVolumeSlider;
                }
                else if (sfxVolumeSlider != null)
                {
                    targetSlider = sfxVolumeSlider;
                }

                if (targetSlider != null)
                {
                    targetSlider.value = Mathf.Clamp01(targetSlider.value + step);
                    nextSliderAdjustTime = Time.unscaledTime + 0.12f;
                }
            }
        }

        if (optionBackButton != null)
        {
            bool submitPressed = false;

            if (Keyboard.current != null)
            {
                submitPressed =
                    Keyboard.current.enterKey.wasPressedThisFrame ||
                    Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                    Keyboard.current.spaceKey.wasPressedThisFrame;
            }

            if (gamepad != null)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame)
                {
                    submitPressed = true;
                }
            }

            if (submitPressed &&
                optionBackButton.IsActive() &&
                optionBackButton.IsInteractable())
            {
                optionBackButton.onClick.Invoke();
            }
        }
    }

    private Gamepad GetGamepad()
    {
        if (Gamepad.current != null)
        {
            return Gamepad.current;
        }

        if (Gamepad.all.Count > 0)
        {
            return Gamepad.all[0];
        }

        return null;
    }

    private void ReadSelectionInput()
    {
        if (Time.unscaledTime < nextMoveTime)
        {
            return;
        }

        float vertical = 0f;

        // 1. Keyboard Arrow Keys ONLY (WASD disabled for UI selection)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                vertical = 1f;
            }
            else if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                vertical = -1f;
            }
        }

        // 2. Gamepad D-Pad ONLY (Left stick disabled for UI selection)
        Gamepad gamepad = GetGamepad();

        if (gamepad != null)
        {
            float dpadY = gamepad.dpad.ReadValue().y;
            float dpadX = gamepad.dpad.ReadValue().x;

            if (Mathf.Abs(dpadY) >= gamepadDeadzone)
            {
                vertical = dpadY;
            }
            else if (Mathf.Abs(dpadX) >= gamepadDeadzone)
            {
                vertical = -dpadX;
            }
        }

        if (vertical > gamepadDeadzone)
        {
            MoveSelection(-1);
        }
        else if (vertical < -gamepadDeadzone)
        {
            MoveSelection(1);
        }
    }

    private void ReadSubmitInput()
    {
        bool submitPressed = false;

        if (Keyboard.current != null)
        {
            submitPressed =
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        Gamepad gamepad = GetGamepad();

        if (gamepad != null)
        {
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                submitPressed = true;
            }
        }

        if (submitPressed)
        {
            SubmitSelectedButton();
        }
    }

    private void MoveSelection(int direction)
    {
        int nextIndex = selectedIndex + direction;

        if (wrapSelection)
        {
            if (nextIndex < 0)
            {
                nextIndex = buttons.Count - 1;
            }
            else if (nextIndex >= buttons.Count)
            {
                nextIndex = 0;
            }
        }
        else
        {
            nextIndex = Mathf.Clamp(
                nextIndex,
                0,
                buttons.Count - 1
            );
        }

        SelectButton(nextIndex);

        nextMoveTime =
            Time.unscaledTime + moveRepeatDelay;
    }

    public void SelectButton(int index)
    {
        if (buttons.Count == 0 ||
            index < 0 ||
            index >= buttons.Count)
        {
            return;
        }

        selectedIndex = index;

        Button selectedButton = buttons[selectedIndex];

        if (EventSystem.current != null &&
            selectedButton != null)
        {
            EventSystem.current.SetSelectedGameObject(
                selectedButton.gameObject
            );

            selectedButton.Select();
        }

        UpdateAnimatorSelection();
    }

    private void UpdateAnimatorSelection()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            string parameterName = selectedBoolPrefix + (i + 1);

            if (anim != null && HasAnimatorParameter(anim, parameterName, AnimatorControllerParameterType.Bool))
            {
                anim.SetBool(parameterName, i == selectedIndex);
            }

            if (carAnimator != null && HasAnimatorParameter(carAnimator, parameterName, AnimatorControllerParameterType.Bool))
            {
                carAnimator.SetBool(parameterName, i == selectedIndex);
            }

            if (panelAnim != null && HasAnimatorParameter(panelAnim, parameterName, AnimatorControllerParameterType.Bool))
            {
                panelAnim.SetBool(parameterName, i == selectedIndex);
            }
        }
    }

    private void SubmitSelectedButton()
    {
        if (selectedIndex < 0 ||
            selectedIndex >= buttons.Count)
        {
            return;
        }

        Button selectedButton = buttons[selectedIndex];

        if (selectedButton == null ||
            !selectedButton.gameObject.activeInHierarchy ||
            !selectedButton.interactable)
        {
            return;
        }

        AnimateButtonPress(selectedButton);

        if (selectedButton == hostGameButton)
        {
            HostGame();
            return;
        }

        if (selectedButton == joinGameButton)
        {
            JoinGame();
            return;
        }

        if (selectedButton == leaderboardButton)
        {
            OpenGlobalLeaderboard();
            return;
        }

        if (selectedButton == optionsButton)
        {
            Options();
            return;
        }

        selectedButton.onClick.Invoke();
    }

    public void AnimateButtonPress(Button btn)
    {
        if (btn == null) return;
        Transform t = btn.transform;

        if (activePunchCoroutines.ContainsKey(t) && activePunchCoroutines[t] != null)
        {
            StopCoroutine(activePunchCoroutines[t]);
        }

        activePunchCoroutines[t] = StartCoroutine(ButtonPunchRoutine(t));
    }

    private IEnumerator ButtonPunchRoutine(Transform targetTransform)
    {
        if (targetTransform == null) yield break;

        if (!initialButtonScales.ContainsKey(targetTransform))
        {
            initialButtonScales[targetTransform] = targetTransform.localScale;
        }

        Vector3 baseScale = initialButtonScales[targetTransform];
        targetTransform.localScale = baseScale * 0.88f;

        yield return new WaitForSecondsRealtime(0.08f);

        targetTransform.localScale = baseScale;
        activePunchCoroutines.Remove(targetTransform);
    }

    private bool HasAnimatorParameter(
        Animator animatorToCheck,
        string parameterName,
        AnimatorControllerParameterType parameterType)
    {
        if (animatorToCheck == null ||
            string.IsNullOrEmpty(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter
                 in animatorToCheck.parameters)
        {
            if (parameter.name == parameterName &&
                parameter.type == parameterType)
            {
                return true;
            }
        }

        return false;
    }

    private void DisableUIControllerSubmit()
    {
        if (uiInputModule == null)
        {
            return;
        }

        uiInputModule.submit = null;
        uiInputModule.cancel = null;
    }
}