using UnityEngine;

/// <summary>
/// Retro stop-motion look: advances the character's Animator at a fixed low rate
/// (default 24 fps, or 12 for the classic "on 2s" anime/claymation feel) so the body
/// snaps between poses instead of interpolating smoothly each frame.
///
/// Body only — the camera is NOT stepped. It keeps following every frame, so set
/// <see cref="NeckFollowCamera"/> smoothing &gt; 0 and the camera will ease smoothly over
/// the stepped neck motion instead of jittering with it.
///
/// Works by taking manual control of the Animator: the component is disabled so it no
/// longer auto-updates, and we evaluate it ourselves only on step boundaries. Animation
/// speed stays accurate because each step advances by the exact accumulated real time.
///
/// Place on the model child (the GameObject with the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
public class RetroAnimationStepper : MonoBehaviour
{
    [Tooltip("Target animation framerate. 24 = film, 12 = 'on 2s' classic anime/stop-motion.")]
    [SerializeField] private float _fps = 24f;

    private Animator _animator;
    private float    _accum;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.enabled = false; // take manual control of the update
    }

    private void Update()
    {
        // Disabled / invalid rate → behave like a normal per-frame animator.
        if (_fps <= 0f)
        {
            _animator.Update(Time.deltaTime);
            return;
        }

        _accum += Time.deltaTime;
        float step = 1f / _fps;

        if (_accum >= step)
        {
            // Advance by the whole accumulated time in one jump → stepped poses,
            // but correct overall playback speed (we consumed exactly what we waited).
            _animator.Update(_accum);
            _accum = 0f;
        }
    }
}
