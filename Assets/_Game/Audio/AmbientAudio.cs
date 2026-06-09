using System.Collections;
using UnityEngine;

public class AmbientAudio : MonoBehaviour
{
    [SerializeField] private SoundDefinition _sound;
    [SerializeField] private float _fadeInDuration = 2f;

    private AudioSource _source;

    private void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.loop          = true;
        _source.spatialBlend  = 0f;   // ambient is always 2D
        _source.playOnAwake   = false;
        _source.volume        = 0f;

        if (!IsReady()) return;

        _source.clip               = _sound.GetClip();
        _source.pitch              = _sound.GetPitch();
        _source.outputAudioMixerGroup = _sound.MixerGroup;
    }

    private void Start()
    {
        if (!IsReady()) return;

        _source.Play();

        if (_fadeInDuration > 0f)
            StartCoroutine(FadeVolume(0f, _sound.Volume, _fadeInDuration));
        else
            _source.volume = _sound.Volume;
    }

    // ── Public API (extend from here) ────────────────────────────────────────

    public void FadeOut(float duration) =>
        StartCoroutine(FadeVolume(_source.volume, 0f, duration, stopOnDone: true));

    public void FadeIn(float duration) =>
        StartCoroutine(FadeVolume(_source.volume, _sound != null ? _sound.Volume : 1f, duration));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerator FadeVolume(float from, float to, float duration, bool stopOnDone = false)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _source.volume = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        _source.volume = to;
        if (stopOnDone) _source.Stop();
    }

    private bool IsReady() => _sound != null && _sound.IsValid;
}
