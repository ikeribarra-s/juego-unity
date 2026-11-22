using UnityEngine;

/// <summary>
/// Big centred mission announcements drawn with a gothic font (IMGUI, like the other HUDs):
/// generator progress ("Generator 2/3 running"), extraction unlocking, and mission win / loss.
///
/// Listens to <see cref="PowerGenerator.AnyStateChanged"/> and the <see cref="MissionManager"/>
/// events — no wiring needed beyond placing it on a persistent HUD object. Messages fade in, hold,
/// then fade out; win/loss messages stay on screen.
/// </summary>
public class MissionMessageUI : MonoBehaviour
{
    [Tooltip("Gothic font. Falls back to the default GUI font if empty.")]
    [SerializeField] private Font  _gothicFont;
    [SerializeField] private int   _fontSize      = 44;
    [SerializeField] private int   _subFontSize   = 24;
    [SerializeField] private Color _color         = new(0.93f, 0.88f, 0.78f, 1f);
    [SerializeField] private Color _unlockColor   = new(0.6f, 1f, 0.65f, 1f);
    [SerializeField] private Color _lossColor     = new(0.85f, 0.12f, 0.08f, 1f);
    [Tooltip("Vertical screen position of the message (0 = top, 1 = bottom).")]
    [SerializeField, Range(0f, 1f)] private float _verticalAnchor = 0.28f;
    [SerializeField] private float _fadeIn  = 0.4f;
    [SerializeField] private float _hold    = 2.5f;
    [SerializeField] private float _fadeOut = 1f;

    [Header("Text")]
    [SerializeField] private string _generatorFormat = "Generator {0}/{1} running";
    [SerializeField] private string _unlockTitle     = "The way out is open";
    [SerializeField] private string _unlockSub       = "Bring the evidence to the extraction point";
    [SerializeField] private string _winTitle        = "You made it out";
    [SerializeField] private string _lossTitle       = "You didn't make it";

    private string _title, _sub;
    private Color  _titleColor;
    private float  _shownAt = -999f;
    private bool   _sticky;               // win/loss: stay on screen
    private bool   _subscribed;
    private GUIStyle _titleStyle, _subStyle;

    private void Start()      => Subscribe();
    private void OnEnable()   { if (Time.frameCount > 0) Subscribe(); }
    private void OnDisable()  => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        PowerGenerator.AnyStateChanged += OnGeneratorChanged;
        var mm = MissionManager.Instance;
        if (mm != null)
        {
            mm.GeneratorsCompleted += OnUnlocked;
            mm.MissionWon          += OnWon;
            mm.MissionLost         += OnLost;
        }
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        PowerGenerator.AnyStateChanged -= OnGeneratorChanged;
        var mm = MissionManager.Instance;
        if (mm != null)
        {
            mm.GeneratorsCompleted -= OnUnlocked;
            mm.MissionWon          -= OnWon;
            mm.MissionLost         -= OnLost;
        }
        _subscribed = false;
    }

    private void OnGeneratorChanged(PowerGenerator gen)
    {
        var mm = MissionManager.Instance;
        if (mm == null || !mm.RequiresGenerators || !gen.IsRunning) return;

        // The last one is announced by OnUnlocked (fires in the same frame) instead.
        if (mm.GeneratorsComplete) return;
        Show(string.Format(_generatorFormat, mm.RunningGeneratorCount, mm.TotalGeneratorCount), null, _color);
    }

    private void OnUnlocked() => Show(_unlockTitle, _unlockSub, _unlockColor);
    private void OnWon()      => Show(_winTitle,  null, _color,     sticky: true);
    private void OnLost()     => Show(_lossTitle, null, _lossColor, sticky: true);

    /// <summary>Show an arbitrary announcement (usable by other systems).</summary>
    public void Show(string title, string sub, Color color, bool sticky = false)
    {
        if (_sticky) return;   // don't overwrite the final win/loss screen
        _title = title; _sub = sub; _titleColor = color; _sticky = sticky;
        _shownAt = Time.unscaledTime;
    }

    private float CurrentAlpha()
    {
        float t = Time.unscaledTime - _shownAt;
        if (t < 0f) return 0f;
        if (t < _fadeIn) return t / _fadeIn;
        if (_sticky) return 1f;
        t -= _fadeIn;
        if (t < _hold) return 1f;
        t -= _hold;
        return t < _fadeOut ? 1f - t / _fadeOut : 0f;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_title)) return;
        float a = CurrentAlpha();
        if (a <= 0f) return;

        _titleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        _subStyle   ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        _titleStyle.fontSize = _fontSize;
        _subStyle.fontSize   = _subFontSize;
        if (_gothicFont != null) { _titleStyle.font = _gothicFont; _subStyle.font = _gothicFont; }

        float y = Screen.height * _verticalAnchor;
        var titleRect = new Rect(0f, y, Screen.width, _fontSize * 1.6f);
        DrawShadowed(titleRect, _title, _titleStyle, _titleColor, a);

        if (!string.IsNullOrEmpty(_sub))
        {
            var subRect = new Rect(0f, titleRect.yMax, Screen.width, _subFontSize * 1.6f);
            DrawShadowed(subRect, _sub, _subStyle, _color, a);
        }
    }

    private static void DrawShadowed(Rect rect, string text, GUIStyle style, Color color, float alpha)
    {
        var shadow = rect; shadow.x += 2f; shadow.y += 2f;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * alpha);
        GUI.Label(shadow, text, style);
        color.a *= alpha;
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }
}
