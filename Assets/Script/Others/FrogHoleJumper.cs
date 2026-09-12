using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private string sortingLayerName = "Background";
    [SerializeField] private int jumpSortingOrder = 0;
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
    private bool isJumping = false;
    private Coroutine jumpCoroutine;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (spriteRenderer != null)
        {
            if (!string.IsNullOrEmpty(sortingLayerName))
            {
                spriteRenderer.sortingLayerName = sortingLayerName;
            }
            spriteRenderer.sortingOrder = jumpSortingOrder;
        }

        PlayIdleAnimation();
    }

    public void PlayIdleAnimation()
    {
        if (animator != null && animator.isActiveAndEnabled)
        {
            if (!string.IsNullOrEmpty(idleBoolName)) animator.SetBool(idleBoolName, true);
            if (!string.IsNullOrEmpty(jumpBoolName)) animator.SetBool(jumpBoolName, false);
            if (!string.IsNullOrEmpty(idleTriggerName)) animator.SetTrigger(idleTriggerName);
        }
    }
    public void PlayJumpAnimation()
    {
        if (animator != null && animator.isActiveAndEnabled)
        {
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
    }
    public void LaunchJump(Vector3 spawnPoint, Vector2 jumpDirection)
    {
        startPosition = spawnPoint;
        if (jumpDirection.sqrMagnitude < 0.01f)
        {
            jumpDirection = Random.insideUnitCircle.normalized;
        }
        else
        {
            jumpDirection = jumpDirection.normalized;
        }

        landPosition = startPosition + (Vector3)(jumpDirection * jumpDistance);
        peakPosition = (startPosition + landPosition) * 0.5f + Vector3.up * jumpHeight;

        transform.position = startPosition;
        gameObject.SetActive(true);

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.sortingOrder = jumpSortingOrder;
            spriteRenderer.flipX = jumpDirection.x < 0f;
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

    private IEnumerator JumpRoutine()
    {
        isJumping = true;
        PlayJumpAnimation();

        float elapsed = 0f;
        Vector3 initialScale = transform.localScale;

        while (elapsed < jumpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / jumpDuration);

            // Synchronize frame animation to trajectory progress t if jumpFrames exist
            if (jumpFrames != null && jumpFrames.Length > 0 && spriteRenderer != null && animator == null)
            {
                int frameIndex = Mathf.Clamp(Mathf.FloorToInt(t * jumpFrames.Length), 0, jumpFrames.Length - 1);
                spriteRenderer.sprite = jumpFrames[frameIndex];
            }

            // Parabolic Bézier curve jump trajectory above water
            Vector3 m1 = Vector3.Lerp(startPosition, peakPosition, t);
            Vector3 m2 = Vector3.Lerp(peakPosition, landPosition, t);
            transform.position = Vector3.Lerp(m1, m2, t);

            // Subtle squash & stretch during jump
            float verticalStretch = 1f + Mathf.Sin(t * Mathf.PI) * 0.25f;
            float horizontalSquash = 1f / Mathf.Sqrt(verticalStretch);
            transform.localScale = new Vector3(initialScale.x * horizontalSquash, initialScale.y * verticalStretch, initialScale.z);

            yield return null;
        }

        transform.position = landPosition;
        transform.localScale = initialScale;

        PlayIdleAnimation();

        // Spawn optional splash on landing back in hole/water
        if (splashEffectPrefab != null)
        {
            GameObject splash = Instantiate(splashEffectPrefab, landPosition, Quaternion.identity);
            Destroy(splash, 1.5f);
        }

        isJumping = false;
        gameObject.SetActive(false);
    }
}

