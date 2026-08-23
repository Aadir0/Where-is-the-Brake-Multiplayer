using UnityEngine;

public class JumpTrigger : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
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
}
