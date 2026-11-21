using UnityEngine;

/// <summary>
/// On-screen interaction prompt, centred low on the screen, drawn with a gothic <see cref="Font"/>.
/// Reads the live prompt from <see cref="InteractionSystem.CurrentPrompt"/> — so it shows whatever
/// the targeted interactable returns from <c>GetPrompt()</c> ("Hold E to repair", "Repairing… 45%",
/// "Press E to start", "Running", evidence pickups, …). Hidden when nothing is targeted.
///
/// IMGUI (like the other HUDs in this project). Assign a gothic TTF/OTF to <see cref="_gothicFont"/>;
/// it falls back to the default skin font if none is set. Place it on a persistent scene object and
/// it auto-finds the local player's <see cref="InteractionSystem"/> if the reference is left empty.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private InteractionSystem _interaction;
    [Tooltip("Gothic font for the prompt. Falls back to the default GUI font if empty.")]
    [SerializeField] private Font _gothicFont;
    [SerializeField] private int _fontSize = 28;
    [SerializeField] private Color _color = new(0.93f, 0.88f, 0.78f, 1f);
    [Tooltip("Vertical screen position of the prompt (0 = top, 1 = bottom).")]
    [SerializeField, Range(0f, 1f)] private float _verticalAnchor = 0.66f;

    private GUIStyle _style;

    private void Awake()
    {
        if (_interaction == null) _interaction = FindAnyObjectByType<InteractionSystem>();
    }

    private void OnGUI()
    {
        if (_interaction == null) return;
        string prompt = _interaction.CurrentPrompt;
        if (string.IsNullOrEmpty(prompt)) return;

        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        _style.fontSize = _fontSize;
        if (_gothicFont != null) _style.font = _gothicFont;

        var rect = new Rect(0f, Screen.height * _verticalAnchor, Screen.width, _fontSize * 2f);

        // Drop shadow for legibility over bright/dark scenery, then the prompt itself.
        var shadow = rect; shadow.x += 2f; shadow.y += 2f;
        _style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(shadow, prompt, _style);
        _style.normal.textColor = _color;
        GUI.Label(rect, prompt, _style);
    }
}
