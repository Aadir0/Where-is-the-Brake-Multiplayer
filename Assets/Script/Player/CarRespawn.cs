using Unity.Netcode;
using UnityEngine;

public class CarRespawn : NetworkBehaviour
{
    private Rigidbody2D rb;
    private CarHealth healthComp;
    private Vector3 lastCheckpointPosition;
    private Quaternion lastCheckpointRotation;
    private bool hasCheckpoint;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        healthComp = GetComponent<CarHealth>();
    }

    public override void OnNetworkSpawn()
    {
        lastCheckpointPosition = transform.position;
        lastCheckpointRotation = transform.rotation;
    }

    public void SetCheckpoint(Vector3 position, Quaternion rotation)
    {
        lastCheckpointPosition = position;
        lastCheckpointRotation = rotation;
        hasCheckpoint = true;
    }

    public void RespawnCarServer()
    {
        if (!IsServer) return;

        Vector3 spawnPos = lastCheckpointPosition;
        Quaternion spawnRot = lastCheckpointRotation;

        if (!hasCheckpoint && PlayerSpawner.Instance != null)
        {
            spawnPos = PlayerSpawner.Instance.GetSpawnPosition((int)OwnerClientId, out spawnRot);
        }

        RespawnClientRpc(spawnPos, spawnRot);

        if (healthComp != null)
        {
            healthComp.ResetHealthAndStateServer();
        }
    }

    [ClientRpc]
    private void RespawnClientRpc(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}

