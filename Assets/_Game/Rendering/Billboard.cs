using UnityEngine;

/// <summary>
/// Rotates this object to face the camera each frame so a flat quad (the flame card) always presents
/// itself to the player. The Flipbook shader uses <c>Cull Off</c>, so the quad is visible from either
/// side and orientation only needs to keep the card facing the viewer.
///
/// <see cref="Mode.YAxisOnly"/> (default) keeps the flame upright and only spins it horizontally —
/// right for a torch. <see cref="Mode.FaceCamera"/> tilts to face fully (right for sparks/orbs).
/// </summary>
[DisallowMultipleComponent]
public class Billboard : MonoBehaviour
{
    public enum Mode { YAxisOnly, FaceCamera }

    [SerializeField] private Mode _mode = Mode.YAxisOnly;

    private Transform _cam;

    private void LateUpdate()
    {
        if (_cam == null)
        {
            Camera c = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (c == null) return;
            _cam = c.transform;
        }

        Vector3 dir = transform.position - _cam.position;
        if (_mode == Mode.YAxisOnly) dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
}
