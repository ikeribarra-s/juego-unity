using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterMovement))]
public class CharacterBase : MonoBehaviour
{
    [SerializeField] private CharacterStats _stats;
    [SerializeField] private InputReader    _input;

    public CharacterStats Stats => _stats;
    public InputReader    Input => _input;

    public CharacterController Controller { get; private set; }
    public CharacterMovement   Movement   { get; private set; }

    protected virtual void Awake()
    {
        Controller = GetComponent<CharacterController>();
        Movement   = GetComponent<CharacterMovement>();
    }
}
