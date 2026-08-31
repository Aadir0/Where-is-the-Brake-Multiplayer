using UnityEngine;

public class JumpTrigger : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryEnableJump(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Catches the case where the player lands back inside this trigger
        // after a jump (layer changed back to playerLayer while still overlapping)
        TryEnableJump(other);
    }

    private void TryEnableJump(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        NetworkCarController netCarController = other.GetComponent<NetworkCarController>();
        if (netCarController != null)
        {
            netCarController.EnableJump();
        }

        CarControllerSingle singleCarController = other.GetComponent<CarControllerSingle>();
        if (singleCarController != null)
        {
            singleCarController.EnableJump();
        }
    }
}
