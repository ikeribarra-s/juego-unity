using UnityEngine;

/// <summary>
/// Post FX for the LOCAL dead player (Docs/Design/DeathFX.md 3c): a short kick (grain + chroma pulse) and then the
/// mild dead look (<see cref="HorrorPostFX.SetDeathBlend"/>, blended over <see cref="DeathFXProfile.DeadBlendTime"/>).
/// Added by <see cref="SkeletonDisassembler"/> at death, only for the local player. <see cref="Revive"/> blends back;
/// <c>OnDisable</c> / destroy restore the alive look at once. Remote players never get this.
/// </summary>
public class DeathFXLocal : MonoBehaviour
{
    private DeathFXProfile _fx;
    private float _t, _target = 1f, _blendTime = 0.8f;

    public float Blend => _t;

    public void Init(DeathFXProfile fx)
    {
        _fx = fx;
        _blendTime = fx != null ? Mathf.Max(0.01f, fx.DeadBlendTime) : 0.8f;
        _t = 0f; _target = 1f;
        HorrorPostFX post = HorrorPostFX.Instance;
        if (post != null && fx != null)
        {
            post.SetDeadLook(fx.Saturation, fx.PostExposure, fx.Vignette, fx.Bloom, fx.Chromatic, fx.Grain);
            post.PulseGrain(fx.KickGrain, fx.KickGrainTime);
            post.PulseChromatic(fx.KickChromatic, fx.KickChromaticTime);
        }
    }

    /// <summary>Blend the dead look back out (default <see cref="DeathFXProfile.ReviveTime"/>).</summary>
    public void Revive(float duration = -1f)
    {
        if (duration < 0f) duration = _fx != null ? _fx.ReviveTime : 0.6f;
        _target = 0f;
        _blendTime = Mathf.Max(0.01f, duration);
    }

    private void Update()
    {
        HorrorPostFX post = HorrorPostFX.Instance;
        if (post == null) return;
        float before = _t;
        _t = Mathf.MoveTowards(_t, _target, Time.deltaTime / _blendTime);
        if (_t != before || _t == 1f) post.SetDeathBlend(_t);   // held at 1: keep re-asserting (a mood change re-applies the alive look)
        if (_target == 0f && _t <= 0f) enabled = false;
    }

    private void OnDisable()
    {
        if (HorrorPostFX.Instance != null) HorrorPostFX.Instance.SetDeathBlend(0f);
        _t = 0f;
    }
}
