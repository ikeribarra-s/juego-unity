using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(InventorySystem))]
[RequireComponent(typeof(InteractionSystem))]
public class CharacterBase : MonoBehaviour
{
    [SerializeField] private CharacterDefinition _definition;
    [SerializeField] private InputReader         _input;
    [SerializeField] private Transform           _cameraRoot;

    public CharacterDefinition Definition  => _definition;
    public CharacterStats      Stats       => _definition != null ? _definition.Stats : null;
    public InputReader         Input       => _input;
    public Transform           CameraRoot  => _cameraRoot;

    public CharacterController Controller  { get; private set; }
    public CharacterMovement   Movement    { get; private set; }
    public InventorySystem     Inventory   { get; private set; }
    public InteractionSystem   Interaction { get; private set; }
    public PhysicsGrabber      Grabber     { get; private set; }

    protected virtual void Awake()
    {
        Controller  = GetComponent<CharacterController>();
        Movement    = GetComponent<CharacterMovement>();
        Inventory   = GetComponent<InventorySystem>();
        Interaction = GetComponent<InteractionSystem>();
        Grabber     = GetComponent<PhysicsGrabber>();

        // Auto-add so existing character prefabs work without editor setup;
        // add it manually in the Inspector if you want to tune its values.
        if (Grabber == null)
            Grabber = gameObject.AddComponent<PhysicsGrabber>();

        if (Movement == null)
            Debug.LogError($"[CharacterBase] No CharacterMovement on '{name}'. Set a Definition and let the editor sync the component.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_definition == null) return;
        EditorApplication.delayCall += SyncMovementComponent;
    }

    private void SyncMovementComponent()
    {
        if (this == null || _definition == null) return;

        System.Type wanted = _definition.Movement switch
        {
            CharacterDefinition.MovementClass.Trotin => typeof(TrotinMovement),
            _                                        => typeof(CharacterMovement),
        };

        var current = GetComponent<CharacterMovement>();
        if (current != null && current.GetType() == wanted) return;

        if (current != null)
            Undo.DestroyObjectImmediate(current);

        Undo.AddComponent(gameObject, wanted);
    }
#endif
}
