using System;
using UnityEngine;

/// <summary>Outcome of a single skill check, returned to whoever started it.</summary>
public enum SkillCheckResult { Good, Great, Miss }

/// <summary>
/// Dead-by-Daylight–style skill check overlay + a repair progress bar. One local instance
/// (<see cref="Instance"/>), placed on a HUD/manager GameObject with the player's
/// <see cref="InputReader"/> assigned — same setup style as <c>InventoryHUD</c> / <c>DebugHUD</c>.
///
/// A caller (e.g. <see cref="PowerGenerator"/>) calls <see cref="Begin"/> with a callback; a circular
/// gauge appears with a needle sweeping clockwise from the top and a randomly-placed success arc
/// (white) containing a thin <b>red "great"</b> slice at its leading edge. The player taps the hit key
/// (the Jump binding — Space; suppressed from actually jumping via <see cref="Active"/>):
///   • needle in the red slice → <see cref="SkillCheckResult.Great"/> (caller grants bonus progress),
///   • needle in the white arc  → <see cref="SkillCheckResult.Good"/> (no bonus, no penalty),
///   • pressed outside, or the needle sweeps past the arc untouched → <see cref="SkillCheckResult.Miss"/>.
///
/// IMGUI like the rest of the HUDs: the ring is drawn as colour-coded radial segments and the needle
/// is a rotated quad (one shared 1×1 white texture, tinted via <c>GUI.color</c>).
/// </summary>
public class SkillCheckController : MonoBehaviour
{
    public static SkillCheckController Instance { get; private set; }
    /// <summary>True while a skill check is on screen — read by CharacterMovement to swallow the jump.</summary>
    public static bool Active => Instance != null && Instance._active;

    [SerializeField] private InputReader _input;

    [Header("Difficulty")]
    [Tooltip("Seconds for the needle to sweep a full circle (lower = faster = harder).")]
    [SerializeField] private float _sweepDuration = 1.5f;
    [Tooltip("Width of the white 'good' success arc, in degrees.")]
    [SerializeField] private float _successArc = 55f;
    [Tooltip("Width of the red 'great' slice (inside the success arc), in degrees. " +
             "Set ≈ success arc to make red the ONLY pass zone.")]
    [SerializeField] private float _greatArc = 14f;
    [Tooltip("Earliest angle (from the top) the zone can appear — gives the player reaction time.")]
    [SerializeField] private float _minZoneAngle = 70f;
    [Tooltip("Latest angle the zone can start.")]
    [SerializeField] private float _maxZoneAngle = 300f;

    [Header("Layout")]
    [SerializeField] private float   _radius        = 95f;
    [SerializeField] private float   _ringThickness = 16f;
    [Tooltip("Gauge centre offset from screen centre (+y = down).")]
    [SerializeField] private Vector2 _screenOffset  = new(0f, 30f);
    [SerializeField] private Color   _ringColor   = new(0.12f, 0.12f, 0.14f, 0.85f);
    [SerializeField] private Color   _goodColor   = new(0.92f, 0.92f, 0.95f, 0.95f);
    [SerializeField] private Color   _greatColor  = new(0.95f, 0.12f, 0.10f, 1f);
    [SerializeField] private Color   _needleColor = new(1f, 0.95f, 0.6f, 1f);
    [SerializeField] private Color   _successFlash = new(0.3f, 1f, 0.4f, 1f);

    [Header("Repair bar")]
    [SerializeField] private bool  _showRepairBar = true;
    [SerializeField] private float _barWidth      = 240f;
    [SerializeField] private float _barHeight     = 16f;
    [SerializeField] private Color _barFill       = new(0.95f, 0.75f, 0.15f, 1f);
    [SerializeField] private Color _barBack       = new(0f, 0f, 0f, 0.65f);

    private const float LingerTime = 0.22f;   // how long the result stays on screen after resolving

    // --- active skill check state ---
    private bool  _active;
    private bool  _resolved;
    private float _needle;        // degrees, clockwise from top
    private float _zoneStart, _successEnd, _greatStart, _greatEnd;
    private float _resolveTime;
    private SkillCheckResult _lastResult;
    private Action<SkillCheckResult> _onResult;

    // --- repair bar feed ---
    private float _repairValue;
    private float _repairShownTime = -999f;

    private Texture2D _tex;

    /// <summary>True while a check is showing — callers use this to avoid starting another.</summary>
    public bool IsBusy => _active;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _tex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()  { if (_input != null) _input.JumpEvent += OnHit; }
    private void OnDisable() { if (_input != null) _input.JumpEvent -= OnHit; }

