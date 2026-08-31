using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    [Header("Cursor Settings")]
    [SerializeField] private bool hideCursor = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ApplyCursorState();
    }

    private void OnEnable()
    {
        ApplyCursorState();
    }

    private void Update()
    {
        if (hideCursor && (Cursor.visible || Cursor.lockState != CursorLockMode.Locked))
        {
            ApplyCursorState();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ApplyCursorState();
        }
    }

    public void ApplyCursorState()
    {
        if (hideCursor)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    public void SetCursorVisibility(bool visible)
    {
        hideCursor = !visible;
        ApplyCursorState();
    }
}

