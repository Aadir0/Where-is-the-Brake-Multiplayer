using System;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class JumpTrigger : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            CarControllerSingle carController = other.GetComponent<CarControllerSingle>();

            if (carController != null)
            {
                carController.EnableJump();
            }
        }
    }
}
