using System.Collections;
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

    [Tooltip("When sprinting, never fall back to the walk sound — play the sprint clip or nothing.")]
    [SerializeField] private bool _muteWalkWhileSprinting = true;

    [Tooltip("Seconds to fade out a sprint clip when sprint ends, instead of letting it ring to the end.")]
    [SerializeField] private float _sprintFadeOutTime = 0.25f;

    [Header("Actions")]
    [SerializeField] private SoundDefinition _jumpLand;
    [SerializeField] private float           _landSoundOffset = 0.12f; // seconds before impact to start playing
    [SerializeField] private SoundDefinition _itemPickup;
    [SerializeField] private SoundDefinition _itemDrop;

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
        _character.Grabber.ItemGrabbed  += OnItemGrabbed;
        _character.Grabber.ItemReleased += OnItemReleased;
    }

    private void OnDisable()
    {
        _character.Grabber.ItemGrabbed  -= OnItemGrabbed;
        _character.Grabber.ItemReleased -= OnItemReleased;
    }

    private void Update()
    {
        _stepCooldown = Mathf.Max(0f, _stepCooldown - Time.deltaTime);
        HandleLanding();
        HandleFootsteps();
        HandleSprintFadeOut();
        _wasGrounded = _controller.isGrounded;
    }

    // ── Landing ───────────────────────────────────────────────────────────────

    private void HandleLanding()
    {
        // Just left the ground — arm the pre-trigger
        if (_wasGrounded && !_controller.isGrounded)
        {
            _landSoundFired = false;
            return;
        }

        // Touched down — fire if the pre-trigger didn't already handle it
        if (!_wasGrounded && _controller.isGrounded)
        {
            if (!_landSoundFired)
                SoundManager.Instance?.Play(_jumpLand, transform.position);
            _landSoundFired = false;
            return;
        }

        // In the air and falling — pre-trigger when estimated impact time ≤ offset
        if (!_controller.isGrounded && !_landSoundFired && _landSoundOffset > 0f)
        {
            float vy = _controller.velocity.y;
            if (vy < 0f && Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 30f))
            {
                float timeToImpact = hit.distance / Mathf.Abs(vy);
                if (timeToImpact <= _landSoundOffset)
                {
                    SoundManager.Instance?.Play(_jumpLand, transform.position);
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
            if (m.IsSprinting)
                def = _sprintSteps;                          // sprinting never uses the walk sound
            else if (m.IsCrouching && _crouchSteps != null)
                def = _crouchSteps;
        }

        // When sprinting with no sprint clip assigned, stay silent rather than falling back to walk
        if (def == null && !(m != null && m.IsSprinting && _muteWalkWhileSprinting))
            def = _walkSteps;

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
        _footstepSource.Play();

        _lastStepWasSprint = def == _sprintSteps;
        _stepCooldown      = MinStepInterval;
    }

    // ── Sprint fade-out ───────────────────────────────────────────────────────

    private void HandleSprintFadeOut()
    {
        if (!_lastStepWasSprint || _fadeOut != null || !_footstepSource.isPlaying) return;

        var  m         = _character.Movement;
        bool sprinting = m != null && m.IsSprinting;

        Vector3 hVel    = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
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

    // ── Grab beam ─────────────────────────────────────────────────────────────

    private void OnItemGrabbed(EvidencePickup _)  => SoundManager.Instance?.Play(_itemPickup, transform.position);
    private void OnItemReleased(EvidencePickup _) => SoundManager.Instance?.Play(_itemDrop,   transform.position);
}
