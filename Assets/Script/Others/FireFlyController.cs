using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

public class FireflyController : MonoBehaviour
{
    private enum State
    {
        Wandering,
        GoingToFlower,
        GoingToButton,
        SittingOnFlower,
        SittingOnButton
    }

    [Header("Movement")]
    [SerializeField] private float wanderRadius = 2.5f;
    [SerializeField] private float moveSpeed = 0.6f;
    [SerializeField] private float directionChangeTime = 2.5f;

    [Header("Hovering")]
    [SerializeField] private float bobAmount = 0.08f;
    [SerializeField] private float bobSpeed = 2f;

    [Header("Rotation")]
    [SerializeField] private float maxRotation = 15f;
    [SerializeField] private float rotationSmoothness = 4f;

    [Header("Flower Behaviour")]
    [SerializeField] private string flowerTag = "FireflyFlower";

    [SerializeField, Range(0f, 1f)]
    private float flowerChance = 0.25f;

    [SerializeField] private float minimumFlowerDistance = 0.4f;
    [SerializeField] private float flowerDetectionRadius = 4f;

    [SerializeField] private float minimumSitTime = 2f;
    [SerializeField] private float maximumSitTime = 6f;

    [Header("UI Button Behaviour")]
    [Tooltip("Allows this firefly to occasionally land on UI Buttons.")]
    [SerializeField] private bool canSitOnUIButtons = true;
    [SerializeField] private Transform glow;
    [SerializeField] private SpriteRenderer glowSprite;

    [SerializeField, Range(0f, 1f)]
    private float buttonChance = 0.20f;

    [SerializeField] private float buttonDetectionRadius = 5f;

    [SerializeField] private float buttonLandingOffset = 0.15f;

    [Header("UI Button Camera")]
    [Tooltip("Camera used to convert UI button screen positions into world positions.")]
    [SerializeField] private Camera uiCamera;

    [Header("Glow")]
    [SerializeField] private SpriteRenderer fireflySprite;
    [SerializeField] private Light2D fireflyLight;

    [SerializeField] private float minimumGlow = 0.15f;
    [SerializeField] private float maximumGlow = 0.45f;
    [SerializeField] private float glowSpeed = 2.5f;

    [Header("Flying Shadow")]
    [SerializeField] private Transform shadow;
    [SerializeField] private SpriteRenderer shadowSprite;

    [SerializeField] private float shadowGroundOffset = 0.12f;

    [SerializeField] private float shadowMinAlpha = 0.12f;
    [SerializeField] private float shadowMaxAlpha = 0.30f;

    [SerializeField] private float shadowMinScale = 0.55f;
    [SerializeField] private float shadowMaxScale = 1f;

    [SerializeField] private float shadowRotation = 0f;

    [Header("Sprite")]
    [SerializeField] private bool flipSprite = true;

    private State currentState = State.Wandering;

    private Vector3 startingPosition;
    private Vector3 targetPosition;
    private Vector3 movementDirection;

    private float directionTimer;
    private float bobTimer;
    private float currentHoverHeight;
    private float currentRotationZ;

    private Tilemap flowerTilemap;

    private readonly List<Button> uiButtons = new List<Button>();


