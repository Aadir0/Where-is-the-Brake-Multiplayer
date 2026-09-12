using UnityEngine;

public class AutoDisableOnAnimationEnd : MonoBehaviour
{
    [SerializeField] private bool destroyGameObject = true;
    [SerializeField] private float fallbackTimeout = 2.0f;

    private Animator animator;
    private ParticleSystem particleSys;
    private float timer = 0f;
    private bool animationStarted = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        particleSys = GetComponent<ParticleSystem>();
        if (particleSys == null) particleSys = GetComponentInChildren<ParticleSystem>();
    }

    private void OnEnable()
    {
        timer = 0f;
        animationStarted = false;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        // Check Animator state completion
        if (animator != null && animator.isActiveAndEnabled)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.normalizedTime > 0.05f)
            {
                animationStarted = true;
            }

            if (animationStarted && stateInfo.normalizedTime >= 1.0f)
            {
                FinishEffect();
                return;
            }
        }

        // Check ParticleSystem state completion
        if (particleSys != null)
        {
            if (!particleSys.IsAlive(true) && timer > 0.1f)
            {
                FinishEffect();
                return;
            }
        }

        // Fallback safety timeout
        if (timer >= fallbackTimeout)
        {
            FinishEffect();
        }
    }

    private void FinishEffect()
    {
        if (destroyGameObject)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}

