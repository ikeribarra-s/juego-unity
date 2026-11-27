using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev keys for the death mechanic (TestScene, on GameManager; added by Fluffterror > Death > Add Test Dummy To Open Scene).
///  • K   — kill the character you are looking at (else the nearest other living one).
///  • F9  — kill yourself (you become the living skull).
///  • F10 — reload the scene.
/// </summary>
public class DeathDebugKeys : MonoBehaviour
{
    [Tooltip("The local player. Empty = the character under Camera.main at Start.")]
    [SerializeField] private CharacterBase _player;
    [SerializeField] private float _lookRange = 30f;

    private void Start()
    {
        if (_player == null) _player = CharacterBase.FindLocal();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.kKey.wasPressedThisFrame)   KillOther();
        if (kb.f9Key.wasPressedThisFrame)  KillSelf();
        if (kb.f10Key.wasPressedThisFrame) Reload();
    }

    public void KillSelf()
    {
        if (_player != null && _player.Vitals != null) _player.Vitals.Kill(gameObject);
    }

    public CharacterBase KillOther()
    {
        CharacterBase target = LookedAt();
        if (target == null) target = NearestOther();
        if (target == null) { Debug.Log("[DeathDebugKeys] K: nobody else alive."); return null; }
        target.Vitals.Kill(gameObject);
        return target;
    }

    private CharacterBase LookedAt()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, _lookRange, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; CharacterBase found = null;
        foreach (var h in hits)
        {
            var cb = h.collider.GetComponentInParent<CharacterBase>();
            if (cb == null || cb == _player || cb.Vitals == null || cb.Vitals.IsDead) continue;
            if (h.distance < best) { best = h.distance; found = cb; }
        }
        return found;
    }

    private CharacterBase NearestOther()
    {
        Vector3 from = _player != null ? _player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        float best = float.MaxValue; CharacterBase found = null;
        foreach (var cb in FindObjectsByType<CharacterBase>(FindObjectsInactive.Exclude))
        {
            if (cb == _player || cb.Vitals == null || cb.Vitals.IsDead) continue;
            float d = (cb.transform.position - from).sqrMagnitude;
            if (d < best) { best = d; found = cb; }
        }
        return found;
    }

    private static void Reload()
    {
        Scene s = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // Works for scenes that are not in the build settings (TestScene).
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(s.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(s.buildIndex);
#endif
    }
}
