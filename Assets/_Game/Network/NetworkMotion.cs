using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates what other players' copies need to look alive. The <b>owner</b> writes a small state (grounded / crouch / sprint flags,
/// vertical velocity, look pitch) and announces jumps; every <b>remote</b> copy feeds it into its (disabled)
/// <see cref="CharacterMovement"/> through <see cref="CharacterMovement.SetRemoteState"/>, so AnimationHandler, CharacterAudio,
/// FootIK, CartoonAnimator ... keep reading the same properties and need no network code.
/// Planar velocity is NOT sent: a remote copy derives it from its own (interpolated) transform, so the feet always match the motion
/// that is actually on screen. Position and yaw come from the NetworkTransform on the same object.
/// </summary>
[DefaultExecutionOrder(-50)]   // before AnimationHandler.Update (0) reads the movement state
[DisallowMultipleComponent]
public class NetworkMotion : NetworkBehaviour
{
    public struct State : INetworkSerializable, IEquatable<State>
    {
        public float VerticalVelocity;
        public float Pitch;
        public byte  Flags;   // 1 grounded, 2 crouching, 4 sprinting

        public bool Grounded  => (Flags & 1) != 0;
        public bool Crouching => (Flags & 2) != 0;
        public bool Sprinting => (Flags & 4) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref VerticalVelocity);
            serializer.SerializeValue(ref Pitch);
            serializer.SerializeValue(ref Flags);
        }

        public bool Equals(State o) => Flags == o.Flags
            && Mathf.Abs(VerticalVelocity - o.VerticalVelocity) < 0.05f && Mathf.Abs(Pitch - o.Pitch) < 0.25f;
    }

    [Header("Remote smoothing")]
    [Tooltip("Time (s) the derived planar velocity takes to follow the interpolated transform (hides 30 Hz tick steps).")]
    [SerializeField] private float _velocitySmoothTime = 0.08f;
    [Tooltip("Look pitch follow rate (1/s) on remote copies.")]
    [SerializeField] private float _pitchSharpness = 18f;
    [Tooltip("Above this speed (m/s) a position change is a teleport / respawn, not motion.")]
    [SerializeField] private float _teleportSpeed = 25f;

    private readonly NetworkVariable<State> _state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CharacterBase _character;
    private CharacterMovement _movement;
    private Vector3 _prevPos;
    private Vector3 _planar, _planarVel;
    private float _pitch;
    private bool _remoteInit;

    public override void OnNetworkSpawn()
    {
        _character = GetComponent<CharacterBase>();
        _movement = _character != null ? _character.Movement : null;
        if (_movement == null) { enabled = false; return; }

        if (IsOwner) _movement.Jumped += OnOwnerJumped;
        else InitRemote();
    }

    public override void OnNetworkDespawn()
    {
        if (_movement != null) _movement.Jumped -= OnOwnerJumped;
    }

    private void InitRemote()
    {
        _prevPos = transform.position;
        _planar = Vector3.zero; _planarVel = Vector3.zero;
        State s = _state.Value;
        _pitch = s.Pitch;
        _movement.SetRemoteState(Vector3.zero, s.VerticalVelocity, s.Grounded || s.Flags == 0, s.Crouching, s.Sprinting);
        _remoteInit = true;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || _movement == null || !IsOwner) return;

        byte flags = 0;
        if (_movement.IsGrounded)  flags |= 1;
        if (_movement.IsCrouching) flags |= 2;
        if (_movement.IsSprinting) flags |= 4;
        _state.Value = new State { VerticalVelocity = _movement.VerticalVelocity, Pitch = _movement.CameraPitch, Flags = flags };
    }

    private void Update()
    {
        if (!IsSpawned || _movement == null || IsOwner) return;
        if (!_remoteInit) InitRemote();

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Planar velocity from the interpolated transform.
        Vector3 pos = transform.position;
        Vector3 delta = pos - _prevPos; delta.y = 0f;
        _prevPos = pos;
        Vector3 raw = delta / dt;
        if (raw.magnitude > _teleportSpeed) { raw = Vector3.zero; _planar = Vector3.zero; _planarVel = Vector3.zero; }
        _planar = Vector3.SmoothDamp(_planar, raw, ref _planarVel, _velocitySmoothTime, Mathf.Infinity, dt);
        if (_planar.sqrMagnitude < 0.0004f) _planar = Vector3.zero;

        State s = _state.Value;
        _movement.SetRemoteState(_planar, s.VerticalVelocity, s.Grounded, s.Crouching, s.Sprinting);

        // Head look: the camera rig is disabled on remote copies but its transform still drives the head IK target.
        _pitch = Mathf.LerpAngle(_pitch, s.Pitch, 1f - Mathf.Exp(-dt * _pitchSharpness));
        if (_character.CameraRoot != null) _character.CameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void OnOwnerJumped()
    {
        if (IsSpawned) JumpedRpc();
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void JumpedRpc()
    {
        if (_movement != null) _movement.RaiseJumped();   // AnimationHandler (Jump trigger), CharacterAudio (take-off), CartoonAnimator (squash)
    }
}
