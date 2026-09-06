using UnityEngine;

public class ShadowJump : MonoBehaviour
{
    [Header("Jump Shadow")]
    [SerializeField] private float shadowScale = 1.15f;
    [SerializeField] private float shadowDistance = 0.15f;

    [Header("Direction")]
    [SerializeField] private float sideOffset = 0.2f;

    [Header("Smoothness")]
    [SerializeField] private float positionSmoothness = 16f;
    [SerializeField] private float rotationSmoothness = 14f;

    [Header("Rotation")]
    [Range(0f, 1f)]
    [SerializeField] private float rotationAmount = 0.92f;

    [Header("Shadow Color")]
    [SerializeField] private Color groundShadowColor = new Color(0f, 0f, 0f, 0.6f);

    private Transform target;
    private SpriteRenderer targetSpriteRenderer;
    private SpriteRenderer shadowRenderer;
    private Sprite defaultShadowSprite;
    private Material defaultMaterial;
    private Vector3 initialScale;
    private bool active;
    private float disableTime;

    private void Awake()
    {
        initialScale = transform.localScale;
        shadowRenderer = GetComponent<SpriteRenderer>();
        if (shadowRenderer != null)
        {
            defaultShadowSprite = shadowRenderer.sprite;
            defaultMaterial = shadowRenderer.sharedMaterial;
        }

        gameObject.SetActive(false);
    }

    public void Initialize(
        Transform targetTransform,
        float duration)
    {
        target = targetTransform;
        FetchTargetSpriteRenderer();

        if (shadowRenderer == null) shadowRenderer = GetComponent<SpriteRenderer>();
        if (shadowRenderer != null)
        {
            if (defaultShadowSprite == null) defaultShadowSprite = shadowRenderer.sprite;
            if (defaultMaterial == null) defaultMaterial = shadowRenderer.sharedMaterial;
            shadowRenderer.material = defaultMaterial;
            shadowRenderer.color = groundShadowColor;
            shadowRenderer.flipY = false;
        }

        active = true;
        disableTime = Time.time + duration;

        transform.localScale = initialScale * shadowScale;

        // Calculate and snap to initial position and rotation
        Vector2 forward = target.right;
        Vector2 side = target.up;
        Vector2 shadowDirection = (-forward + side * sideOffset).normalized;
        Vector2 desiredPosition = (Vector2)target.position + shadowDirection * shadowDistance;

        float desiredAngle = target.eulerAngles.z * rotationAmount;
        transform.position = desiredPosition;
        transform.rotation = Quaternion.Euler(0f, 0f, desiredAngle);

        UpdateVisuals();

        gameObject.SetActive(true);
    }

    private void FetchTargetSpriteRenderer()
    {
        if (target == null) return;
        targetSpriteRenderer = target.GetComponent<SpriteRenderer>();
        if (targetSpriteRenderer == null) targetSpriteRenderer = target.GetComponentInChildren<SpriteRenderer>();
        if (targetSpriteRenderer == null) targetSpriteRenderer = target.GetComponentInParent<SpriteRenderer>();
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

        Vector2 forward = target.right;
        Vector2 side = target.up;

        Vector2 shadowDirection = (-forward + side * sideOffset).normalized;
        Vector2 desiredPosition = (Vector2)target.position + shadowDirection * shadowDistance;

        UpdateVisuals();

        float positionLerp = 1f - Mathf.Exp(-positionSmoothness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionLerp);

        float desiredAngle = target.eulerAngles.z * rotationAmount;
        float rotationLerp = 1f - Mathf.Exp(-rotationSmoothness * Time.deltaTime);
        float currentAngle = Mathf.LerpAngle(transform.eulerAngles.z, desiredAngle, rotationLerp);

        transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
    }

    private void UpdateVisuals()
    {
        if (targetSpriteRenderer == null || targetSpriteRenderer.sprite == null)
        {
            FetchTargetSpriteRenderer();
        }

        if (shadowRenderer != null)
        {
            Sprite activeCarSprite = (targetSpriteRenderer != null && targetSpriteRenderer.sprite != null)
                ? targetSpriteRenderer.sprite
                : defaultShadowSprite;

            if (defaultMaterial != null)
            {
                shadowRenderer.material = defaultMaterial;
            }
            shadowRenderer.sprite = activeCarSprite != null ? activeCarSprite : defaultShadowSprite;
            shadowRenderer.color = groundShadowColor;
            shadowRenderer.flipY = false;
            if (targetSpriteRenderer != null) shadowRenderer.flipX = targetSpriteRenderer.flipX;
        }
    }

    public void DisableShadow()
    {
        active = false;
        target = null;
        targetSpriteRenderer = null;
        transform.localScale = initialScale;

        if (shadowRenderer != null)
        {
            if (defaultMaterial != null) shadowRenderer.material = defaultMaterial;
            if (defaultShadowSprite != null) shadowRenderer.sprite = defaultShadowSprite;
            shadowRenderer.color = groundShadowColor;
            shadowRenderer.flipY = false;
        }

        gameObject.SetActive(false);
    }
}