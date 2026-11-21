using UnityEngine;

/// <summary>
/// Steps a sprite-sheet through its frames over time, driving the <c>_Frame</c> property of the
/// <c>Fluffterror/Flipbook</c> material on this Renderer. Writes via a <see cref="MaterialPropertyBlock"/>,
/// so many torches can share one material yet animate independently (and the shared asset is never
/// instanced). Grid size is read from the material's <c>_Columns</c>/<c>_Rows</c> unless you override
/// <see cref="_frameCount"/> (handy when the last cells of the sheet are blank).
///
/// Put it on the flame quad alongside <see cref="Billboard"/>.
/// </summary>
[RequireComponent(typeof(Renderer))]
[DisallowMultipleComponent]
public class FlipbookAnimator : MonoBehaviour
{
    [Tooltip("Playback speed in frames per second.")]
    [SerializeField] private float _fps = 24f;
    [Tooltip("Number of frames to play. 0 = Columns × Rows from the material (use this if trailing cells are empty).")]
    [SerializeField] private int _frameCount = 0;
    [Tooltip("Start at a random frame so multiple torches don't flicker in lockstep.")]
    [SerializeField] private bool _randomStartOffset = true;

    private static readonly int FrameID   = Shader.PropertyToID("_Frame");
    private static readonly int ColumnsID = Shader.PropertyToID("_Columns");
    private static readonly int RowsID    = Shader.PropertyToID("_Rows");

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private int   _frames;
    private float _time;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        int cols = Mathf.Max(1, Mathf.RoundToInt(MaterialFloat(ColumnsID, 4f)));
        int rows = Mathf.Max(1, Mathf.RoundToInt(MaterialFloat(RowsID, 4f)));
        _frames = _frameCount > 0 ? _frameCount : cols * rows;

        if (_randomStartOffset && _fps > 0f)
            _time = Random.Range(0f, _frames / _fps);
    }

    private float MaterialFloat(int id, float fallback)
    {
        Material m = _renderer.sharedMaterial;
        return (m != null && m.HasProperty(id)) ? m.GetFloat(id) : fallback;
    }

    private void Update()
    {
        if (_frames <= 0) return;

        _time += Time.deltaTime;
        int frame = (int)(_time * _fps) % _frames;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(FrameID, frame);
        _renderer.SetPropertyBlock(_mpb);
    }
}
