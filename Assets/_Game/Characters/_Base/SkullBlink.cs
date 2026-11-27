using UnityEngine;

/// <summary>
/// Eyelid blinking for the skeleton skull (Docs/Design/SkullBlink.md). Lives on the boneless <c>Skeleton_Lids</c>
/// SkinnedMeshRenderer under <c>Head/LidPivot</c>. Weights are driven from key tables with SetBlendShapeWeight
/// (no Animator layer). Alive: one tick per <see cref="RetroAnimationStepper"/> step. Dead (lids ride on the skull
/// piece): own 24 Hz clock. No per-frame allocations.
/// </summary>
[DefaultExecutionOrder(70)]
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class SkullBlink : MonoBehaviour
{
    // Unity weights 0-100, linear between keys, authored at 30 fps (ArtSource/Characters/Animations/Blink/README.md).
    public static readonly float[] BlinkKeys       = { 0, 45, 100, 100, 100, 70, 25, 0 };
    public static readonly float[] BlinkDoubleKeys = { 0, 50, 100, 100, 55, 35, 70, 100, 100, 70, 35, 10, 0 };
    private const float KeyFps = 30f;

    [SerializeField] private SkinnedMeshRenderer _lids;
    [SerializeField] private RetroAnimationStepper _stepper;
    [Tooltip("Free clock rate when there is no stepper (dead skull).")]
    [SerializeField] private float _fps = 24f;
    [Tooltip("Seconds between blinks (min, max).")]
    [SerializeField] private Vector2 _interval = new(3f, 7f);
    [SerializeField, Range(0f, 1f)] private float _doubleChance = 0.2f;
    [SerializeField] private float _squeezeTime = 0.15f;
    [Tooltip("Delay after the skull lands before its double blink.")]
    [SerializeField] private float _landedDelay = 0.3f;
    [SerializeField] private bool _enabledBlink = true;
    [Tooltip("Tiny quiet lid tick, played on the frame the lids reach closed. Not for the local player's own living blink.")]
    [SerializeField] private SoundDefinition _blinkSound;
    [SerializeField, Range(0f, 1f)] private float _secondTickScale = 0.8f;

    private int _seqSide;
    private float _nextBlinkTime, _seqStart;
    private float[] _seq;
    private float _squeezeUntil = -1f;
    private int _idxL = -1, _idxR = -1;
    private float _lastL = -1f, _lastR = -1f;
    private float _acc;
    private LivingSkull _living;
    private BonePiece _skull;
    private bool _wasReady = true, _landedSeen;
    private float _landedBlinkAt = -1f;
    private bool _detached;
    private bool _closed;
    private int _closures;
    private bool _localChecked, _isLocalOwner;
    private CharacterBase _owner;
    private const float ClosedThreshold = 95f;

    /// <summary>Debug: number of lid ticks requested so far (counts even when the sound is muted by the voice cap).</summary>
    public int TickSoundCount { get; private set; }

    /// <summary>Debug: number of SetBlendShapeWeight calls so far.</summary>
    public int WriteCount { get; private set; }
    public float WeightL => _lastL;
    public float WeightR => _lastR;
    public bool Detached => _detached;

    private void Awake()
    {
        if (_lids == null) _lids = GetComponent<SkinnedMeshRenderer>();
        if (_stepper == null) _stepper = GetComponentInParent<RetroAnimationStepper>();
        _owner = GetComponentInParent<CharacterBase>();
        if (_lids != null && _lids.sharedMesh != null)
        {
            _idxL = _lids.sharedMesh.GetBlendShapeIndex("Blink_L");
            _idxR = _lids.sharedMesh.GetBlendShapeIndex("Blink_R");
            if (_idxL < 0 || _idxR < 0)
                Debug.LogError("[SkullBlink] Blend shapes Blink_L / Blink_R not found on the lids mesh.", this);
        }
        Write(0f, 0f, true);
        ScheduleNext();
    }

    private void OnDestroy()
    {
        if (_living != null) _living.Rolled -= OnRolled;
    }

    // Gaps are measured in real time (a tick is only a chance to act), so a slow frame rate does not stretch them.
    private void ScheduleNext() => _nextBlinkTime = Time.time + Random.Range(_interval.x, _interval.y);

    // ---------------------------------------------------------------- API

    public void Blink()       { if (_seq == null) StartSeq(BlinkKeys, 0); }
    public void DoubleBlink() { if (_seq == null) StartSeq(BlinkDoubleKeys, 0); }
    /// <summary>side 0 = left, 1 = right, -1 = random.</summary>
    public void Wink(int side)
    {
        if (_seq != null || Time.time < _squeezeUntil) return;
        if (side < 0) side = Random.value < 0.5f ? 0 : 1;
        StartSeq(BlinkKeys, side + 1);
    }
    public void Squeeze(float duration = 0.15f)
    {
        _seq = null;
        _squeezeUntil = Time.time + duration;
        Write(100f, 100f);
    }

    public void Attach(LivingSkull living, BonePiece skull)
    {
        if (_living != null) _living.Rolled -= OnRolled;
        _living = living; _skull = skull;
        if (_living != null) { _living.Rolled += OnRolled; _wasReady = _living.RollReady; }
    }

    /// <summary>Unparent from the model before it is deactivated; switch to the free clock, show the lids to every camera.</summary>
    public void DetachForDeath()
    {
        transform.SetParent(null, true);
        _stepper = null;
        _detached = true;
        if (_lids != null) _lids.SetPropertyBlock(null);
    }

    public void AttachToSkull(Transform skullPiece)
    {
        _seq = null; _squeezeUntil = -1f; _landedBlinkAt = -1f; _landedSeen = false;
        Write(0f, 0f, true);
        transform.SetParent(skullPiece, true);
        ScheduleNext();
    }

    private void OnRolled() => Squeeze(_squeezeTime);

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (!_detached || _living == null) return;
        if (!_landedSeen && _skull != null && _skull.Landed)
        {
            _landedSeen = true;
            _landedBlinkAt = Time.time + _landedDelay;
        }
        if (_landedBlinkAt >= 0f && Time.time >= _landedBlinkAt) { _landedBlinkAt = -1f; if (_seq == null) DoubleBlink(); }

        bool ready = _living.RollReady;
        if (ready && !_wasReady && _living.RollCount > 0) Wink(-1);
        _wasReady = ready;
    }

    private void LateUpdate()
    {
        if (_lids == null) return;
        if (_squeezeUntil >= 0f)
        {
            if (Time.time < _squeezeUntil) { Write(100f, 100f); return; }
            _squeezeUntil = -1f;
            Write(0f, 0f);
            ScheduleNext();
        }

        if (_stepper != null && _stepper.isActiveAndEnabled)
        {
            if (_stepper.LastStepFrame == Time.frameCount) Tick();
        }
        else
        {
            _acc += Time.deltaTime;
            float dt = 1f / _fps;
            int guard = 0;
            while (_acc >= dt && guard++ < 2) { _acc -= dt; Tick(); }
            if (_acc > dt) _acc = 0f;
        }
    }

    private void StartSeq(float[] table, int side)
    {
        _seq = table; _seqSide = side; _seqStart = Time.time;
        _closed = false; _closures = 0;
    }

    private void Tick()
    {
        if (_seq == null)
        {
            if (!_enabledBlink || Time.time < _nextBlinkTime) return;
            StartSeq(Random.value < _doubleChance ? BlinkDoubleKeys : BlinkKeys, 0);
        }

        float frame = (Time.time - _seqStart) * KeyFps;
        float last = _seq.Length - 1;
        if (frame >= last)
        {
            _seq = null;
            _closed = false;
            Write(0f, 0f);
            ScheduleNext();
            return;
        }
        int i = (int)frame;
        float w = Mathf.Lerp(_seq[i], _seq[i + 1], frame - i);
        Write(_seqSide == 2 ? 0f : w, _seqSide == 1 ? 0f : w);
        if (w >= ClosedThreshold)
        {
            if (!_closed) { _closed = true; PlayTick(++_closures > 1 ? _secondTickScale : 1f); }
        }
        else _closed = false;
    }

    // Quiet "tic" of the lids closing. Silent for the local player's own living blink (lids hidden in first person).
    private void PlayTick(float scale)
    {
        if (_blinkSound == null || SoundManager.Instance == null) return;
        if (!_detached && IsLocalOwner()) return;
        TickSoundCount++;
        SoundManager.Instance.Play(_blinkSound, transform.position, scale);
    }

    private bool IsLocalOwner()
    {
        if (!_localChecked)
        {
            var cam = Camera.main;
            if (cam == null) return false;   // retry next blink
            _localChecked = true;
            _isLocalOwner = _owner != null && cam.transform.IsChildOf(_owner.transform);
        }
        return _isLocalOwner;
    }

    private void Write(float l, float r, bool force = false)
    {
        if (_lids == null) return;
        if (_idxL >= 0 && (force || !Mathf.Approximately(l, _lastL))) { _lids.SetBlendShapeWeight(_idxL, l); _lastL = l; WriteCount++; }
        if (_idxR >= 0 && (force || !Mathf.Approximately(r, _lastR))) { _lids.SetBlendShapeWeight(_idxR, r); _lastR = r; WriteCount++; }
    }
}
