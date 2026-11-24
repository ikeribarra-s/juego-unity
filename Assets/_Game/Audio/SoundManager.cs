using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private int _poolSize = 16;

    private AudioSource[]     _pool;
    private SoundDefinition[] _poolDef;          // which definition each pool source last played
    private int               _nextIndex;
    private readonly Dictionary<SoundDefinition, float> _lastPlay = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    private void BuildPool()
    {
        _pool    = new AudioSource[_poolSize];
        _poolDef = new SoundDefinition[_poolSize];
        for (int i = 0; i < _poolSize; i++)
        {
            var go = new GameObject($"AudioSource_{i}");
            go.transform.SetParent(transform);
            _pool[i] = go.AddComponent<AudioSource>();
            _pool[i].playOnAwake = false;
            _pool[i].rolloffMode = AudioRolloffMode.Logarithmic;
            _pool[i].dopplerLevel = 0f;
        }
    }

    // 3D sound at a world position
    public void Play(SoundDefinition def, Vector3 worldPosition)
    {
        if (!Allowed(def)) return;
        var src = NextSource(def);
        Configure(src, def);
        src.transform.position = worldPosition;
        src.Play();
    }

    // 2D sound (UI, global feedback)
    public void Play2D(SoundDefinition def)
    {
        if (!Allowed(def)) return;
        var src = NextSource(def);
        Configure(src, def);
        src.spatialBlend = 0f;
        src.Play();
    }

    /// <summary>Anti-spam: per-definition minimum interval and voice cap (see SoundDefinition).</summary>
    private bool Allowed(SoundDefinition def)
    {
        if (def == null || !def.IsValid) return false;
        float now = Time.time;
        if (def.MinInterval > 0f && _lastPlay.TryGetValue(def, out float last) && now - last < def.MinInterval)
            return false;
        if (def.MaxVoices > 0)
        {
            int playing = 0;
            for (int i = 0; i < _pool.Length; i++)
                if (_poolDef[i] == def && _pool[i].isPlaying) playing++;
            if (playing >= def.MaxVoices) return false;
        }
        _lastPlay[def] = now;
        return true;
    }

    private void Configure(AudioSource src, SoundDefinition def)
    {
        src.Stop();
        src.clip                  = def.GetClip();
        src.volume                = def.Volume;
        src.pitch                 = def.GetPitch();
        src.spatialBlend          = def.SpatialBlend;
        src.outputAudioMixerGroup = def.MixerGroup;
        src.minDistance           = def.MinDistance;   // defaults 1 / 500 = Unity's defaults
        src.maxDistance           = def.MaxDistance;
    }

    // Prefer a free source; otherwise round-robin (the oldest sound gets cut).
    private AudioSource NextSource(SoundDefinition def)
    {
        for (int k = 0; k < _pool.Length; k++)
        {
            int i = (_nextIndex + k) % _pool.Length;
            if (_pool[i].isPlaying) continue;
            _nextIndex = (i + 1) % _pool.Length;
            _poolDef[i] = def;
            return _pool[i];
        }
        var src = _pool[_nextIndex];
        _poolDef[_nextIndex] = def;
        _nextIndex = (_nextIndex + 1) % _pool.Length;
        return src;
    }
}
