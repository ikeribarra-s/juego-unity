using UnityEngine;

/// <summary>
/// Minimal pre-game menu (IMGUI, placeholder until the real UI): Solo / Host / Join by IP. Lives in the Lobby scene next to a
/// <see cref="NetworkBootstrap"/>. Players are spawned by the bootstrap once the game scene is loaded.
/// </summary>
public class LobbyMenu : MonoBehaviour
{
    [SerializeField] private string _ip = "127.0.0.1";

    private GUIStyle _title, _label;
    private string _status = "";
    private NetworkBootstrap _boot;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _boot = NetworkBootstrap.Instance;
        if (_boot != null) { _status = _boot.Status; _boot.StatusChanged += OnStatus; }
    }

    private void OnDisable()
    {
        if (_boot != null) _boot.StatusChanged -= OnStatus;
    }

    private void OnStatus(string s) => _status = s;

    private void OnGUI()
    {
        if (_boot == null) _boot = NetworkBootstrap.Instance;
        _title ??= new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };

        const float w = 320f;
        float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.25f;

        GUI.Label(new Rect(x - 100, y, w + 200, 56), "FLUFFTERROR", _title);
        y += 80;

        bool busy = Net.IsOnline;
        GUI.enabled = !busy && _boot != null;
        if (GUI.Button(new Rect(x, y, w, 40), "Play solo")) _boot.StartHostSession(loadGameScene: true);
        y += 50;
        if (GUI.Button(new Rect(x, y, w, 40), "Host a game")) _boot.StartHostSession(loadGameScene: true);
        y += 60;
        GUI.Label(new Rect(x, y, w, 22), "Host address (IP)", _label);
        y += 22;
        _ip = GUI.TextField(new Rect(x, y, w, 28), _ip);
        y += 34;
        if (GUI.Button(new Rect(x, y, w, 40), "Join game")) _boot.StartClientSession(_ip);
        y += 60;
        GUI.enabled = true;
        if (busy && GUI.Button(new Rect(x, y, w, 32), "Cancel")) { _boot.StopSession(loadLobby: false); }
        if (GUI.Button(new Rect(x, y + (busy ? 40 : 0), w, 32), "Quit")) Application.Quit();

        if (!string.IsNullOrEmpty(_status))
            GUI.Label(new Rect(x - 100, Screen.height - 80, w + 200, 50), _status, _label);
    }
}
