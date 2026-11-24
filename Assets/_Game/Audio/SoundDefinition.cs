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
    [Tooltip("3D only: distance (m) up to which the sound plays at full volume (Unity default 1). " +
             "Raise it for sounds meant to be heard from far away (distant ambience, creature cues).")]
    [SerializeField, Min(0.01f)] private float _minDistance = 1f;
    [Tooltip("3D only: logarithmic rolloff stops attenuating beyond this (Unity default 500).")]
    [SerializeField, Min(0.1f)] private float _maxDistance = 500f;

    [Header("Anti-spam (SoundManager pool only)")]
    [Tooltip("Max instances of this definition playing at once through the SoundManager pool (0 = no limit). " +
             "Extra requests are dropped, never cut the ones already playing.")]
    [SerializeField, Min(0)] private int _maxVoices = 0;
    [Tooltip("Minimum seconds between two plays of this definition through the pool (0 = none).")]
    [SerializeField, Min(0f)] private float _minInterval = 0f;

    public bool          IsValid      => _clips != null && _clips.Length > 0;
    public float         Volume       => _volume;
    public float         SpatialBlend => _spatialBlend;
    public AudioMixerGroup MixerGroup => _mixerGroup;
    public float         MinDistance  => _minDistance;
    public float         MaxDistance  => Mathf.Max(_maxDistance, _minDistance + 0.1f);
    public int           MaxVoices    => _maxVoices;
    public float         MinInterval  => _minInterval;

    public AudioClip GetClip()  => IsValid ? _clips[Random.Range(0, _clips.Length)] : null;
    public float     GetPitch() => Random.Range(_pitchRange.x, _pitchRange.y);
}
