using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev probe (Play mode, add at runtime to the player): measures how close the carried torch's geometry gets to the
/// first-person camera (min camera-space depth over every torch mesh vertex) while sweeping the pitch, standing and
/// crouched. Anything below the camera's near clip plane is sliced open on screen.
/// </summary>
[DefaultExecutionOrder(200)] // after TorchArmAnimator (50) and TorchCarrier (100)
public class TorchClipProbe : MonoBehaviour
{
    public string Report = "";
    public bool Running;
    public float MinZ = 99f, MinZPitch;

    private TorchCarrier _carrier;
    private Camera _cam;
    private readonly List<(Transform t, Vector3[] v)> _meshes = new();
    private Torch _cached;
    private float _curPitch;
    private bool _track;

    private static readonly FieldInfo PitchField =
        typeof(CharacterMovement).GetField("_cameraPitch", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo CrouchField =
        typeof(CharacterMovement).GetField("_crouchHeld", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo MoveField =
        typeof(CharacterMovement).GetField("_moveInput", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SprintField =
        typeof(CharacterMovement).GetField("_isSprinting", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo CrouchingField =
        typeof(CharacterMovement).GetField("_isCrouching", BindingFlags.NonPublic | BindingFlags.Instance);

    public void Run() { if (!Running) StartCoroutine(Suite()); }

    private void Cache()
    {
        _carrier = GetComponent<TorchCarrier>();
        _cam = GetComponentInChildren<Camera>();
        if (_carrier == null || _carrier.Carried == _cached) return;
        _cached = _carrier.Carried;
        _meshes.Clear();
        if (_cached == null) return;
        foreach (var mf in _cached.GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<Renderer>();
            if (mf.sharedMesh == null || r == null || !r.enabled) continue;
            if (r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Flipbook")) continue; // flame card
            _meshes.Add((mf.transform, mf.sharedMesh.isReadable ? mf.sharedMesh.vertices : BoundsCorners(mf.sharedMesh.bounds)));
        }
    }

    private static Vector3[] BoundsCorners(Bounds b)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return c;
    }

    private float SampleMinZ()
    {
        float m = 99f;
        var w2c = _cam.worldToCameraMatrix;
        float tanV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * _cam.aspect;
        foreach (var (t, v) in _meshes)
        {
            var l2w = t.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = w2c.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v[i]));
                float z = -p.z; // camera looks down -Z
                // only points that are on screen matter (behind / outside the frustum can't clip visibly)
                bool onScreen = z > 0f && Mathf.Abs(p.x) <= z * tanH && Mathf.Abs(p.y) <= z * tanV;
                bool sliceable = z <= _cam.nearClipPlane && Mathf.Abs(p.x) <= Mathf.Max(z, _cam.nearClipPlane) * tanH
                                 && Mathf.Abs(p.y) <= Mathf.Max(z, _cam.nearClipPlane) * tanV;
                if ((onScreen || sliceable) && z < m) m = z;
            }
        }
        return m;
    }

    private void LateUpdate()
    {
        if (!_track) return;
        Cache();
        if (_cam == null || _meshes.Count == 0) return;
        float z = SampleMinZ();
        if (z < MinZ) { MinZ = z; MinZPitch = _curPitch; }
    }

    private void SetPitch(CharacterMovement move, float p)
    {
        PitchField.SetValue(move, p);
        var root = GetComponent<CharacterBase>().CameraRoot;
        if (root != null) root.localRotation = Quaternion.Euler(p, 0f, 0f); // ApplyLook only writes on mouse input
    }

    private IEnumerator Suite()
    {
        Running = true;
        var sb = new StringBuilder();
        var move = GetComponent<CharacterMovement>();
        Cache();
        sb.AppendLine($"near clip {(_cam != null ? _cam.nearClipPlane : -1):0.000}, meshes {_meshes.Count}, torch {(_cached != null ? _cached.name : "none")}");
        foreach (bool crouch in new[] { false, true })
        {
            CrouchField?.SetValue(move, crouch);
            if (crouch) CrouchingField?.SetValue(move, true);
            yield return new WaitForSeconds(0.8f);
            for (float p = -80f; p <= 80.1f; p += 10f)
            {
                _curPitch = p;
                SetPitch(move, p);
                yield return new WaitForSeconds(0.35f); // let the hand smoothing settle
                MinZ = 99f; _track = true;
                yield return new WaitForSeconds(0.25f);
                _track = false;
                sb.AppendLine($"{(crouch ? "crouch" : "stand ")} pitch {p,4:0}: minZ {MinZ:0.000}");
            }
            // fast flick down/up
            MinZ = 99f; _track = true;
            for (int i = 0; i < 60; i++) { _curPitch = Mathf.Sin(i / 60f * Mathf.PI * 4f) * 70f; SetPitch(move, _curPitch); yield return null; }
            _track = false;
            sb.AppendLine($"{(crouch ? "crouch" : "stand ")} flick ±70: minZ {MinZ:0.000} at pitch {MinZPitch:0}");
        }
        CrouchField?.SetValue(move, false);
        // walk / sprint in circles (turning) while sweeping the pitch
        foreach (bool sprint in new[] { false, true })
        {
            SprintField?.SetValue(move, sprint);
            MoveField?.SetValue(move, new Vector2(0f, 1f));
            MinZ = 99f; _track = true;
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                transform.Rotate(0f, 60f * Time.deltaTime, 0f);
                _curPitch = Mathf.Sin(t * 1.5f) * 50f; SetPitch(move, _curPitch);
                yield return null;
            }
            _track = false;
            MoveField?.SetValue(move, Vector2.zero);
            SprintField?.SetValue(move, false);
            sb.AppendLine($"{(sprint ? "sprint" : "walk  ")} circle, pitch ±50: minZ {MinZ:0.000} at pitch {MinZPitch:0}");
            yield return new WaitForSeconds(0.5f);
        }
        SetPitch(move, 0f);
        Report = sb.ToString();
        Running = false;
    }
}
