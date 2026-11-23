using System;
using UnityEngine;

/// <summary>
/// Frame-accurate sound sync with the (stepped) character animation.
///
/// Lives on the model child next to the Animator / <see cref="RetroAnimationStepper"/>. Every LateUpdate
/// it looks at the Base Layer state the Animator is *showing* (current + next during a cross-fade) and fires
/// when the normalized time crosses a contact phase from the tables below:
///   • locomotion foot contacts  → <see cref="CharacterAudio.AnimationFootstep"/>
///   • JumpStart take-off frame  → <see cref="CharacterAudio.AnimationTakeoff"/>
///   • JumpLand foot contact     → <see cref="CharacterAudio.AnimationLandContact"/>
/// Because the stepper only advances the Animator on step boundaries, a crossing can only be seen on the
/// frame the stepped pose actually reaches the contact — never before it.
///
/// Blend trees: all children of a 1D blend share normalized time, so the contacts of the *dominant* clip
/// (highest weight) are used — walk/run blending can't double-fire. CharacterAudio keeps its 0.18 s floor.
///
/// Phases come from the real rig (lowest foot/toe point &lt; 2 cm and moving with the ground). Re-measure
/// after changing clips with <b>Fluffterror › Animation › Analyse Foot Contacts</b>, which rewrites these
/// tables on every instance in the open scene.
/// </summary>
[DefaultExecutionOrder(200)]          // after RetroAnimationStepper / AnimationHandler (Update) — we read in LateUpdate anyway
[RequireComponent(typeof(Animator))]
public class AnimationAudioSync : MonoBehaviour
{
    [Serializable]
    public class ClipContacts
    {
        public string  Clip;
        [Tooltip("Normalized times (0..1) of the contact frames in this clip.")]
        public float[] Phases;
        public ClipContacts(string clip, params float[] phases) { Clip = clip; Phases = phases; }
    }

    [Header("States (Base Layer)")]
    [SerializeField] private string _locomotionState = "Idle Walk Run Blend";
    [SerializeField] private string _jumpStartState  = "JumpStart";
    [SerializeField] private string _inAirState      = "InAir";
    [SerializeField] private string _landState       = "JumpLand";

    [Header("Contact tables (normalized time)")]
    [SerializeField] private ClipContacts[] _footContacts =
    {
        new ClipContacts("Walk_N", 0.339f, 0.826f),     // frames 9.8 / 24.0 of 29 (L / R heel)
        new ClipContacts("Run_N",  0.396f, 0.884f),     // frames 7.9 / 17.7 of 20
    };
    [SerializeField] private ClipContacts[] _takeoffContacts =
    {
        new ClipContacts("JumpStart", 0.469f),           // frame 5.6 of 12 — lowest foot starts rising
    };
    [SerializeField] private ClipContacts[] _landContacts =
    {
        new ClipContacts("JumpLand",    0.073f),         // frame 1.4 of 19
        new ClipContacts("Walk_N_Land", 0.079f),         // frame 1.3 of 16
        new ClipContacts("Run_N_Land",  0f),             // a foot is already down at frame 0
    };

    [Tooltip("Minimum blend weight of the dominant locomotion clip for a footstep (idle-dominated blends stay silent).")]
    [SerializeField, Range(0f, 1f)] private float _minFootWeight = 0.35f;

    [Tooltip("Log every contact with the stepped frame shown vs the contact frame.")]
    [SerializeField] private bool _debugLog;

    private Animator       _animator;
    private CharacterAudio _audio;
    private int _locoHash, _jumpHash, _airHash, _landHash;

    // last observed normalized time per state (current / next slot), to detect crossings
    private int   _lastHashA, _lastHashB;
    private float _lastNormA, _lastNormB;

    private static readonly AnimatorClipInfo[] Empty = Array.Empty<AnimatorClipInfo>();

    /// <summary>True while the Animator shows (or is fading into) JumpStart / InAir / JumpLand.</summary>
    public bool InAirOrLanding
    {
        get
        {
            if (_animator == null) return false;
            var cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            var nxt = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            return IsAir(cur) || IsAir(nxt);
        }
    }

