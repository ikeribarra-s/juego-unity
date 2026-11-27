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
        CharacterBase net = Net.LocalPlayer;   // online: the player object this client owns
        if (net != null) return net;

        Camera main = Camera.main;
        CharacterBase cb = main != null ? main.GetComponentInParent<CharacterBase>() : null;
        return cb != null ? cb : FindAnyObjectByType<CharacterBase>();
    }

    /// <summary>True for another player's copy on this machine (networking). It has no input, camera rig or own movement.</summary>
    public bool IsNetworkRemote { get; private set; }

    /// <summary>
    /// Turn this instance into a remote player's copy. <b>Call before it is activated</b> (PlayerFactory builds it under an inactive
    /// holder): disabled components never get OnEnable, so nothing subscribes to input or grabs the cursor. The remote look
    /// is the TestDummy recipe: inert input, no movement / interaction, camera + listener + reverb + head-bob off, third-person body.
    /// </summary>
    public void ConfigureAsRemote(InputReader inertInput)
    {
        IsNetworkRemote = true;
        if (inertInput != null) _input = inertInput;

        foreach (var m in GetComponents<CharacterMovement>())
        {
            m.enabled = false;                                             // position comes from the owner's NetworkTransform
            m.SetRemoteState(Vector3.zero, 0f, true, false, false);        // state readers (animation, audio) get replicated values from NetworkMotion
        }
        var interaction = GetComponent<InteractionSystem>();
        if (interaction != null) interaction.enabled = false;

        // Camera rig: kept (Grabber / Interaction look one up in Start) but never renders, listens or follows the neck.
        foreach (var cam in GetComponentsInChildren<Camera>(true)) { cam.enabled = false; cam.tag = "Untagged"; }
        foreach (var c in GetComponentsInChildren<AudioListener>(true)) c.enabled = false;
        foreach (var c in GetComponentsInChildren<EnvironmentReverb>(true)) c.enabled = false;
        foreach (var c in GetComponentsInChildren<AudioReverbFilter>(true)) c.enabled = false;
        foreach (var c in GetComponentsInChildren<CameraEffects>(true)) c.enabled = false;
        foreach (var c in GetComponentsInChildren<NeckFollowCamera>(true)) c.enabled = false;

        foreach (var c in GetComponentsInChildren<FirstPersonBodyCull>(true)) c.SetLocalPlayer(false);   // full body
        foreach (var c in GetComponentsInChildren<TorchArmAnimator>(true)) c.SetLocalView(false);        // plain animated arm
        foreach (var c in GetComponentsInChildren<CartoonAnimator>(true)) c.SetRemote();
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
