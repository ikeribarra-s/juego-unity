using UnityEngine;

public class LightFlicker : MonoBehaviour
{
    [Header("Flicker")]
    [SerializeField, Range(0f, 1f)] private float _flickerIntensity = 0.35f;
    [SerializeField] private float _flickerSpeed = 8f;

    [Header("FBM")]
    [SerializeField, Range(1, 8)] private int _octaves = 4;
    [SerializeField] private float _lacunarity = 2f;
    [SerializeField, Range(0f, 1f)] private float _gain = 0.5f;

    private struct LightData
    {
        public Light light;
        public float initialIntensity;
        public float noiseOffset; // per-light offset keeps them desynchronised
    }

    private LightData[] _lights;

    private void Awake()
    {
        Light[] found = GetComponentsInChildren<Light>(includeInactive: true);
        _lights = new LightData[found.Length];
        for (int i = 0; i < found.Length; i++)
        {
            _lights[i] = new LightData
            {
                light           = found[i],
                initialIntensity = found[i].intensity,
                noiseOffset     = Random.Range(0f, 100f)
            };
        }
    }

    private void Update()
    {
        float time = Time.time * _flickerSpeed;
        for (int i = 0; i < _lights.Length; i++)
        {
            ref LightData ld = ref _lights[i];
            float n = FBM(time + ld.noiseOffset);           // [0, 1]
            ld.light.intensity = ld.initialIntensity
                * Mathf.Lerp(1f - _flickerIntensity, 1f + _flickerIntensity, n);
        }
    }

    // Fractal Brownian Motion over 1-D Perlin noise — returns [0, 1]
    private float FBM(float x)
    {
        float value     = 0f;
        float amplitude = 0.5f;
        float frequency = 1f;

        for (int i = 0; i < _octaves; i++)
        {
            value     += amplitude * (Mathf.PerlinNoise(x * frequency, 0f) * 2f - 1f);
            frequency *= _lacunarity;
            amplitude *= _gain;
        }

        return value * 0.5f + 0.5f;
    }
}
