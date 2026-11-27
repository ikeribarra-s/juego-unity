using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterBase))]
public class CharacterAudio : MonoBehaviour
{
    [Header("Footsteps")]
    [SerializeField] private SoundDefinition _walkSteps;
    [SerializeField] private SoundDefinition _sprintSteps;
    [SerializeField] private SoundDefinition _crouchSteps;

    [Header("Shallow water (WaterVolume)")]
    [SerializeField] private SoundDefinition _waterWalk;
    [SerializeField] private SoundDefinition _waterSprint;
    [Tooltip("Optional; falls back to the water walk set.")]
    [SerializeField] private SoundDefinition _waterCrouch;
    [Tooltip("One-shot when the feet enter the water.")]
    [SerializeField] private SoundDefinition _waterEnter;

    [SerializeField] private float _walkStepDistance   = 2.0f;
    [SerializeField] private float _sprintStepDistance = 2.8f;
    [SerializeField] private float _crouchStepDistance = 1.5f;

    [Tooltip("When sprinting, never fall back to the walk sound — play the sprint clip or nothing.")]
    [SerializeField] private bool _muteWalkWhileSprinting = true;

    [Tooltip("Seconds to fade out a sprint clip when sprint ends, instead of letting it ring to the end.")]
    [SerializeField] private float _sprintFadeOutTime = 0.25f;

    [Header("Actions")]
    [Tooltip("Take-off sound — plays on CharacterMovement.Jumped (only fires for a real grounded jump).")]
    [SerializeField] private SoundDefinition _jump;
    [SerializeField] private SoundDefinition _jumpLand;
    [Tooltip("Fallback only (no AnimationAudioSync): seconds before the predicted impact to pre-fire the land " +
             "sound. 0 = play exactly on touchdown. The prediction ignores gravity, so large values fire far too early.")]
    [SerializeField] private float           _landSoundOffset = 0f;
    [Tooltip("Ignore touchdowns after less airtime than this (isGrounded flicker on stairs/slopes).")]
    [SerializeField] private float           _minLandAirTime = 0.12f;
    [SerializeField] private SoundDefinition _itemPickup;
    [SerializeField] private SoundDefinition _itemDrop;

    [Header("Torch")]
    [SerializeField] private SoundDefinition _torchPickup;   // TorchCarrier.TorchPickedUp
    [SerializeField] private SoundDefinition _torchThrow;    // TorchCarrier.TorchThrown (G)

    [Header("Idle")]
    [Tooltip("Quiet bone creak/rattle while standing still.")]
    [SerializeField] private SoundDefinition _idleCreak;
    [Tooltip("Random seconds between idle creaks (min, max) while the character stays still.")]
    [SerializeField] private Vector2 _idleCreakInterval = new Vector2(6f, 14f);

    [Header("Animation sync")]
    [Tooltip("With an AnimationAudioSync on the model, jump/land wait for the animation's take-off/contact " +
             "frame; if it doesn't arrive within this many seconds they play anyway.")]
    [SerializeField] private float _animSyncTimeout = 0.25f;

    [Header("Debug")]
    [Tooltip("Log every one-shot this component triggers (for testing hooks).")]
    [SerializeField] private bool _debugLog;

