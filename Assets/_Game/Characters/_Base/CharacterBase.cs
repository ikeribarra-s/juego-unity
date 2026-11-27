using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(InventorySystem))]
[RequireComponent(typeof(InteractionSystem))]
// Awake first: it auto-adds PhysicsGrabber / Vitals / TorchCarrier … that siblings (CharacterAudio.OnEnable) subscribe to.
// Component order alone is not guaranteed (an Instantiate'd copy — TestScene's TestDummy — ran CharacterAudio first).
[DefaultExecutionOrder(-100)]
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
    public CharacterVitals     Vitals      { get; private set; }
    public TorchCarrier        TorchCarrier { get; private set; }

    /// <summary>
    /// The character the local player controls: the one whose child camera is <c>Camera.main</c> (falls back to any
    /// CharacterBase). Use instead of <c>FindAnyObjectByType&lt;CharacterBase&gt;()</c> now that a scene can hold
    /// several characters (TestScene's TestDummy; Phase 4 replaces this with the NGO owner).
    /// </summary>
    public static CharacterBase FindLocal()
    {
        Camera main = Camera.main;
        CharacterBase cb = main != null ? main.GetComponentInParent<CharacterBase>() : null;
        return cb != null ? cb : FindAnyObjectByType<CharacterBase>();
    }

    protected virtual void Awake()
    {
        Controller  = GetComponent<CharacterController>();
        Movement    = GetComponent<CharacterMovement>();
        Inventory   = GetComponent<InventorySystem>();
        Interaction = GetComponent<InteractionSystem>();
        Grabber      = GetComponent<PhysicsGrabber>();
        Vitals       = GetComponent<CharacterVitals>();
        TorchCarrier = GetComponent<TorchCarrier>();

        // Auto-add so existing character prefabs work without editor setup;
        // add it manually in the Inspector if you want to tune its values.
        if (Grabber == null)
            Grabber = gameObject.AddComponent<PhysicsGrabber>();
        if (Vitals == null)
            Vitals = gameObject.AddComponent<CharacterVitals>();
        if (TorchCarrier == null)
            TorchCarrier = gameObject.AddComponent<TorchCarrier>();
        if (GetComponent<WaterSplashEmitter>() == null)
            gameObject.AddComponent<WaterSplashEmitter>();
        if (GetComponent<SkeletonDisassembler>() == null)
            gameObject.AddComponent<SkeletonDisassembler>();   // death: bones scatter, skull lives on

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
