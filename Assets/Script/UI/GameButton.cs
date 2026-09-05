using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameButton : MonoBehaviour
{
    [Header("Canvas Type")]
    [SerializeField] private bool isWinningCanvas;

    [Header("Buttons")]
    [SerializeField] private Button restartButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Main Menu")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Selection Visual")]
    [SerializeField, Min(1f)] private float selectedScaleMultiplier = 1.1f;
    [SerializeField, Min(0.1f)] private float unselectedScaleMultiplier = 0.95f;
    [SerializeField, Min(0f)] private float scaleLerpSpeed = 10f;

    [Header("Input Repeat Settings")]
    [SerializeField] private float moveRepeatDelay = 0.22f;

    private EventSystem eventSystem;
    private Vector3 restartBaseScale;
    private Vector3 mainMenuBaseScale;
    private float nextMoveTime;
    private int selectedIndex = 0; // 0 = Restart, 1 = MainMenu

    private void Awake()
    {
        eventSystem = EventSystem.current;

        if (restartButton != null)
        {
            restartBaseScale = restartButton.transform.localScale;
        }

        if (mainMenuButton != null)
        {
            mainMenuBaseScale = mainMenuButton.transform.localScale;
        }

        DisableBuiltInUIMovement();
    }

    private void OnEnable()
    {
        if (eventSystem == null)
        {
            eventSystem = EventSystem.current;
        }

        DisableBuiltInUIMovement();

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartLevel);
            restartButton.onClick.AddListener(RestartLevel);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }

        selectedIndex = isWinningCanvas && mainMenuButton != null ? 1 : 0;
        SelectButton(selectedIndex);
    }

    private void OnDisable()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartLevel);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
        }
    }

    private void DisableBuiltInUIMovement()
    {
        // 1. Set Navigation mode to None so Unity EventSystem doesn't respond to Left Stick automatically
        if (restartButton != null)
        {
            Navigation nav = restartButton.navigation;
            nav.mode = Navigation.Mode.None;
            restartButton.navigation = nav;
        }

        if (mainMenuButton != null)
        {
            Navigation nav = mainMenuButton.navigation;
            nav.mode = Navigation.Mode.None;
            mainMenuButton.navigation = nav;
        }

        // 2. Disable move action on InputSystemUIInputModule so Left Stick moves are completely ignored by UI
        InputSystemUIInputModule uiInputModule = Object.FindFirstObjectByType<InputSystemUIInputModule>();
        if (uiInputModule != null)
        {
            uiInputModule.move = null;
        }
    }

    private void Update()
    {
        if (eventSystem == null)
        {
            eventSystem = EventSystem.current;
        }

        if (eventSystem != null && eventSystem.currentSelectedGameObject == null)
        {
            SelectButton(selectedIndex);
        }

        ReadNavigationInput();
        ReadSubmitInput();

        if (isWinningCanvas)
        {
            HandleWinningContinueInput();
        }

        AnimateButtonScale();
    }

    [Header("Rotation Settings")]
    [SerializeField] private float buttonWobbleSpeed = 4.0f;
    [SerializeField] private float buttonWobbleAngle = 2.0f;

    private void ReadNavigationInput()
    {
        if (Time.unscaledTime < nextMoveTime) return;
        if (restartButton == null || mainMenuButton == null) return;

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

        // 2. Gamepad D-Pad ONLY (Left Stick strictly disabled)
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
        Button targetBtn = (selectedIndex == 0) ? restartButton : mainMenuButton;

        if (targetBtn == null)
        {
            targetBtn = (selectedIndex == 0) ? mainMenuButton : restartButton;
        }

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
            submitPressed = Keyboard.current.enterKey.wasPressedThisFrame ||
                            Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                            Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        Gamepad gamepad = Gamepad.current ?? (Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
        if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
        {
            submitPressed = true;
        }

        if (submitPressed)
        {
            Button currentBtn = (selectedIndex == 0) ? restartButton : mainMenuButton;
            if (currentBtn != null && currentBtn.IsActive() && currentBtn.IsInteractable())
            {
                currentBtn.onClick.Invoke();
            }
        }
    }

    private void HandleWinningContinueInput()
    {
        // In 2-player multiplayer, do not allow manual skipping to next level.
        // Level transitions must only be triggered when both players finish or finish timer expires.
        bool isMultiplayer = Unity.Netcode.NetworkManager.Singleton != null &&
                             Unity.Netcode.NetworkManager.Singleton.IsListening &&
                             Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count > 1;

        if (isMultiplayer)
        {
            return;
        }

        if (!IsContinuePressed())
        {
            return;
        }

        LoadNextScene();
    }

    private bool IsContinuePressed()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.rKey.wasPressedThisFrame)
            {
                return true;
            }
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        return false;
    }

    private void AnimateButtonScale()
    {
        if (eventSystem == null)
        {
            return;
        }

        var selected = eventSystem.currentSelectedGameObject;
        float rotZ = Mathf.Sin(Time.unscaledTime * buttonWobbleSpeed) * buttonWobbleAngle;

        if (restartButton != null)
        {
            bool isSelected = (selected == restartButton.gameObject || selectedIndex == 0);
            var target = isSelected
                ? restartBaseScale * selectedScaleMultiplier
                : restartBaseScale * unselectedScaleMultiplier;

            restartButton.transform.localScale = Vector3.Lerp(
                restartButton.transform.localScale,
                target,
                Time.unscaledDeltaTime * scaleLerpSpeed
            );

            restartButton.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(restartButton.transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 10f);
        }

        if (mainMenuButton != null)
        {
            bool isSelected = (selected == mainMenuButton.gameObject || selectedIndex == 1);
            var target = isSelected
                ? mainMenuBaseScale * selectedScaleMultiplier
                : mainMenuBaseScale * unselectedScaleMultiplier;

            mainMenuButton.transform.localScale = Vector3.Lerp(
                mainMenuButton.transform.localScale,
                target,
                Time.unscaledDeltaTime * scaleLerpSpeed
            );

            mainMenuButton.transform.localRotation = isSelected
                ? Quaternion.Euler(0f, 0f, rotZ)
                : Quaternion.Lerp(mainMenuButton.transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 10f);
        }
    }

    public void RestartLevel()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsListening &&
            Unity.Netcode.NetworkManager.Singleton.IsServer &&
            Unity.Netcode.NetworkManager.Singleton.SceneManager != null)
        {
            Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturnToMainMenu()
    {
        if (string.IsNullOrWhiteSpace(mainMenuSceneName))
        {
            mainMenuSceneName = "MainMenu";
        }

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ShutdownSession();
        }

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void LoadNextScene()
    {
        if (NetworkRaceManager.Instance != null &&
            Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            NetworkRaceManager.Instance.LoadNextLevelServer();
            return;
        }

        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        int nextIndex = currentIndex + 1;

        if (nextIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextIndex);
            return;
        }

        Debug.LogWarning("No next scene found in Build Settings after current scene.");
    }
}