    private bool IsAir(int h) => h != 0 && (h == _jumpHash || h == _airHash || h == _landHash);

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _audio    = GetComponentInParent<CharacterAudio>();
        _locoHash = Animator.StringToHash(_locomotionState);
        _jumpHash = Animator.StringToHash(_jumpStartState);
        _airHash  = Animator.StringToHash(_inAirState);
        _landHash = Animator.StringToHash(_landState);
    }

    private void OnEnable()  { if (_audio != null) _audio.SetAnimationDriver(this); }
    private void OnDisable() { if (_audio != null) _audio.ClearAnimationDriver(this); }

    private void LateUpdate()
    {
        if (_audio == null || _animator == null || _animator.runtimeAnimatorController == null) return;

        var  cur          = _animator.GetCurrentAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);

        // When a fade completes, "next" becomes "current" — carry its tracked time over first so the
        // contacts it already crossed during the fade don't re-fire.
        if (!inTransition && _lastHashB != 0)
        {
            if (_lastHashB == cur.fullPathHash) { _lastHashA = _lastHashB; _lastNormA = _lastNormB; }
            _lastHashB = 0;
        }

        Process(cur, _animator.GetCurrentAnimatorClipInfo(0), ref _lastHashA, ref _lastNormA);

        if (inTransition)
            Process(_animator.GetNextAnimatorStateInfo(0), _animator.GetNextAnimatorClipInfo(0), ref _lastHashB, ref _lastNormB);
    }

    private void Process(AnimatorStateInfo info, AnimatorClipInfo[] clips, ref int lastHash, ref float lastNorm)
    {
        float norm = info.normalizedTime;
        bool  fresh = info.fullPathHash != lastHash;
        float prev  = fresh ? -0.0001f : lastNorm;      // a newly entered state counts from its start
        lastHash = info.fullPathHash;
        lastNorm = norm;
        if (!fresh && norm <= prev) return;               // the stepped pose didn't advance this frame

        int h = info.shortNameHash;
        if (h == _locoHash)       Fire(_footContacts,    clips, prev, norm, info, _minFootWeight, Kind.Foot);
        else if (h == _jumpHash)  Fire(_takeoffContacts, clips, prev, norm, info, 0f, Kind.Takeoff);
        else if (h == _landHash)  Fire(_landContacts,    clips, prev, norm, info, 0f, Kind.Land);
    }

    private enum Kind { Foot, Takeoff, Land }

    private void Fire(ClipContacts[] table, AnimatorClipInfo[] clips, float prev, float norm,
                      AnimatorStateInfo info, float minWeight, Kind kind)
    {
        // Dominant clip of the blend that has a contact table.
        ClipContacts best = null;
        AnimationClip bestClip = null;
        float bestW = -1f;
        foreach (var ci in clips ?? Empty)
        {
            if (ci.clip == null || ci.weight <= bestW) continue;
            var entry = Find(table, ci.clip.name);
            if (entry == null) continue;
            best = entry; bestClip = ci.clip; bestW = ci.weight;
        }
        if (best == null || bestW < minWeight) return;

        bool loop = info.loop;
        foreach (float c in best.Phases)
        {
            // Loops: normalizedTime keeps growing (1.3 = 2nd cycle at 0.3), so test every cycle in (prev, norm].
            int k0 = loop ? Mathf.FloorToInt(prev) : 0;
            int k1 = loop ? Mathf.FloorToInt(norm) : 0;
            for (int k = k0; k <= k1; k++)
            {
                float t = k + c;
                if (t > prev && t <= norm)
                {
                    if (_debugLog)
                    {
                        float fps   = bestClip.frameRate;
                        float shown = (norm - k) * bestClip.length * fps;
                        float lagMs = (norm - t) * info.length * 1000f;
                        Debug.Log($"[AnimSync] {kind} {bestClip.name} w={bestW:0.00} contact f{c * bestClip.length * fps:0.0} " +
                                  $"shown f{shown:0.0} (pose {lagMs:0} ms past contact) frame {Time.frameCount} t={Time.time:0.000}", this);
                    }
                    switch (kind)
                    {
                        case Kind.Foot:    _audio.AnimationFootstep();    break;
                        case Kind.Takeoff: _audio.AnimationTakeoff();     break;
                        case Kind.Land:    _audio.AnimationLandContact(); break;
                    }
                    return; // one event per crossing step is plenty (≥ 0.18 s apart anyway)
                }
            }
        }
    }

    private static ClipContacts Find(ClipContacts[] table, string clip)
    {
        foreach (var e in table) if (e != null && e.Clip == clip) return e;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor hook for the analyser: overwrite the tables.</summary>
    public void SetTables(ClipContacts[] foot, ClipContacts[] takeoff, ClipContacts[] land)
    {
        UnityEditor.Undo.RecordObject(this, "Set contact tables");
        _footContacts = foot; _takeoffContacts = takeoff; _landContacts = land;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
