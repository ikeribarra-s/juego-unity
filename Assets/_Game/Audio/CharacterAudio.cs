using UnityEngine;

[RequireComponent(typeof(CharacterBase))]
public class CharacterAudio : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private SoundDefinition _walkSteps;
    [SerializeField] private SoundDefinition _sprintSteps;
    [SerializeField] private SoundDefinition _crouchSteps;

    [SerializeField] private float _walkStepDistance   = 2.0f;
    [SerializeField] private float _sprintStepDistance = 2.8f;
    [SerializeField] private float _crouchStepDistance = 1.5f;

    [Header("Actions")]
    [SerializeField] private SoundDefinition _jumpLand;
    [SerializeField] private SoundDefinition _itemPickup;
    [SerializeField] private SoundDefinition _itemDrop;

    private CharacterBase       _character;
    private CharacterController _controller;
    private AudioSource         _footstepSource;   // dedicated — prevents pool overlap
    private float               _distanceTraveled;
    private float               _prevSpeed;
    private float               _stepCooldown;
    private bool                _wasGrounded;

    private const float MinMoveSpeed    = 0.1f;
    private const float MinStepInterval = 0.18f;   // hard floor between any two steps

    private void Awake()
    {
        _character      = GetComponent<CharacterBase>();
        _controller     = GetComponent<CharacterController>();
        _footstepSource = gameObject.AddComponent<AudioSource>();
        _footstepSource.playOnAwake   = false;
        _footstepSource.spatialBlend  = 1f;
        _footstepSource.loop          = false;
    }

    private void OnEnable()
    {
        _character.Inventory.ItemAdded   += OnItemAdded;
        _character.Inventory.ItemRemoved += OnItemRemoved;
    }

    private void OnDisable()
    {
        _character.Inventory.ItemAdded   -= OnItemAdded;
        _character.Inventory.ItemRemoved -= OnItemRemoved;
    }

    private void Update()
    {
        _stepCooldown = Mathf.Max(0f, _stepCooldown - Time.deltaTime);
        HandleLanding();
        HandleFootsteps();
        _wasGrounded = _controller.isGrounded;
    }

    // ── Landing ───────────────────────────────────────────────────────────────

    private void HandleLanding()
    {
        if (!_wasGrounded && _controller.isGrounded)
            SoundManager.Instance?.Play(_jumpLand, transform.position);
    }

    // ── Footsteps ─────────────────────────────────────────────────────────────

    private void HandleFootsteps()
    {
        if (!_controller.isGrounded)
        {
            _distanceTraveled = 0f;
            _prevSpeed        = 0f;
            return;
        }

        Vector3 hVel  = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
        float   speed = hVel.magnitude;

        bool wasMoving    = _prevSpeed >= MinMoveSpeed;
        bool isMoving     = speed      >= MinMoveSpeed;
        bool decelerating = speed < _prevSpeed - 0.01f; // small epsilon avoids float jitter

        // Stopped — pre-load counter so the next start fires immediately
        if (!isMoving)
        {
            _distanceTraveled = GetStepDistance();
            _prevSpeed        = speed;
            return;
        }

        // Decelerating — cut off cleanly; reset so no late step fires as we slow down
        if (decelerating)
        {
            _distanceTraveled = 0f;
            _prevSpeed        = speed;
            return;
        }

        // Transitioning from stopped → moving: fire the first step without any delay
        if (!wasMoving)
        {
            TriggerStep();
            _distanceTraveled = 0f;
            _prevSpeed        = speed;
            return;
        }

        // Normal movement: accumulate distance, trigger at threshold
        _distanceTraveled += speed * Time.deltaTime;

        if (_distanceTraveled >= GetStepDistance())
        {
            _distanceTraveled = 0f;
            TriggerStep();
        }

        _prevSpeed = speed;
    }

    private float GetStepDistance()
    {
        var m = _character.Movement;
        if (m == null)     return _walkStepDistance;
        if (m.IsSprinting) return _sprintStepDistance;
        if (m.IsCrouching) return _crouchStepDistance;
        return _walkStepDistance;
    }

    private void TriggerStep()
    {
        if (_stepCooldown > 0f) return;

        var m   = _character.Movement;
        var def = _walkSteps;

        if (m != null)
        {
            if      (m.IsSprinting && _sprintSteps != null) def = _sprintSteps;
            else if (m.IsCrouching && _crouchSteps != null) def = _crouchSteps;
        }

        if (def == null || !def.IsValid) return;

        _footstepSource.clip                  = def.GetClip();
        _footstepSource.volume                = def.Volume;
        _footstepSource.pitch                 = def.GetPitch();
        _footstepSource.spatialBlend          = def.SpatialBlend;
        _footstepSource.outputAudioMixerGroup = def.MixerGroup;
        _footstepSource.Play();

        _stepCooldown = MinStepInterval;
    }

    // ── Inventory ─────────────────────────────────────────────────────────────

    private void OnItemAdded(EvidencePickup _)   => SoundManager.Instance?.Play(_itemPickup, transform.position);
    private void OnItemRemoved(EvidencePickup _) => SoundManager.Instance?.Play(_itemDrop,   transform.position);
}
