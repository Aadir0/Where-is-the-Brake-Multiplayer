using UnityEngine;

public class ShadowJump : MonoBehaviour
{
    [Header("Jump Shadow")]
    [SerializeField] private float shadowScale = 1.15f;
    [SerializeField] private float shadowDistance = 0.15f;

    [Header("Direction")]
    [SerializeField] private float sideOffset = 0.2f;

    [Header("Smoothness")]
    [SerializeField] private float positionSmoothness = 7f;
    [SerializeField] private float rotationSmoothness = 5f;

    [Header("Rotation")]
    [Range(0f, 1f)]
    [SerializeField] private float rotationAmount = 0.92f;

    private Transform target;
    private Vector3 initialScale;
    private bool active;
    private float disableTime;

    private void Awake()
    {
        initialScale = transform.localScale;

        gameObject.SetActive(false);
    }

    public void Initialize(
        Transform targetTransform,
        float duration)
    {
        target = targetTransform;

        active = true;

        disableTime =
            Time.time + duration;

        transform.localScale =
            initialScale;

        transform.localScale =
            initialScale * shadowScale;

        transform.position =
            target.position;

        transform.rotation =
            target.rotation;

        gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (!active || target == null || !target.gameObject.activeInHierarchy)
        {
            DisableShadow();
            return;
        }

        Renderer r = target.GetComponent<Renderer>();
        if (r != null && !r.enabled)
        {
            DisableShadow();
            return;
        }

        if (Time.time >= disableTime)
        {
            DisableShadow();
            return;
        }

        Vector2 forward =
            target.right;

        Vector2 side =
            target.up;

        Vector2 shadowDirection =
            (-forward + side * sideOffset).normalized;

        Vector2 desiredPosition =
            (Vector2)target.position +
            shadowDirection * shadowDistance;

        float positionLerp =
            1f - Mathf.Exp(
                -positionSmoothness *
                Time.deltaTime
            );

        transform.position =
            Vector3.Lerp(
                transform.position,
                desiredPosition,
                positionLerp
            );

        float desiredAngle =
            target.eulerAngles.z *
            rotationAmount;

        float rotationLerp =
            1f - Mathf.Exp(
                -rotationSmoothness *
                Time.deltaTime
            );

        float currentAngle =
            Mathf.LerpAngle(
                transform.eulerAngles.z,
                desiredAngle,
                rotationLerp
            );

        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                currentAngle
            );
    }

    private void DisableShadow()
    {
        active = false;

        target = null;
        transform.localScale =
            initialScale;

        gameObject.SetActive(false);
    }
}