    private CharacterBase       _character;
    private CharacterController _controller;
    private AudioSource         _footstepSource;   // dedicated — prevents pool overlap
    private float               _distanceTraveled;
    private float               _prevSpeed;
    private float               _stepCooldown;
    private bool                _wasGrounded;
    private bool                _landSoundFired;   // true once pre-triggered mid-air
    private bool                _lastStepWasSprint;
    private Coroutine           _fadeOut;
    private CharacterMovement   _movement;         // the instance we subscribed Jumped on
    private TorchCarrier        _torch;
    private float               _idleTimer;
    private float               _nextIdleCreak;
    private AnimationAudioSync  _animDriver;       // set → footsteps/jump/land follow the animation frames
    private float               _jumpPending = -1f; // > 0: waiting for the take-off frame (seconds left)
    private float               _lastLandContact = -10f;
    private float               _landPending = -1f; // > 0: waiting for the land contact frame
    private float               _airTime;

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
        _nextIdleCreak = Random.Range(_idleCreakInterval.x, _idleCreakInterval.y);
    }

    private void OnEnable()
    {
        _character.Grabber.ItemGrabbed  += OnItemGrabbed;
        _character.Grabber.ItemReleased += OnItemReleased;

        _movement = _character.Movement;
        if (_movement != null) _movement.Jumped += OnJumped;

        _torch = _character.TorchCarrier;
        if (_torch != null)
        {
            _torch.TorchPickedUp += OnTorchPickedUp;
            _torch.TorchThrown   += OnTorchThrown;
        }
    }

    private void OnDisable()
    {
        _character.Grabber.ItemGrabbed  -= OnItemGrabbed;
        _character.Grabber.ItemReleased -= OnItemReleased;

        if (_movement != null) _movement.Jumped -= OnJumped;
        if (_torch != null)
        {
            _torch.TorchPickedUp -= OnTorchPickedUp;
            _torch.TorchThrown   -= OnTorchThrown;
        }
        _movement = null;
        _torch    = null;
    }

    private void Update()
    {
        _stepCooldown = Mathf.Max(0f, _stepCooldown - Time.deltaTime);
        HandlePendingSync();
        HandleWater();
        HandleLanding();
        HandleFootsteps();
        HandleSprintFadeOut();
        HandleIdleCreak();
        _wasGrounded = _controller.isGrounded;
    }

    // ── Water ─────────────────────────────────────────────────────────────────

    private bool _inWater;

    /// <summary>Raised on every foot contact (animation contact frame, or the distance-based fallback step). Used by WaterSplashEmitter.</summary>
    public event System.Action FootContact;

    private void HandleWater()
    {
        bool now = WaterVolume.TryFind(transform.position) != null;
        if (now && !_inWater) PlayOneShot(_waterEnter, "water enter");
        _inWater = now;
    }

    // ── Landing ───────────────────────────────────────────────────────────────

    private void HandleLanding()
    {
        bool grounded = _controller.isGrounded;
        if (!grounded) _airTime += Time.deltaTime;

        // Just left the ground — arm the pre-trigger
        if (_wasGrounded && !grounded)
        {
            _landSoundFired = false;
            return;
        }

        // Touched down — fire if the pre-trigger didn't already handle it
        if (!_wasGrounded && grounded)
        {
            float air = _airTime;
            _airTime = 0f;
            if (_landSoundFired || air < _minLandAirTime) { _landSoundFired = false; return; }
            _landSoundFired = false;

            if (_debugLog) Debug.Log($"[CharacterAudio] touchdown after {air:0.00} s air, frame {Time.frameCount} t={Time.time:0.000}", this);

            // The animation shows a landing (InAir/JumpLand) → play on its foot-contact frame.
            // Otherwise (short drop that never left the locomotion state) the touchdown *is* the contact.
            // JumpLand starts slightly before touchdown (AnimationHandler._landLead): if its contact frame was already shown, play now.
            if (_animDriver != null && _animDriver.InAirOrLanding && Time.time - _lastLandContact > _animSyncTimeout)
                _landPending = _animSyncTimeout;
            else
                PlayOneShot(_jumpLand, "land");
            return;
        }

        // Fallback without animation sync: pre-trigger when estimated impact time ≤ offset
        if (_animDriver == null && !grounded && !_landSoundFired && _landSoundOffset > 0f)
        {
            float vy = _controller.velocity.y;
            if (vy < 0f && Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 30f))
            {
                float timeToImpact = hit.distance / Mathf.Abs(vy);
                if (timeToImpact <= _landSoundOffset)
                {
                    PlayOneShot(_jumpLand, "land (pre-trigger)");
                    _landSoundFired = true;
                }
            }
        }
    }

    // ── Footsteps ─────────────────────────────────────────────────────────────

    private void HandleFootsteps()
    {
        if (!_controller.isGrounded)
        {
            _distanceTraveled = 0f;
            _prevSpeed        = 0f;

            // Airborne — silence any step clip (walk or sprint) still ringing
            if (_footstepSource.isPlaying && _fadeOut == null)
                _fadeOut = StartCoroutine(FadeOutFootsteps());
            return;
        }

        Vector3 hVel  = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
        float   speed = hVel.magnitude;

        // Animation-driven: AnimationAudioSync calls AnimationFootstep() on the contact frames.
        if (_animDriver != null)
        {
            _prevSpeed = speed;
            return;
        }

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
        if (_animDriver == null) FootContact?.Invoke();   // animation-driven contacts raise it in AnimationFootstep
        if (_stepCooldown > 0f) return;

        var m   = _character.Movement;
        var def = _walkSteps;

        if (m != null)
        {
            if (m.IsSprinting)
                def = _sprintSteps;                          // sprinting never uses the walk sound
            else if (m.IsCrouching && _crouchSteps != null)
                def = _crouchSteps;
        }

        // When sprinting with no sprint clip assigned, stay silent rather than falling back to walk
        if (def == null && !(m != null && m.IsSprinting && _muteWalkWhileSprinting))
            def = _walkSteps;

        if (_inWater)
        {
            SoundDefinition w = m != null && m.IsSprinting ? _waterSprint
                              : m != null && m.IsCrouching ? (_waterCrouch != null ? _waterCrouch : _waterWalk)
                              : _waterWalk;
            if (w != null && w.IsValid) def = w;
        }

        if (def == null || !def.IsValid) return;

        // A fresh step always plays at full volume — cancel any fade in progress
        if (_fadeOut != null)
        {
            StopCoroutine(_fadeOut);
            _fadeOut = null;
        }

        _footstepSource.clip                  = def.GetClip();
        _footstepSource.volume                = def.Volume;
        _footstepSource.pitch                 = def.GetPitch();
        _footstepSource.spatialBlend          = def.SpatialBlend;
        _footstepSource.outputAudioMixerGroup = def.MixerGroup;
        _footstepSource.minDistance           = def.MinDistance;
        _footstepSource.maxDistance           = def.MaxDistance;
        _footstepSource.Play();
        if (_debugLog) Debug.Log($"[CharacterAudio] step {def.name}/{_footstepSource.clip.name}  frame {Time.frameCount} t={Time.time:0.000}", this);

        _lastStepWasSprint = def == _sprintSteps;
        _stepCooldown      = MinStepInterval;
    }

    // ── Sprint fade-out ───────────────────────────────────────────────────────

    private void HandleSprintFadeOut()
    {
        if (!_lastStepWasSprint || _fadeOut != null || !_footstepSource.isPlaying) return;

        var  m         = _character.Movement;
        bool sprinting = m != null && m.IsSprinting;

        Vector3 hVel    = m != null ? m.PlanarVelocity : new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
        bool    stopped = hVel.magnitude < MinMoveSpeed;

        // Shift released (or we stopped) while a sprint clip is still ringing — fade it out
        if (!sprinting || stopped)
            _fadeOut = StartCoroutine(FadeOutFootsteps());
    }

    private IEnumerator FadeOutFootsteps()
    {
        float startVolume = _footstepSource.volume;
        float t           = 0f;

        while (t < _sprintFadeOutTime && _footstepSource.isPlaying)
        {
            t += Time.deltaTime;
            _footstepSource.volume = Mathf.Lerp(startVolume, 0f, t / _sprintFadeOutTime);
            yield return null;
        }

        _footstepSource.Stop();
        _lastStepWasSprint = false;
        _fadeOut           = null;
    }

    // ── Idle creak ────────────────────────────────────────────────────────────

    private void HandleIdleCreak()
    {
        if (_idleCreak == null) return;

        Vector3 hVel  = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
        bool    dead  = _character.Vitals != null && _character.Vitals.IsDead;
        bool    still = _controller.isGrounded && hVel.magnitude < MinMoveSpeed && !dead;

        if (!still)
        {
            _idleTimer = 0f;
            return;
        }

        _idleTimer += Time.deltaTime;
        if (_idleTimer < _nextIdleCreak) return;

        PlayOneShot(_idleCreak, "idle creak");
        _idleTimer     = 0f;
        _nextIdleCreak = Random.Range(_idleCreakInterval.x, _idleCreakInterval.y);
    }

    // ── Animation sync (AnimationAudioSync on the model) ─────────────────────

    public void SetAnimationDriver(AnimationAudioSync driver) => _animDriver = driver;

    public void ClearAnimationDriver(AnimationAudioSync driver)
    {
        if (_animDriver == driver) _animDriver = null;
    }

    /// <summary>Foot-contact frame of the locomotion animation. Picks walk/sprint/crouch from the movement state.</summary>
    public void AnimationFootstep()
    {
        FootContact?.Invoke();
        if (!_controller.isGrounded) return;
        Vector3 hVel = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
        if (hVel.magnitude < MinMoveSpeed) return;
        TriggerStep();
    }

    /// <summary>Take-off frame of JumpStart.</summary>
    public void AnimationTakeoff()
    {
        if (_jumpPending <= 0f) return;      // only for a jump we were told about (not a replayed state)
        _jumpPending = -1f;
        PlayOneShot(_jump, "jump (take-off frame)");
    }

    /// <summary>Foot-contact frame of JumpLand.</summary>
    public void AnimationLandContact()
    {
        if (_landPending <= 0f) { _lastLandContact = Time.time; return; }   // shown before touchdown: HandleLanding plays it there
        _landPending = -1f;
        PlayOneShot(_jumpLand, "land (contact frame)");
    }

    private void HandlePendingSync()
    {
        if (_jumpPending > 0f && (_jumpPending -= Time.deltaTime) <= 0f)
            PlayOneShot(_jump, "jump (sync timeout)");
        if (_landPending > 0f && (_landPending -= Time.deltaTime) <= 0f)
            PlayOneShot(_jumpLand, "land (sync timeout)");
    }

    // ── One-shots (SoundManager pool) ────────────────────────────────────────

    private void PlayOneShot(SoundDefinition def, string label)
    {
        if (def == null) return;
        if (_debugLog) Debug.Log($"[CharacterAudio] {label} -> {def.name}  frame {Time.frameCount} t={Time.time:0.000}", this);
        SoundManager.Instance?.Play(def, transform.position);
    }

    private void OnJumped()
    {
        if (_debugLog) Debug.Log($"[CharacterAudio] Jumped event, frame {Time.frameCount} t={Time.time:0.000}", this);
        if (_animDriver != null) _jumpPending = _animSyncTimeout;   // wait for the JumpStart take-off frame
        else PlayOneShot(_jump, "jump");
    }
    private void OnTorchPickedUp(Torch _)         => PlayOneShot(_torchPickup, "torch pickup");
    private void OnTorchThrown(Torch _)           => PlayOneShot(_torchThrow,  "torch throw");
    private void OnItemGrabbed(EvidencePickup _)  => PlayOneShot(_itemPickup,  "item grab");
    private void OnItemReleased(EvidencePickup _) => PlayOneShot(_itemDrop,    "item release");
}
