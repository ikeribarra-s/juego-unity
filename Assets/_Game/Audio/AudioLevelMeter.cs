using UnityEngine;

/// <summary>
/// Dev tool: measures what the player actually hears. Add it (at runtime or in a test scene) to the
/// GameObject with the <see cref="AudioListener"/>; <c>OnAudioFilterRead</c> sees the final mix.
/// Reports BS.1770-style K-weighted loudness — momentary (400 ms), short-term (3 s), integrated (ungated
/// mean since <see cref="ResetMeter"/>), the loudest momentary value — and the sample peak, all in
/// LUFS / dBFS. Not added to any scene; the sound-designer agent uses it for mix checks.
/// </summary>
[AddComponentMenu("Fluffterror/Audio/Audio Level Meter (dev)")]
public class AudioLevelMeter : MonoBehaviour
{
    public float MomentaryLufs  { get; private set; } = -120f;
    public float ShortTermLufs  { get; private set; } = -120f;
    public float IntegratedLufs => _totalN > 0 ? Lufs(_totalE / _totalN) : -120f;
    public float MaxMomentary   { get; private set; } = -120f;
    public float PeakDbfs       => 20f * Mathf.Log10(Mathf.Max(_peak, 1e-9f));

    // K-weighting biquads (per channel), coefficients for the output sample rate
    private double[] _b1, _a1, _b2, _a2;
    private double[,] _z;                // [channel, 4] states (2 per stage)
    private int _sr;

    // 100 ms blocks → ring buffers for 400 ms / 3 s windows
    private int _blockLen, _blockN;
    private double _blockE;
    private readonly double[] _blocks = new double[30];
    private int _blockIdx, _blocksFilled;
    private double _totalE; private long _totalN;
    private float _peak;
    private readonly object _lock = new object();

    private void Awake() => Setup(AudioSettings.outputSampleRate);

    private void Setup(int sr)
    {
        _sr = sr;
        Biquad(1681.974450955533, 0.7071752369554196, 3.999843853973347, true, out _b1, out _a1);  // high shelf
        Biquad(38.13547087602444, 0.5003270373238773, 0, false, out _b2, out _a2);                 // high pass
        _z = new double[8, 4];
        _blockLen = Mathf.Max(1, sr / 10);
    }

    private void Biquad(double f, double q, double gainDb, bool shelf, out double[] b, out double[] a)
    {
        double w = 2 * System.Math.PI * f / _sr, cs = System.Math.Cos(w), sn = System.Math.Sin(w), al = sn / (2 * q);
        if (shelf)
        {
            double A = System.Math.Pow(10, gainDb / 40), sq = 2 * System.Math.Sqrt(A) * al;
            b = new[] { A * ((A + 1) + (A - 1) * cs + sq), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - sq) };
            a = new[] { (A + 1) - (A - 1) * cs + sq, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - sq };
        }
        else
        {
            b = new[] { (1 + cs) / 2, -(1 + cs), (1 + cs) / 2 };
            a = new[] { 1 + al, -2 * cs, 1 - al };
        }
        for (int i = 2; i >= 0; i--) { b[i] /= a[0]; if (i > 0) a[i] /= a[0]; }
        a[0] = 1;
    }

    public void ResetMeter()
    {
        lock (_lock)
        {
            _totalE = 0; _totalN = 0; _peak = 0; MaxMomentary = -120f;
            System.Array.Clear(_blocks, 0, _blocks.Length); _blocksFilled = 0; _blockIdx = 0; _blockE = 0; _blockN = 0;
        }
    }

    private static float Lufs(double meanSquare) => (float)(-0.691 + 10 * System.Math.Log10(System.Math.Max(meanSquare, 1e-12)));

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_z == null || channels > 8) return;
        lock (_lock)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                double e = 0;
                for (int c = 0; c < channels; c++)
                {
                    float s = data[i + c];
                    float abs = s < 0 ? -s : s;
                    if (abs > _peak) _peak = abs;
                    // stage 1 (DF2T)
                    double y1 = _b1[0] * s + _z[c, 0];
                    _z[c, 0] = _b1[1] * s - _a1[1] * y1 + _z[c, 1];
                    _z[c, 1] = _b1[2] * s - _a1[2] * y1;
                    double y2 = _b2[0] * y1 + _z[c, 2];
                    _z[c, 2] = _b2[1] * y1 - _a2[1] * y2 + _z[c, 3];
                    _z[c, 3] = _b2[2] * y1 - _a2[2] * y2;
                    e += y2 * y2;                       // channel-summed (L+R weight 1)
                }
                _blockE += e; _blockN++;
                if (_blockN >= _blockLen)
                {
                    double ms = _blockE / _blockN;
                    _blocks[_blockIdx] = ms; _blockIdx = (_blockIdx + 1) % _blocks.Length;
                    _blocksFilled = System.Math.Min(_blocksFilled + 1, _blocks.Length);
                    _totalE += ms; _totalN++;
                    MomentaryLufs = Lufs(Mean(4));
                    ShortTermLufs = Lufs(Mean(30));
                    if (_blocksFilled >= 4 && MomentaryLufs > MaxMomentary) MaxMomentary = MomentaryLufs;
                    _blockE = 0; _blockN = 0;
                }
            }
        }
    }

    private double Mean(int n)
    {
        n = System.Math.Min(n, _blocksFilled);
        if (n == 0) return 0;
        double s = 0;
        for (int k = 1; k <= n; k++) s += _blocks[(_blockIdx - k + _blocks.Length) % _blocks.Length];
        return s / n;
    }
}