    private void Start()
    {
        startingPosition = transform.position;

        FindFlowerTilemap();

        if (canSitOnUIButtons)
        {
            FindUIButtons();
        }

        ChooseNewDirection();

        bobTimer = Random.Range(
            0f,
            Mathf.PI * 2f
        );

        if (shadow != null)
        {
            shadow.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    shadowRotation
                );
        }
    }


    private void Update()
    {
        UpdateGlow();

        switch (currentState)
        {
            case State.Wandering:
                Wander();
                break;

            case State.GoingToFlower:
                MoveToFlower();
                break;

            case State.GoingToButton:
                MoveToButton();
                break;

            case State.SittingOnFlower:
            case State.SittingOnButton:
                break;
        }

        UpdateFlyingShadow();
    }


    // ============================================================
    // WANDERING
    // ============================================================

    private void Wander()
    {
        directionTimer -= Time.deltaTime;

        if (directionTimer <= 0f)
        {
            ChooseNewDirection();
        }

        bobTimer +=
            Time.deltaTime * bobSpeed;

        currentHoverHeight =
            (Mathf.Sin(bobTimer) + 1f) * 0.5f;

        Vector3 movement =
            movementDirection *
            moveSpeed *
            Time.deltaTime;

        movement.y +=
            Mathf.Sin(bobTimer) *
            bobAmount *
            Time.deltaTime;

        Vector3 nextPosition =
            transform.position + movement;

        Vector3 offset =
            nextPosition - startingPosition;

        if (offset.magnitude > wanderRadius)
        {
            Vector3 directionBack =
                (startingPosition -
                 transform.position).normalized;

            movementDirection =
                Vector3.Lerp(
                    movementDirection,
                    directionBack,
                    0.05f
                );

            movementDirection.Normalize();
        }

        transform.position += movement;

        UpdateRotation();

        FlipSprite();

        // Occasionally try a UI button.
        if (canSitOnUIButtons &&
            Random.value <
            buttonChance * Time.deltaTime)
        {
            if (TryFindButton())
                return;
        }

        // Otherwise try a flower.
        if (Random.value <
            flowerChance * Time.deltaTime)
        {
            TryFindFlower();
        }
    }


    private void ChooseNewDirection()
    {
        Vector2 randomDirection =
            Random.insideUnitCircle.normalized;

        movementDirection =
            new Vector3(
                randomDirection.x,
                randomDirection.y,
                0f
            );

        directionTimer =
            Random.Range(
                directionChangeTime * 0.5f,
                directionChangeTime * 1.5f
            );
    }


    // ============================================================
    // ROTATION
    // ============================================================

    private void UpdateRotation()
    {
        if (movementDirection.sqrMagnitude <
            0.001f)
            return;

        float targetAngle =
            Mathf.Atan2(
                movementDirection.y,
                movementDirection.x
            ) * Mathf.Rad2Deg;

        targetAngle =
            Mathf.Clamp(
                targetAngle,
                -maxRotation,
                maxRotation
            );

        currentRotationZ =
            Mathf.LerpAngle(
                currentRotationZ,
                targetAngle,
                rotationSmoothness *
                Time.deltaTime
            );

        transform.rotation =
            Quaternion.Euler(
                0f,
                0f,
                currentRotationZ
            );
    }


    // ============================================================
    // FLOWERS
    // ============================================================

    private void FindFlowerTilemap()
    {
        GameObject flowerObject =
            GameObject.FindGameObjectWithTag(
                flowerTag
            );

        if (flowerObject == null)
        {
            Debug.LogWarning(
                "Firefly: No GameObject with tag '" +
                flowerTag +
                "' was found."
            );

            return;
        }

        flowerTilemap =
            flowerObject.GetComponent<Tilemap>();

        if (flowerTilemap == null)
        {
            Debug.LogWarning(
                "Firefly: Object tagged '" +
                flowerTag +
                "' does not contain a Tilemap."
            );
        }
    }


    private void TryFindFlower()
    {
        if (flowerTilemap == null)
            return;

        Vector3Int currentCell =
            flowerTilemap.WorldToCell(
                transform.position
            );

        int searchRadius =
            Mathf.CeilToInt(
                flowerDetectionRadius
            );

        Vector3 bestFlowerPosition =
            Vector3.zero;

        float bestDistance =
            Mathf.Infinity;

        bool foundFlower = false;

        for (int x = -searchRadius;
             x <= searchRadius;
             x++)
        {
            for (int y = -searchRadius;
                 y <= searchRadius;
                 y++)
            {
                Vector3Int cell =
                    currentCell +
                    new Vector3Int(x, y, 0);

                if (!flowerTilemap.HasTile(cell))
                    continue;

                Vector3 flowerPosition =
                    flowerTilemap.GetCellCenterWorld(
                        cell
                    );

                float distance =
                    Vector3.Distance(
                        transform.position,
                        flowerPosition
                    );

                if (distance <
                    minimumFlowerDistance)
                    continue;

                if (distance >
                    flowerDetectionRadius)
                    continue;

                if (distance <
                    bestDistance)
                {
                    bestDistance = distance;
                    bestFlowerPosition =
                        flowerPosition;

                    foundFlower = true;
                }
            }
        }

        if (foundFlower)
        {
            targetPosition =
                bestFlowerPosition;

            currentState =
                State.GoingToFlower;
        }
    }


    // ============================================================
    // MOVE TO FLOWER
    // ============================================================

    private void MoveToFlower()
    {
        MoveTowardsTarget(
            targetPosition,
            false
        );
    }


    // ============================================================
    // UI BUTTONS
    // ============================================================

    private void FindUIButtons()
    {
        uiButtons.Clear();

        Button[] buttons =
            FindObjectsByType<Button>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

        foreach (Button button in buttons)
        {
            if (button != null &&
                button.gameObject.activeInHierarchy)
            {
                uiButtons.Add(button);
            }
        }
    }


    private bool TryFindButton()
    {
        if (!canSitOnUIButtons)
            return false;

        if (uiButtons.Count == 0)
            return false;

        Button bestButton = null;

        float bestDistance =
            Mathf.Infinity;

        foreach (Button button in uiButtons)
        {
            if (button == null)
                continue;

            if (!button.gameObject.activeInHierarchy)
                continue;

            RectTransform rect =
                button.GetComponent<RectTransform>();

            if (rect == null)
                continue;

            Vector3 screenPosition =
                RectTransformUtility.WorldToScreenPoint(
                    uiCamera,
                    rect.position
                );

            Vector3 worldPosition =
                ScreenToWorldPosition(
                    screenPosition
                );

            float distance =
                Vector3.Distance(
                    transform.position,
                    worldPosition
                );

            if (distance <
                bestDistance &&
                distance <=
                buttonDetectionRadius)
            {
                bestDistance = distance;
                bestButton = button;
            }
        }

        if (bestButton == null)
            return false;

        RectTransform buttonRect =
            bestButton.GetComponent<RectTransform>();

        Vector3 buttonScreenPosition =
            RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                buttonRect.position
            );

        targetPosition =
            ScreenToWorldPosition(
                buttonScreenPosition
            );

        // Slightly above the button.
        targetPosition.y +=
            buttonLandingOffset;

        currentState =
            State.GoingToButton;

        return true;
    }


    private Vector3 ScreenToWorldPosition(
        Vector3 screenPosition)
    {
        if (uiCamera == null)
        {
            uiCamera =
                Camera.main;
        }

        if (uiCamera == null)
            return transform.position;

        float distance =
            Mathf.Abs(
                transform.position.z -
                uiCamera.transform.position.z
            );

        Vector3 worldPosition =
            uiCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    distance
                )
            );

        worldPosition.z =
            transform.position.z;

        return worldPosition;
    }


    // ============================================================
    // MOVE TO BUTTON
    // ============================================================

    private void MoveToButton()
    {
        MoveTowardsTarget(
            targetPosition,
            true
        );
    }


    private void MoveTowardsTarget(
        Vector3 target,
        bool isButton)
    {
        Vector3 direction =
            (target -
             transform.position).normalized;

        float distance =
            Vector3.Distance(
                transform.position,
                target
            );

        if (distance < 0.08f)
        {
            transform.position =
                target;

            if (isButton)
            {
                StartCoroutine(
                    SitOnButton()
                );
            }
            else
            {
                StartCoroutine(
                    SitOnFlower()
                );
            }

            return;
        }

        bobTimer +=
            Time.deltaTime * bobSpeed;

        currentHoverHeight =
            (Mathf.Sin(bobTimer) + 1f) * 0.5f;

        Vector3 movement =
            direction *
            moveSpeed *
            1.2f *
            Time.deltaTime;

        movement.y +=
            Mathf.Sin(bobTimer) *
            bobAmount *
            0.5f *
            Time.deltaTime;

        transform.position +=
            movement;

        // Smoothly rotate toward destination.
        if (direction.sqrMagnitude >
            0.001f)
        {
            float targetAngle =
                Mathf.Atan2(
                    direction.y,
                    direction.x
                ) * Mathf.Rad2Deg;

            targetAngle =
                Mathf.Clamp(
                    targetAngle,
                    -maxRotation,
                    maxRotation
                );

            currentRotationZ =
                Mathf.LerpAngle(
                    currentRotationZ,
                    targetAngle,
                    rotationSmoothness *
                    Time.deltaTime
                );

            transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    currentRotationZ
                );
        }

        FlipSprite();
    }


    // ============================================================
    // SIT ON FLOWER
    // ============================================================

    private IEnumerator SitOnFlower()
    {
        currentState =
            State.SittingOnFlower;

        currentHoverHeight = 0f;

        float sitTime =
            Random.Range(
                minimumSitTime,
                maximumSitTime
            );

        yield return new WaitForSeconds(
            sitTime
        );

        TakeOff();
    }


    // ============================================================
    // SIT ON BUTTON
    // ============================================================

    private IEnumerator SitOnButton()
    {
        currentState =
            State.SittingOnButton;

        currentHoverHeight = 0f;

        float sitTime =
            Random.Range(
                minimumSitTime,
                maximumSitTime
            );

        yield return new WaitForSeconds(
            sitTime
        );

        TakeOff();
    }


    private void TakeOff()
    {
        transform.position +=
            Vector3.up * 0.05f;

        ChooseNewDirection();

        currentHoverHeight = 0.5f;

        currentState =
            State.Wandering;
    }


    // ============================================================
    // GLOW
    // ============================================================

    private void UpdateGlow()
    {
        float pulse =
            (Mathf.Sin(Time.time * glowSpeed) + 1f) * 0.5f;

        float glowIntensity =
            Mathf.Lerp(
                minimumGlow,
                maximumGlow,
                pulse
            );

        // Light 2D - works on the 2D world.
        if (fireflyLight != null)
        {
            fireflyLight.intensity =
                glowIntensity;
        }

        // Firefly sprite brightness.
        if (fireflySprite != null)
        {
            Color color = fireflySprite.color;

            color.a =
                Mathf.Lerp(
                    0.65f,
                    1f,
                    pulse
                );

            fireflySprite.color = color;
        }

        // Visible glow sprite - works over UI.
        if (glowSprite != null)
        {
            Color glowColor =
                glowSprite.color;

            glowColor.a =
                Mathf.Lerp(
                    0.05f,
                    0.25f,
                    pulse
                );

            glowSprite.color =
                glowColor;

            float scale =
                Mathf.Lerp(
                    0.8f,
                    1.15f,
                    pulse
                );

            glow.localScale =
                Vector3.one * scale;
        }
    }


    // ============================================================
    // SHADOW
    // ============================================================

    private void UpdateFlyingShadow()
    {
        if (shadow == null ||
            shadowSprite == null)
            return;

        Vector3 shadowPosition =
            transform.position;

        shadowPosition.y -=
            shadowGroundOffset;

        shadow.position =
            shadowPosition;

        float height =
            currentHoverHeight;

        float alpha =
            Mathf.Lerp(
                shadowMaxAlpha,
                shadowMinAlpha,
                height
            );

        float scale =
            Mathf.Lerp(
                shadowMaxScale,
                shadowMinScale,
                height
            );

        // Sitting on a button/flower:
        // make the shadow extremely subtle.
        if (currentState ==
            State.SittingOnFlower ||
            currentState ==
            State.SittingOnButton)
        {
            alpha *= 0.2f;
            scale *= 0.8f;
        }

        Color shadowColor =
            shadowSprite.color;

        shadowColor.a =
            alpha;

        shadowSprite.color =
            shadowColor;

        shadow.localScale =
            Vector3.one * scale;

        shadow.rotation =
            Quaternion.Euler(
                0f,
                0f,
                shadowRotation
            );
    }


    // ============================================================
    // SPRITE FLIP
    // ============================================================

    private void FlipSprite()
    {
        if (!flipSprite || fireflySprite == null)
            return;

        if (movementDirection.x > 0.05f)
        {
            fireflySprite.flipX = false;
        }
        else if (movementDirection.x < -0.05f)
        {
            fireflySprite.flipX = true;
        }

        // Make shadow follow the firefly's horizontal flip.
        if (shadowSprite != null)
        {
            shadowSprite.flipX = fireflySprite.flipX;
        }
    }


    // ============================================================
    // GIZMOS
    // ============================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            wanderRadius
        );

        Gizmos.DrawWireSphere(
            transform.position,
            flowerDetectionRadius
        );

        if (canSitOnUIButtons)
        {
            Gizmos.DrawWireSphere(
                transform.position,
                buttonDetectionRadius
            );
        }
    }
}