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
    [Header("Buttons")]
    [SerializeField] private List<Button> buttons = new List<Button>();
    [SerializeField] private Button hostGameButton;
    [SerializeField] private Button joinGameButton;
    [SerializeField] private Button optionsButton;
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

    [Header("Options Menu & Volume Slider")]
    [SerializeField] private GameObject OptionMenu;
    [SerializeField] private Button optionBackButton;
    [SerializeField] private Slider musicVolumeSlider;

    private int selectedIndex = 0;
    private float nextMoveTime;
    private bool isBusy;
    private bool isOptionMenuOpen;

    private readonly Dictionary<Transform, Vector3> initialButtonScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Coroutine> activePunchCoroutines = new Dictionary<Transform, Coroutine>();

    private void Awake()
    {
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

        if (OptionMenu != null)
        {
            OptionMenu.SetActive(false);
        }

        EnableMainButtons();

        selectedIndex = Mathf.Clamp(firstSelectedIndex, 0, Mathf.Max(0, buttons.Count - 1));
        SelectButton(selectedIndex);

        SetupVolumeSlider();
    }

    private void SetupVolumeSlider()
    {
        if (musicVolumeSlider == null && OptionMenu != null)
        {
            musicVolumeSlider = OptionMenu.GetComponentInChildren<Slider>(true);
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
    }

    private void OnMusicVolumeChanged(float val)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMusicVolume(val);
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

        if (isOptionMenuOpen)
        {
            ReadOptionsInput();
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
        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.CreateRoom();
        }
    }

    public void JoinGame()
    {
        if (joinGameButton != null) AnimateButtonPress(joinGameButton);
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
        if (isBusy || isOptionMenuOpen) return;

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

        DisableMainButtons();

        if (OptionMenu != null)
        {
            OptionMenu.SetActive(true);
        }

        SetupVolumeSlider();

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

        // Handle Gamepad / Keyboard Horizontal Slider Control
        if (musicVolumeSlider != null && Time.unscaledTime >= nextSliderAdjustTime)
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
                musicVolumeSlider.value = Mathf.Clamp01(musicVolumeSlider.value + step);
                nextSliderAdjustTime = Time.unscaledTime + 0.12f;
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