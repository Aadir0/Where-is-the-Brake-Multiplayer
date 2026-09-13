using System.Collections;
using UnityEngine;

public class FrogHoleJumper : MonoBehaviour
{
    [Header("Jump Physics & Motion")]
    [SerializeField] private float jumpDuration = 0.85f;
    [SerializeField] private float jumpHeight = 1.6f;
    [SerializeField] private float jumpDistance = 1.2f;

    [Header("Visuals & Sorting")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int jumpSortingOrder = 30;
    [SerializeField] private Sprite[] jumpFrames;

    [Header("Animator Parameter & State Names")]
    [SerializeField] private string jumpTriggerName = "Jump";
    [SerializeField] private string jumpBoolName = "isJumping";
    [SerializeField] private string idleTriggerName = "Idle";
    [SerializeField] private string idleBoolName = "isIdle";
    [SerializeField] private bool syncDurationToAnimationClip = false;

    [Header("Effects & Audio")]
    [SerializeField] private GameObject splashEffectPrefab;
    [SerializeField] private AudioClip croakOrSplashSound;

    private Vector3 startPosition;
    private Vector3 peakPosition;
    private Vector3 landPosition;
    private bool isJumping;
    private Coroutine jumpCoroutine;
    private Vector3 initialScale;

    private void Awake()
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        animator ??= GetComponent<Animator>() ?? GetComponentInChildren<Animator>();

        if (spriteRenderer != null)
        {
            if (!string.IsNullOrEmpty(sortingLayerName))
            {
                spriteRenderer.sortingLayerName = sortingLayerName;
            }
            spriteRenderer.sortingOrder = jumpSortingOrder;
        }

        initialScale = transform.localScale;
        PlayIdleAnimation();
    }

    public void PlayIdleAnimation()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;

        if (!string.IsNullOrEmpty(idleBoolName)) animator.SetBool(idleBoolName, true);
        if (!string.IsNullOrEmpty(jumpBoolName)) animator.SetBool(jumpBoolName, false);
        if (!string.IsNullOrEmpty(idleTriggerName)) animator.SetTrigger(idleTriggerName);
    }

    public void PlayJumpAnimation()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;

        if (!string.IsNullOrEmpty(idleBoolName)) animator.SetBool(idleBoolName, false);
        if (!string.IsNullOrEmpty(jumpBoolName)) animator.SetBool(jumpBoolName, true);
        if (!string.IsNullOrEmpty(jumpTriggerName)) animator.SetTrigger(jumpTriggerName);

        if (syncDurationToAnimationClip)
        {
            AnimatorClipInfo[] clipInfo = animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfo != null && clipInfo.Length > 0 && clipInfo[0].clip != null)
            {
                jumpDuration = clipInfo[0].clip.length;
            }
        }
    }

    public void LaunchJumpToTarget(Vector3 spawnPoint, Vector3 destinationPoint)
    {
        startPosition = spawnPoint;
        landPosition = destinationPoint;

        float dist = Vector3.Distance(startPosition, landPosition);
        float dynamicHeight = Mathf.Max(jumpHeight, dist * 0.45f);
        peakPosition = (startPosition + landPosition) * 0.5f + Vector3.up * dynamicHeight;

        transform.position = startPosition;
        gameObject.SetActive(true);

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.sortingLayerName = sortingLayerName;
            spriteRenderer.sortingOrder = jumpSortingOrder;
            spriteRenderer.flipX = (landPosition.x - startPosition.x) < 0f;
            if (jumpFrames != null && jumpFrames.Length > 0)
            {
                spriteRenderer.sprite = jumpFrames[0];
            }
        }

        if (croakOrSplashSound != null)
        {
            float vol = AudioManager.Instance != null ? AudioManager.Instance.GetSfxVolume() : 0.8f;
            AudioSource.PlayClipAtPoint(croakOrSplashSound, transform.position, vol);
        }

        if (jumpCoroutine != null) StopCoroutine(jumpCoroutine);
        jumpCoroutine = StartCoroutine(JumpRoutine());
    }

    public void LaunchJump(Vector3 spawnPoint, Vector2 jumpDirection)
    {
        startPosition = spawnPoint;
        jumpDirection = jumpDirection.sqrMagnitude < 0.01f ? Random.insideUnitCircle.normalized : jumpDirection.normalized;

        landPosition = startPosition + (Vector3)(jumpDirection * jumpDistance);
        LaunchJumpToTarget(startPosition, landPosition);
    }

    private IEnumerator JumpRoutine()
    {
        isJumping = true;
        PlayJumpAnimation();

        float elapsed = 0f;

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / jumpDuration);

            if (jumpFrames != null && jumpFrames.Length > 0 && spriteRenderer != null && animator == null)
            {
                int frameIndex = Mathf.Clamp(Mathf.FloorToInt(t * jumpFrames.Length), 0, jumpFrames.Length - 1);
                spriteRenderer.sprite = jumpFrames[frameIndex];
            }

            Vector3 m1 = Vector3.Lerp(startPosition, peakPosition, t);
            Vector3 m2 = Vector3.Lerp(peakPosition, landPosition, t);
            transform.position = Vector3.Lerp(m1, m2, t);

            float verticalStretch = 1f + Mathf.Sin(t * Mathf.PI) * 0.25f;
            float horizontalSquash = 1f / Mathf.Sqrt(verticalStretch);
            transform.localScale = new Vector3(initialScale.x * horizontalSquash, initialScale.y * verticalStretch, initialScale.z);

            yield return null;
        }

        transform.position = landPosition;
        transform.localScale = initialScale;

        PlayIdleAnimation();

        if (splashEffectPrefab != null)
        {
            GameObject splash = Instantiate(splashEffectPrefab, landPosition, Quaternion.identity);
            Destroy(splash, 1.5f);
        }

        isJumping = false;
        gameObject.SetActive(false);
    }
}