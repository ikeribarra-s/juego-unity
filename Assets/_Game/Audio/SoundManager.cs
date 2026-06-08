using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private int _poolSize = 16;

    private AudioSource[] _pool;
    private int           _nextIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    private void BuildPool()
    {
        _pool = new AudioSource[_poolSize];
        for (int i = 0; i < _poolSize; i++)
        {
            var go = new GameObject($"AudioSource_{i}");
            go.transform.SetParent(transform);
            _pool[i] = go.AddComponent<AudioSource>();
            _pool[i].playOnAwake = false;
        }
    }

    // 3D sound at a world position
    public void Play(SoundDefinition def, Vector3 worldPosition)
    {
        if (def == null || !def.IsValid) return;
        var src = NextSource();
        Configure(src, def);
        src.transform.position = worldPosition;
        src.Play();
    }

    // 2D sound (UI, global feedback)
    public void Play2D(SoundDefinition def)
    {
        if (def == null || !def.IsValid) return;
        var src = NextSource();
        Configure(src, def);
        src.spatialBlend = 0f;
        src.Play();
    }

    private void Configure(AudioSource src, SoundDefinition def)
    {
        src.Stop();
        src.clip                  = def.GetClip();
        src.volume                = def.Volume;
        src.pitch                 = def.GetPitch();
        src.spatialBlend          = def.SpatialBlend;
        src.outputAudioMixerGroup = def.MixerGroup;
    }

    // Round-robin — oldest sound gets cut if pool is exhausted
    private AudioSource NextSource()
    {
        var src    = _pool[_nextIndex];
        _nextIndex = (_nextIndex + 1) % _poolSize;
        return src;
    }
}
