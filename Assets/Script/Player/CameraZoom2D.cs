using UnityEngine;

public class CameraZoom2D : MonoBehaviour
{
    public static CameraZoom2D Instance { get; private set; }

    public Transform target;
    public float moveSpeed = 5f;
    public float zoomSpeed = 5f;
    public float normalSize = 2.5f;
    public float zoomSize = 0.5f;
    private Camera cam;
    private bool isZooming = false;

    public bool IsZooming => isZooming;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        cam = GetComponent<Camera>();
        isZooming = false;
        if (cam != null)
        {
            cam.orthographicSize = normalSize;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;

        if (isZooming && target != null)
        {
            Vector3 targetPosition = new Vector3(target.position.x, target.position.y, transform.position.z);
            transform.position = Vector3.Lerp(transform.position, targetPosition, moveSpeed * Time.deltaTime);

            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, zoomSize, zoomSpeed * Time.deltaTime);

            CameraFollow follow = GetComponent<CameraFollow>();
            if (follow != null && follow.enableConfiner)
            {
                transform.position = follow.ClampPosition(transform.position);
            }
        }
        else
        {
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, normalSize, zoomSpeed * Time.deltaTime);
        }
    }

    public void StartZoom(Transform player)
    {
        target = player;
        isZooming = true;
    }

    public void ResetZoom()
    {
        isZooming = false;
        if (cam == null) cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.orthographicSize = normalSize;
        }

        CameraFollow follow = GetComponent<CameraFollow>();
        if (follow != null && follow.enableConfiner)
        {
            transform.position = follow.ClampPosition(transform.position);
        }
    }
}