using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class ShieldPickup : NetworkBehaviour
{
    [Header("Shield Pickup Settings")]
    [SerializeField] private float shieldDuration = 2.0f;
    [SerializeField] private GameObject pickupFxPrefab;
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Wobble / Floating Animation")]
    [SerializeField] private bool enableWobble = true;
    [SerializeField] private float wobbleSpeed = 3.5f;
    [SerializeField] private float wobbleHeight = 0.18f;

    private bool isCollected = false;
    private Vector3 initialPosition;

    private void Awake()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        isCollected = false;
        initialPosition = transform.position;
    }

    private void Update()
    {
        // Gentle rotation animation
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        // Smooth up-down wobble (bobbing) effect
        if (enableWobble)
        {
            float newY = initialPosition.y + Mathf.Sin(Time.time * wobbleSpeed) * wobbleHeight;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;

        CarHealth health = other.GetComponentInParent<CarHealth>();
        if (health == null) health = other.GetComponent<CarHealth>();

        if (health != null && !health.isDead.Value)
        {
            isCollected = true;

            // Trigger shield effect on player across network
            health.ActivateShieldRpc(shieldDuration);

            // Play pickup particle effect
            if (pickupFxPrefab != null)
            {
                GameObject fx = Instantiate(pickupFxPrefab, transform.position, Quaternion.identity);
                Destroy(fx, 2.5f);
            }

            // Notify server spawner to despawn & schedule next random spawn
            if (IsServer)
            {
                if (ShieldPickupSpawner.Instance != null)
                {
                    ShieldPickupSpawner.Instance.OnPickupCollectedServer(gameObject);
                }
                else
                {
                    NetworkObject netObj = GetComponent<NetworkObject>();
                    if (netObj != null && netObj.IsSpawned)
                    {
                        netObj.Despawn(true);
                    }
                }
            }
        }
    }
}
