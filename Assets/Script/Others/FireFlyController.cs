using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

public class FireflyController : MonoBehaviour
{
    private enum State
    {
        Wandering,
        GoingToFlower,
        SittingOnFlower
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

    private void Start()
    {
        startingPosition = transform.position;

        FindFlowerTilemap();

        ChooseNewDirection();

        bobTimer = Random.Range(0f, Mathf.PI * 2f);

        if (shadow != null)
        {
            shadow.rotation = Quaternion.Euler(0f, 0f, shadowRotation);
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

            case State.SittingOnFlower:
                break;
        }

        UpdateFlyingShadow();
    }

    private void Wander()
    {
        directionTimer -= Time.deltaTime;

        if (directionTimer <= 0f)
        {
            ChooseNewDirection();
        }

        bobTimer += Time.deltaTime * bobSpeed;

        currentHoverHeight = (Mathf.Sin(bobTimer) + 1f) * 0.5f;

        Vector3 movement = movementDirection * moveSpeed * Time.deltaTime;
        movement.y += Mathf.Sin(bobTimer) * bobAmount * Time.deltaTime;

        Vector3 nextPosition = transform.position + movement;
        Vector3 offset = nextPosition - startingPosition;

        if (offset.magnitude > wanderRadius)
        {
            Vector3 directionBack = (startingPosition - transform.position).normalized;
            movementDirection = Vector3.Lerp(movementDirection, directionBack, 0.05f);
            movementDirection.Normalize();
        }

        transform.position += movement;

        UpdateRotation();
        FlipSprite();

        if (Random.value < flowerChance * Time.deltaTime)
        {
            TryFindFlower();
        }
    }

    private void ChooseNewDirection()
    {
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        movementDirection = new Vector3(randomDirection.x, randomDirection.y, 0f);
        directionTimer = Random.Range(directionChangeTime * 0.5f, directionChangeTime * 1.5f);
    }

    private void UpdateRotation()
    {
        if (movementDirection.sqrMagnitude < 0.001f) return;

        float targetAngle = Mathf.Atan2(movementDirection.y, movementDirection.x) * Mathf.Rad2Deg;
        targetAngle = Mathf.Clamp(targetAngle, -maxRotation, maxRotation);

        currentRotationZ = Mathf.LerpAngle(currentRotationZ, targetAngle, rotationSmoothness * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, currentRotationZ);
    }

    private void FindFlowerTilemap()
    {
        GameObject flowerObject = GameObject.FindGameObjectWithTag(flowerTag);

        if (flowerObject == null) return;

        flowerTilemap = flowerObject.GetComponent<Tilemap>();
    }

    private void TryFindFlower()
    {
        if (flowerTilemap == null) return;

        Vector3Int currentCell = flowerTilemap.WorldToCell(transform.position);
        int searchRadius = Mathf.CeilToInt(flowerDetectionRadius);

        Vector3 bestFlowerPosition = Vector3.zero;
        float bestDistance = Mathf.Infinity;
        bool foundFlower = false;

        for (int x = -searchRadius; x <= searchRadius; x++)
        {
            for (int y = -searchRadius; y <= searchRadius; y++)
            {
                Vector3Int cell = currentCell + new Vector3Int(x, y, 0);

                if (!flowerTilemap.HasTile(cell)) continue;

                Vector3 flowerPosition = flowerTilemap.GetCellCenterWorld(cell);
                float distance = Vector3.Distance(transform.position, flowerPosition);

                if (distance < minimumFlowerDistance) continue;
                if (distance > flowerDetectionRadius) continue;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestFlowerPosition = flowerPosition;
                    foundFlower = true;
                }
            }
        }

        if (foundFlower)
        {
            targetPosition = bestFlowerPosition;
            currentState = State.GoingToFlower;
        }
    }

    private void MoveToFlower()
    {
        MoveTowardsTarget(targetPosition);
    }

    private void MoveTowardsTarget(Vector3 target)
    {
        Vector3 direction = (target - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, target);

        if (distance < 0.08f)
        {
            transform.position = target;
            StartCoroutine(SitOnFlower());
            return;
        }

        bobTimer += Time.deltaTime * bobSpeed;
        currentHoverHeight = (Mathf.Sin(bobTimer) + 1f) * 0.5f;

        Vector3 movement = direction * moveSpeed * 1.2f * Time.deltaTime;
        movement.y += Mathf.Sin(bobTimer) * bobAmount * 0.5f * Time.deltaTime;

        transform.position += movement;

        if (direction.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            targetAngle = Mathf.Clamp(targetAngle, -maxRotation, maxRotation);

            currentRotationZ = Mathf.LerpAngle(currentRotationZ, targetAngle, rotationSmoothness * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, currentRotationZ);
        }

        FlipSprite();
    }

    private IEnumerator SitOnFlower()
    {
        currentState = State.SittingOnFlower;
        currentHoverHeight = 0f;

        float sitTime = Random.Range(minimumSitTime, maximumSitTime);
        yield return new WaitForSeconds(sitTime);

        TakeOff();
    }

    private void TakeOff()
    {
        transform.position += Vector3.up * 0.05f;
        ChooseNewDirection();
        currentHoverHeight = 0.5f;
        currentState = State.Wandering;
    }

    private void UpdateGlow()
    {
        float pulse = (Mathf.Sin(Time.time * glowSpeed) + 1f) * 0.5f;
        float glowIntensity = Mathf.Lerp(minimumGlow, maximumGlow, pulse);

        if (fireflyLight != null)
        {
            fireflyLight.intensity = glowIntensity;
        }

        if (fireflySprite != null)
        {
            Color color = fireflySprite.color;
            color.a = Mathf.Lerp(0.65f, 1f, pulse);
            fireflySprite.color = color;
        }
    }

    private void UpdateFlyingShadow()
    {
        if (shadow == null || shadowSprite == null) return;

        Vector3 shadowPosition = transform.position;
        shadowPosition.y -= shadowGroundOffset;
        shadow.position = shadowPosition;

        float height = currentHoverHeight;
        float alpha = Mathf.Lerp(shadowMaxAlpha, shadowMinAlpha, height);
        float scale = Mathf.Lerp(shadowMaxScale, shadowMinScale, height);

        if (currentState == State.SittingOnFlower)
        {
            alpha *= 0.2f;
            scale *= 0.8f;
        }

        Color shadowColor = shadowSprite.color;
        shadowColor.a = alpha;
        shadowSprite.color = shadowColor;

        shadow.localScale = Vector3.one * scale;
        shadow.rotation = Quaternion.Euler(0f, 0f, shadowRotation);
    }

    private void FlipSprite()
    {
        if (!flipSprite || fireflySprite == null) return;

        if (movementDirection.x > 0.05f)
        {
            fireflySprite.flipX = false;
        }
        else if (movementDirection.x < -0.05f)
        {
            fireflySprite.flipX = true;
        }

        if (shadowSprite != null)
        {
            shadowSprite.flipX = fireflySprite.flipX;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
        Gizmos.DrawWireSphere(transform.position, flowerDetectionRadius);
    }
}