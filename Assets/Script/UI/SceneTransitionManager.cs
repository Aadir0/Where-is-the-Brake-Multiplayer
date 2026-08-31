using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Transition Panel References")]
    [SerializeField] private GameObject circleTransitionPanel;
    [SerializeField] private RectTransform circleTransform;

    [Header("Animation Settings")]
    [SerializeField] private float transitionDuration = 0.38f;
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
        if (circleTransitionPanel != null)
        {
            StartCoroutine(AnimateCircleInRoutine());
        }
    }

    public void LocateCirclePanel()
    {
        if (circleTransitionPanel == null || circleTransform == null)
        {
            circleTransitionPanel = null;
            circleTransform = null;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.isLoaded &&
                    (go.CompareTag("Transition") ||
                     string.Equals(go.name, "CircleTransition", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(go.name, "TransitionPanel", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(go.name, "Circle", StringComparison.OrdinalIgnoreCase)))
                {
                    circleTransitionPanel = go;
                    circleTransform = go.GetComponent<RectTransform>();
                    break;
                }
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-locate panel in newly loaded scene
        circleTransitionPanel = null;
        circleTransform = null;
        LocateCirclePanel();

        if (circleTransitionPanel != null)
        {
            StopAllCoroutines();
            isTransitioning = false;
            StartCoroutine(AnimateCircleInRoutine());
        }
    }

    public void TriggerTransition(Action onFullyCovered = null)
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionRoutine(onFullyCovered));
    }

    public void LoadSceneWithTransition(string sceneName)
    {
        TriggerTransition(() =>
        {
            SceneManager.LoadScene(sceneName);
        });
    }

    // Phase 1: Circle Scales UP from 0 to Full Screen Coverage
    public IEnumerator AnimateCircleOutRoutine()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null) circleTransform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            // Smooth ease out scale up curve
            float easeT = Mathf.Sin(t * Mathf.PI * 0.5f);

            if (circleTransform != null)
            {
                circleTransform.localScale = Vector3.Lerp(Vector3.zero, maxCircleScale, easeT);
            }
            yield return null;
        }

        if (circleTransform != null) circleTransform.localScale = maxCircleScale;
    }

    // Phase 2: Circle Scales DOWN from Full Screen Coverage to 0 and disables
    public IEnumerator AnimateCircleInRoutine()
    {
        LocateCirclePanel();
        if (circleTransitionPanel == null) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null) circleTransform.localScale = maxCircleScale;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            // Smooth ease in scale down curve
            float easeT = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);

            if (circleTransform != null)
            {
                circleTransform.localScale = Vector3.Lerp(maxCircleScale, Vector3.zero, easeT);
            }
            yield return null;
        }

        if (circleTransform != null) circleTransform.localScale = Vector3.zero;
        circleTransitionPanel.SetActive(false);
    }

    private IEnumerator TransitionRoutine(Action onFullyCovered)
    {
        isTransitioning = true;

        // 1. Scale UP circle to cover screen completely
        yield return StartCoroutine(AnimateCircleOutRoutine());

        // 2. Perform scene load or button action once fully covered
        onFullyCovered?.Invoke();

        // 3. Small pause while covered for seamless panel switch
        yield return new WaitForSecondsRealtime(0.06f);

        // 4. Scale DOWN circle back to 0 and disable
        yield return StartCoroutine(AnimateCircleInRoutine());

        isTransitioning = false;
    }
}
