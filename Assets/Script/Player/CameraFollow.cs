using Unity.Netcode;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public string targetTag = "Player";
    public Vector3 offset = new Vector3(0f, 5f, -10f);

    [Header("Follow Settings")]
    public float smoothSpeed = 5f;
    public bool lookAtTarget = false;

    private Transform target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    void Start()
    {
        FindTargetByTag();
    }

    void FindTargetByTag()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(targetTag);
        foreach (GameObject player in players)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                target = player.transform;
                return;
            }
        }

        if (players.Length > 0 && target == null)
        {
            target = players[0].transform;
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            FindTargetByTag();
            return;
        }

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        if (lookAtTarget)
        {
            transform.LookAt(target);
        }
    }
}