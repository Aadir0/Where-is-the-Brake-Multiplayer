using System;
using UnityEngine;

public class LevelJumpSettings : MonoBehaviour
{
    public static LevelJumpSettings Instance { get; private set; }

    [Header("Per-Level Jump Settings")]
    [Tooltip("Enable to override the car's default jump duration and jump cooldown for this scene.")]
    [SerializeField] private bool enableJumpOverride = true;
    [SerializeField, Min(0.1f)] private float levelJumpDuration = 0.35f;
    [SerializeField, Min(0f)] private float levelJumpCooldown = 1.2f;

    [Header("Continuous Jumping")]
    [Tooltip("When enabled, the car will automatically keep jumping while inside a JumpTrigger zone (no button press needed after the first). Great for levels with long jump sections.")]
    [SerializeField] private bool continuousJump = false;

    [Header("Per-Level Speed & Steering Settings")]
    [Tooltip("Enable to override the car's default speed and turn speed for this scene.")]
    [SerializeField] private bool enableSpeedOverride = false;
    [SerializeField, Min(1f)] private float levelSpeed = 8.5f;
    [SerializeField, Min(10f)] private float levelTurnSpeed = 220f;

    public bool EnableJumpOverride => enableJumpOverride;
    public float LevelJumpDuration => levelJumpDuration;
    public float LevelJumpCooldown => levelJumpCooldown;
    public bool ContinuousJump => continuousJump;

    public bool EnableSpeedOverride => enableSpeedOverride;
    public float LevelSpeed => levelSpeed;
    public float LevelTurnSpeed => levelTurnSpeed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
