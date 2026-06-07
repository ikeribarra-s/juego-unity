using UnityEngine;

[RequireComponent(typeof(CharacterBase))]
public class CharacterMovement : MonoBehaviour
{
    [SerializeField] private Transform _cameraRoot;

    private CharacterBase      _character;
    private CharacterController _controller;

    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool    _isSprinting;
    private float   _cameraPitch;
    private float   _verticalVelocity;

    private void Awake()
    {
        _character  = GetComponent<CharacterBase>();
        _controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        _character.Input.MoveEvent   += OnMove;
        _character.Input.LookEvent   += OnLook;
        _character.Input.SprintEvent += OnSprint;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void OnDisable()
    {
        _character.Input.MoveEvent   -= OnMove;
        _character.Input.LookEvent   -= OnLook;
        _character.Input.SprintEvent -= OnSprint;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void Update()
    {
        ApplyLook();
        ApplyMovement();
    }

    private void ApplyLook()
    {
        if (_lookInput == Vector2.zero) return;

        float sens = _character.Stats.LookSensitivity;

        // Yaw: rotate the whole character left/right
        transform.Rotate(Vector3.up * (_lookInput.x * sens));

        // Pitch: tilt only the camera up/down, clamped to avoid flipping
        _cameraPitch = Mathf.Clamp(_cameraPitch - _lookInput.y * sens, -80f, 80f);
        _cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
    }

    private void ApplyMovement()
    {
        CharacterStats stats = _character.Stats;
        float speed = stats.MoveSpeed * (_isSprinting ? stats.SprintMultiplier : 1f);

        Vector3 move = transform.right   * _moveInput.x
                     + transform.forward * _moveInput.y;
        move *= speed;

        // Simple gravity
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += stats.Gravity * Time.deltaTime;
        move.y = _verticalVelocity;

        _controller.Move(move * Time.deltaTime);
    }

    private void OnMove(Vector2 input)    => _moveInput    = input;
    private void OnLook(Vector2 input)    => _lookInput    = input;
    private void OnSprint(bool sprinting) => _isSprinting  = sprinting;
}
