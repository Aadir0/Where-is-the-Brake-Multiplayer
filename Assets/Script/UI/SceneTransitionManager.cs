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

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == gameObject) continue; // Never target the manager script's own GameObject
            if (go.transform.IsChildOf(transform)) continue;

            if (go.scene.isLoaded &&
                (go.CompareTag("Transition") ||
                 string.Equals(go.name, "CircleTransition", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(go.name, "TransitionPanel", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(go.name, "Circle", StringComparison.OrdinalIgnoreCase)))
            {
                RectTransform rect = go.GetComponent<RectTransform>();
                if (rect == null) rect = go.GetComponentInChildren<RectTransform>(true);

                if (rect != null)
                {
                    circleTransitionPanel = go;
                    circleTransform = rect;
                    break;
                }
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        circleTransitionPanel = null;
        circleTransform = null;
        LocateCirclePanel();

        if (circleTransitionPanel != null && circleTransitionPanel != gameObject)
        {
            StopAllCoroutines();
            isTransitioning = false;
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
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null)
        {
            circleTransform.SetAsLastSibling();
            circleTransform.anchoredPosition3D = Vector3.zero;
            circleTransform.localScale = Vector3.zero;
        }

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
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
        if (circleTransitionPanel == null || circleTransitionPanel == gameObject) yield break;

        circleTransitionPanel.SetActive(true);
        if (circleTransform == null) circleTransform = circleTransitionPanel.GetComponent<RectTransform>();

        if (circleTransform != null)
        {
            circleTransform.SetAsLastSibling();
            circleTransform.anchoredPosition3D = Vector3.zero;
            circleTransform.localScale = maxCircleScale;
        }

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
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

        yield return StartCoroutine(AnimateCircleOutRoutine());

        onFullyCovered?.Invoke();

        yield return new WaitForSecondsRealtime(0.04f);

        yield return StartCoroutine(AnimateCircleInRoutine());

        isTransitioning = false;
    }
}
