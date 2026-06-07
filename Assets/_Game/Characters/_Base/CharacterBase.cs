using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(InventorySystem))]
[RequireComponent(typeof(InteractionSystem))]
public class CharacterBase : MonoBehaviour
{
    [SerializeField] private CharacterStats _stats;
    [SerializeField] private InputReader    _input;

    public CharacterStats    Stats       => _stats;
    public InputReader       Input       => _input;

    public CharacterController Controller  { get; private set; }
    public CharacterMovement   Movement    { get; private set; }
    public InventorySystem     Inventory   { get; private set; }
    public InteractionSystem   Interaction { get; private set; }

    protected virtual void Awake()
    {
        Controller  = GetComponent<CharacterController>();
        Movement    = GetComponent<CharacterMovement>();
        Inventory   = GetComponent<InventorySystem>();
        Interaction = GetComponent<InteractionSystem>();
    }
}
