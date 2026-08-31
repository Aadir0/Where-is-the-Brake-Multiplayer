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

    [Header("Options Menu")]
    [SerializeField] private GameObject OptionMenu;
    [SerializeField] private Button optionBackButton;

    private int selectedIndex = 0;
    private float nextMoveTime;
    private bool isBusy;
    private bool isOptionMenuOpen;

    private void Awake()
    {
        DisableUIControllerSubmit();
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
        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.OpenJoinUI();
        }
    }

    public void Quit()
    {
        Application.Quit();
    }

    public void Options()
    {
        if (optionsButton != null) AnimateButtonPress(optionsButton);
        if (isBusy || isOptionMenuOpen)
        {
            return;
        }

        OpenOptions();
    }

    public void BackToOptions()
    {
        if (optionBackButton != null) AnimateButtonPress(optionBackButton);
        CloseOptions();
    }

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

        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.isPressed ||
                Keyboard.current.wKey.isPressed)
            {
                vertical = 1f;
            }
            else if (Keyboard.current.downArrowKey.isPressed ||
                     Keyboard.current.sKey.isPressed)
            {
                vertical = -1f;
            }
        }

        // Gamepad selection ONLY via D-Pad (Left Stick disabled for UI button selection)
        Gamepad gamepad = GetGamepad();

        if (gamepad != null)
        {
            float dpadY = gamepad.dpad.ReadValue().y;

            if (Mathf.Abs(dpadY) >= gamepadDeadzone)
            {
                vertical = dpadY;
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
        StartCoroutine(ButtonPunchRoutine(btn.transform));
    }

    private IEnumerator ButtonPunchRoutine(Transform targetTransform)
    {
        if (targetTransform == null) yield break;
        Vector3 orig = targetTransform.localScale;
        targetTransform.localScale = orig * 0.88f;
        yield return new WaitForSecondsRealtime(0.08f);
        targetTransform.localScale = orig;
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