using Unity.Netcode;
using Unity.Netcode.Components;
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

        if (IsOwner && !IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            UpdateCheckpointServerRpc(position, rotation);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpdateCheckpointServerRpc(Vector3 position, Quaternion rotation)
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

        RespawnClientRpc(spawnPos, spawnRot);

        if (healthComp != null)
        {
            healthComp.ResetHealthAndStateServer();
        }
    }

    public void RespawnCarLocal(Vector3 position, Quaternion rotation)
    {
        if (IsOwner || IsLocalPlayer)
        {
            NetworkTransform netTransform = GetComponent<NetworkTransform>();
            if (netTransform != null && netTransform.CanCommitToTransform)
            {
                netTransform.Teleport(position, rotation, transform.localScale);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            if (rb != null)
            {
                rb.position = position;
                rb.rotation = rotation.eulerAngles.z;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
        }

        NetworkCarController.UpdateAllCarsSceneVisibility();
    }

    public void RespawnCarLocal()
    {
        Vector3 position = hasCheckpoint ? lastCheckpointPosition : transform.position;
        Quaternion rotation = hasCheckpoint ? lastCheckpointRotation : transform.rotation;
        RespawnCarLocal(position, rotation);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    public void RespawnClientRpc(Vector3 position, Quaternion rotation)
    {
        if (IsOwner || IsLocalPlayer)
        {
            if (healthComp != null && (healthComp.isDead.Value || healthComp.LocalDeathRequested))
            {
                RespawnCarLocal(position, rotation);
            }
        }
        else
        {
            RespawnCarLocal(position, rotation);
        }
    }
}

