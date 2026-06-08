using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "SoundDefinition", menuName = "Fluffterror/Sound Definition")]
public class SoundDefinition : ScriptableObject
{
    [SerializeField] private AudioClip[]     _clips;
    [SerializeField, Range(0f, 1f)] private float _volume     = 1f;
    [SerializeField] private Vector2         _pitchRange      = new Vector2(0.9f, 1.1f);
    [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f; // 0 = 2D, 1 = full 3D
    [SerializeField] private AudioMixerGroup _mixerGroup;

    public bool          IsValid      => _clips != null && _clips.Length > 0;
    public float         Volume       => _volume;
    public float         SpatialBlend => _spatialBlend;
    public AudioMixerGroup MixerGroup => _mixerGroup;

    public AudioClip GetClip()  => IsValid ? _clips[Random.Range(0, _clips.Length)] : null;
    public float     GetPitch() => Random.Range(_pitchRange.x, _pitchRange.y);
}
