using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStats", menuName = "Fluffterror/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Movement")]
    public float MoveSpeed            = 5f;
    public float SprintMultiplier     = 1.8f;
    public float Gravity              = -20f;
    public float JumpForce            = 6f;
    public float CrouchSpeedMultiplier = 0.5f;

    [Header("Look")]
    public float LookSensitivity = 0.15f;

    [Header("Inventory")]
    public int InventorySlots = 2;

    [Header("Detection")]
    public float DetectionRadius = 5f;
}