    // ── API for the repairer ──────────────────────────────────────────────────
    /// <summary>Feed the repair bar (call each frame while repairing). Auto-hides when calls stop.</summary>
    public void ReportRepairProgress(float value01)
    {
        _repairValue     = Mathf.Clamp01(value01);
        _repairShownTime = Time.time;
    }

    /// <summary>Start a skill check. No-op if one is already showing. Result is delivered to <paramref name="onResult"/>.</summary>
    public void Begin(Action<SkillCheckResult> onResult)
    {
        if (_active) return;
        _active   = true;
        _resolved = false;
        _onResult = onResult;
        _needle   = 0f;

        _zoneStart  = UnityEngine.Random.Range(_minZoneAngle, _maxZoneAngle);
        _successEnd = _zoneStart + _successArc;
        _greatStart = _zoneStart;                                   // red sits at the leading edge
        _greatEnd   = _zoneStart + Mathf.Min(_greatArc, _successArc);
    }

    /// <summary>Silently dismiss the active check (e.g. the player stopped repairing). No result fired.</summary>
    public void Cancel()
    {
        _active   = false;
        _resolved = false;
        _onResult = null;
    }

    // ── timing ────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (!_active) return;

        if (_resolved)
        {
            if (Time.time - _resolveTime > LingerTime) _active = false;
            return;
        }

        _needle += (360f / Mathf.Max(0.05f, _sweepDuration)) * Time.deltaTime;
        if (_needle > _successEnd) Resolve(SkillCheckResult.Miss);   // swept past the zone untouched
    }

    private void OnHit()
    {
        if (!_active || _resolved) return;
        if (_needle >= _greatStart && _needle <= _greatEnd)      Resolve(SkillCheckResult.Great);
        else if (_needle >= _zoneStart && _needle <= _successEnd) Resolve(SkillCheckResult.Good);
        else                                                      Resolve(SkillCheckResult.Miss);
    }

    private void Resolve(SkillCheckResult result)
    {
        _resolved    = true;
        _lastResult  = result;
        _resolveTime = Time.time;
        Action<SkillCheckResult> cb = _onResult;
        _onResult = null;
        cb?.Invoke(result);
    }

    // ── rendering ──────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (_tex == null) return;
        var center = new Vector2(Screen.width * 0.5f + _screenOffset.x,
                                 Screen.height * 0.5f + _screenOffset.y);

        if (_showRepairBar && Time.time - _repairShownTime < 0.15f) DrawRepairBar(center);
        if (_active) DrawSkillCheck(center);
    }

    private void DrawRepairBar(Vector2 center)
    {
        float x = center.x - _barWidth * 0.5f;
        float y = center.y + _radius + 26f;
        DrawRect(new Rect(x - 2f, y - 2f, _barWidth + 4f, _barHeight + 4f), _barBack);
        DrawRect(new Rect(x, y, _barWidth * _repairValue, _barHeight), _barFill);
    }

    private void DrawSkillCheck(Vector2 center)
    {
        float inner = _radius - _ringThickness;
        float outer = _radius;
        float meanR = _radius - _ringThickness * 0.5f;

        const float step = 4f;
        float segWidth = 2f * Mathf.PI * meanR * (step / 360f) * 1.3f;   // slight overlap, no gaps

        for (float a = 0f; a < 360f; a += step)
        {
            Color c = _ringColor;
            if (a >= _greatStart && a <= _greatEnd)        c = _greatColor;
            else if (a >= _zoneStart && a <= _successEnd)  c = _goodColor;
            DrawRadial(center, a, inner, outer, segWidth, c);
        }

        Color needleCol = _needleColor;
        if (_resolved) needleCol = _lastResult == SkillCheckResult.Miss ? _greatColor : _successFlash;
        DrawRadial(center, _needle, inner - 10f, outer + 10f, 5f, needleCol);
    }

    // A radial quad: at angleDeg=0 it points up (12 o'clock); positive angle rotates it clockwise.
    private void DrawRadial(Vector2 center, float angleDeg, float innerR, float outerR, float width, Color color)
    {
        Matrix4x4 prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(angleDeg, center);
        DrawRect(new Rect(center.x - width * 0.5f, center.y - outerR, width, outerR - innerR), color);
        GUI.matrix = prev;
    }

    private void DrawRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _tex);
        GUI.color = prev;
    }
}
