using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Transition Panel References")]
    [SerializeField] private GameObject circleTransitionPanel;
    [SerializeField] private RectTransform circleTransform;

    [Header("Animation Settings")]
    [SerializeField] private float transitionDuration = 0.35f;
    [SerializeField] private Vector3 maxCircleScale = new Vector3(25f, 25f, 1f);

    private bool isTransitioning = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent != null)
        {
            transform.SetParent(null);
        }

        DontDestroyOnLoad(gameObject);

        LocateCirclePanel();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        LocateCirclePanel();
        if (circleTransitionPanel != null && circleTransitionPanel != gameObject)
        {
            StartCoroutine(AnimateCircleInRoutine());
        }
    }

    public void LocateCirclePanel()
    {
        // Must NOT be this manager GameObject itself!
        if (circleTransitionPanel != null && circleTransitionPanel != gameObject && circleTransform != null)
        {
            return;
        }

        circleTransitionPanel = null;
        circleTransform = null;

        GameObject tagged = GameObject.FindGameObjectWithTag("Transition");
        if (tagged != null && tagged != gameObject && !tagged.transform.IsChildOf(transform))
        {
            RectTransform rect = tagged.GetComponent<RectTransform>() ?? tagged.GetComponentInChildren<RectTransform>(true);
            if (rect != null)
            {
                circleTransitionPanel = tagged;
                circleTransform = rect;
                return;
            }
        }

        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var canvas in canvases)
        {
            foreach (Transform child in canvas.transform)
            {
                if (child.gameObject == gameObject || child.IsChildOf(transform)) continue;
                string n = child.name.ToLower();
                if (child.CompareTag("Transition") || n.Contains("circletransition") || n.Contains("transitionpanel") || n.Equals("circle"))
                {
                    RectTransform rect = child.GetComponent<RectTransform>() ?? child.GetComponentInChildren<RectTransform>(true);
                    if (rect != null)
                    {
                        circleTransitionPanel = child.gameObject;
                        circleTransform = rect;
                        return;
                    }
                }
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        circleTransitionPanel = null;
        circleTransform = null;
        LocateCirclePanel();

        StopAllCoroutines();
        isTransitioning = false;

        if (circleTransitionPanel != null && circleTransitionPanel != gameObject)
        {
            StartCoroutine(AnimateCircleInRoutine());
        }
    }

    public void TriggerTransition(Action onFullyCovered = null)
    {
        LocateCirclePanel();

        if (circleTransitionPanel == null || circleTransitionPanel == gameObject || circleTransform == null)
        {
            isTransitioning = false;
            onFullyCovered?.Invoke();
            return;
        }

        if (isTransitioning)
        {
            onFullyCovered?.Invoke();
            return;
        }

        StartCoroutine(TransitionRoutine(onFullyCovered));
    }

    public void ShowTransitionCover()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject) return;

        StopAllCoroutines();
        isTransitioning = true;
        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null)
        {
            circleTransform.SetAsLastSibling();
            circleTransform.anchoredPosition3D = Vector3.zero;
            circleTransform.localScale = maxCircleScale;
        }
    }

    public void HideTransitionCover()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject)
        {
            isTransitioning = false;
            return;
        }

        StopAllCoroutines();
        isTransitioning = false;
        StartCoroutine(AnimateCircleInRoutine());
    }

    public void LoadSceneWithTransition(string sceneName)
    {
        TriggerTransition(() =>
        {
            SceneManager.LoadScene(sceneName);
        });
    }

    // Phase 1: Enable transition panel for duration
    public IEnumerator AnimateCircleOutRoutine()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null)
        {
            circleTransform.SetAsLastSibling();
            circleTransform.anchoredPosition3D = Vector3.zero;
            circleTransform.localScale = maxCircleScale;
        }

        yield return new WaitForSecondsRealtime(transitionDuration);
    }

    // Phase 2: Keep transition panel enabled for duration, then disable
    public IEnumerator AnimateCircleInRoutine()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null)
        {
            circleTransform.SetAsLastSibling();
            circleTransform.anchoredPosition3D = Vector3.zero;
            circleTransform.localScale = maxCircleScale;
        }

        yield return new WaitForSecondsRealtime(transitionDuration);

        circleTransitionPanel.SetActive(false);
    }

    private IEnumerator TransitionRoutine(Action onFullyCovered)
    {
        isTransitioning = true;

        yield return StartCoroutine(AnimateCircleOutRoutine());

        onFullyCovered?.Invoke();

        yield return new WaitForSecondsRealtime(0.04f);

        yield return StartCoroutine(AnimateCircleInRoutine());

        isTransitioning = false;
    }
}
