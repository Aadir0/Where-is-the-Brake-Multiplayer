using UnityEngine;
using UnityEngine.EventSystems;
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

	[Header("Winning Continue Input")]
	[SerializeField] private KeyCode[] continueKeys =
	{
		KeyCode.Return,
		KeyCode.Space,
		KeyCode.JoystickButton0
	};

	private EventSystem eventSystem;
	private Vector3 restartBaseScale;
	private Vector3 mainMenuBaseScale;

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

		SetupNavigation();
	}

	private void OnEnable()
	{
		if (eventSystem == null)
		{
			eventSystem = EventSystem.current;
		}

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

		SelectDefaultButton();
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

	private void Update()
	{
		if (eventSystem == null)
		{
			eventSystem = EventSystem.current;
		}

		if (eventSystem != null && eventSystem.currentSelectedGameObject == null)
		{
			SelectDefaultButton();
		}

		if (isWinningCanvas)
		{
			HandleWinningContinueInput();
		}

		AnimateButtonScale();
	}

	private void SetupNavigation()
	{
		if (restartButton == null || mainMenuButton == null)
		{
			return;
		}

		var restartNav = restartButton.navigation;
		restartNav.mode = Navigation.Mode.Explicit;
		restartNav.selectOnDown = mainMenuButton;
		restartNav.selectOnUp = mainMenuButton;
		restartNav.selectOnLeft = mainMenuButton;
		restartNav.selectOnRight = mainMenuButton;
		restartButton.navigation = restartNav;

		var mainMenuNav = mainMenuButton.navigation;
		mainMenuNav.mode = Navigation.Mode.Explicit;
		mainMenuNav.selectOnDown = restartButton;
		mainMenuNav.selectOnUp = restartButton;
		mainMenuNav.selectOnLeft = restartButton;
		mainMenuNav.selectOnRight = restartButton;
		mainMenuButton.navigation = mainMenuNav;
	}

	private void SelectDefaultButton()
	{
		if (eventSystem == null)
		{
			return;
		}

		if (isWinningCanvas)
		{
			if (mainMenuButton != null)
			{
				eventSystem.SetSelectedGameObject(mainMenuButton.gameObject);
			}
			else if (restartButton != null)
			{
				eventSystem.SetSelectedGameObject(restartButton.gameObject);
			}

			return;
		}

		if (restartButton != null)
		{
			eventSystem.SetSelectedGameObject(restartButton.gameObject);
			return;
		}

		if (mainMenuButton != null)
		{
			eventSystem.SetSelectedGameObject(mainMenuButton.gameObject);
		}
	}

	private void HandleWinningContinueInput()
	{
		if (!IsContinuePressed())
		{
			return;
		}

		LoadNextScene();
	}

	private bool IsContinuePressed()
	{
		if (continueKeys == null || continueKeys.Length == 0)
		{
			return false;
		}

		for (int i = 0; i < continueKeys.Length; i++)
		{
			if (Input.GetKeyDown(continueKeys[i]))
			{
				return true;
			}
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

		if (restartButton != null)
		{
			var target = selected == restartButton.gameObject
				? restartBaseScale * selectedScaleMultiplier
				: restartBaseScale * unselectedScaleMultiplier;

			restartButton.transform.localScale = Vector3.Lerp(
				restartButton.transform.localScale,
				target,
				Time.unscaledDeltaTime * scaleLerpSpeed
			);
		}

		if (mainMenuButton != null)
		{
			var target = selected == mainMenuButton.gameObject
				? mainMenuBaseScale * selectedScaleMultiplier
				: mainMenuBaseScale * unselectedScaleMultiplier;

			mainMenuButton.transform.localScale = Vector3.Lerp(
				mainMenuButton.transform.localScale,
				target,
				Time.unscaledDeltaTime * scaleLerpSpeed
			);
		}
	}

	public void RestartLevel()
	{
		SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
	}

	public void ReturnToMainMenu()
	{
		if (string.IsNullOrWhiteSpace(mainMenuSceneName))
		{
			Debug.LogWarning("Main menu scene name is empty on GameButton.");
			return;
		}

		SceneManager.LoadScene(mainMenuSceneName);
	}

	private void LoadNextScene()
	{
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
