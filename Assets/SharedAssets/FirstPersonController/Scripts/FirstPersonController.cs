using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s (default combat run speed)")]
        public float MoveSpeed = 7.0f;
        [Tooltip("Sprint speed of the character in m/s (boost sprint speed)")]
        public float SprintSpeed = 10.0f;
        [Tooltip("Rotation speed of the character")]
        public float RotationSpeed = 1.0f;
        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;
        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.1f;
        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;
        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;
        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.5f;
        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;
        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 90.0f;
        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -90.0f;

        // cinemachine
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;


#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        public float CinemachineTargetPitch
        {
            get => _cinemachineTargetPitch;
            set
            {
                _cinemachineTargetPitch = ClampAngle(value, BottomClamp, TopClamp);
                if (CinemachineCameraTarget != null)
                {
                    CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
                }
            }
        }

        public void ResetCameraPitch(float pitch = 0f)
        {
            CinemachineTargetPitch = pitch;
        }

        private bool IsCurrentDeviceMouse
        {
            get
            {
                #if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Gamepad.current != null && 
                    UnityEngine.InputSystem.Gamepad.current.wasUpdatedThisFrame && 
                    UnityEngine.InputSystem.Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.01f)
                {
                    return false;
                }
                return true;
                #else
                return true;
                #endif
            }
        }

        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#else
            Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            // Ensure camera starts leveled at horizon
            ResetCameraPitch(0f);

            // Disable any child CapsuleCollider or MeshCollider on the player hierarchy so Physics.CheckSphere never detects the player's own body as ground in mid-air!
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col != null && col != _controller)
                {
                    col.enabled = false;
                }
            }

            // Guarantee downward Earth gravity (never positive or zero)
            if (Gravity >= -1f)
            {
                Gravity = -18.0f;
            }

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            GroundedCheck();
            JumpAndGravity();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void GroundedCheck()
        {
            if (GroundLayers.value == 0)
            {
                GroundLayers = Physics.AllLayers & ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            }

            // Always exclude the player's own layer (layer 8 / Player) from ground checks so the player never stands on themselves in midair!
            int selfMask = ~(1 << gameObject.layer);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) selfMask &= ~(1 << playerLayer);
            int effectiveMask = GroundLayers.value & selfMask;

            // If moving upward fast during the initial phase of a jump, we are airborne
            if (_verticalVelocity > 0.5f)
            {
                Grounded = false;
                return;
            }

            // The character controller root is at the player's feet (y=0 relative).
            // A small downward offset below the feet accurately checks the ground surface.
            float checkOffset = Mathf.Abs(GroundedOffset) > 0.001f ? Mathf.Abs(GroundedOffset) : 0.14f;
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y + GroundedRadius - checkOffset, transform.position.z);

            bool sphereGrounded = false;
            Collider[] overlaps = Physics.OverlapSphere(spherePosition, GroundedRadius, effectiveMask, QueryTriggerInteraction.Ignore);
            if (overlaps != null)
            {
                for (int i = 0; i < overlaps.Length; i++)
                {
                    Collider c = overlaps[i];
                    if (c != null && c != _controller && !c.transform.IsChildOf(transform))
                    {
                        sphereGrounded = true;
                        break;
                    }
                }
            }

            bool ccGrounded = _controller != null && _controller.isGrounded;
            Grounded = sphereGrounded || ccGrounded;
        }

        private void CameraRotation()
        {
            // if there is an input
            if (_input.look.sqrMagnitude >= _threshold)
            {
                // Mouse look: Calibrated 0.045 degrees per pixel for smooth, controllable turning
                float baseLookScale = 0.045f;

                // Gamepad look: 120 degrees per second scaled by delta time.
                // Multiplied by RotationSpeed (which ADS aim drops for steady sniping).
                float lookMultiplier = IsCurrentDeviceMouse
                    ? (baseLookScale * RotationSpeed)
                    : (120.0f * Time.deltaTime * RotationSpeed);

                _cinemachineTargetPitch += _input.look.y * lookMultiplier * 0.85f;
                _rotationVelocity = _input.look.x * lookMultiplier;

                // clamp our pitch rotation
                _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

                // Update Cinemachine camera target pitch
                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

                // rotate the player left and right
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
        }

        private void Move()
        {
            Vector2 moveInput = Vector2.zero;
            float customX = 0f;
            float customY = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) customY += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) customY -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) customX += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) customX -= 1f;
            }
#endif
            if (OasisKeybindManager.IsPressed(OasisAction.MoveForward) || OasisKeybindManager.IsKeyCodePressed(KeyCode.UpArrow)) customY += 1f;
            if (OasisKeybindManager.IsPressed(OasisAction.MoveBack) || OasisKeybindManager.IsKeyCodePressed(KeyCode.DownArrow)) customY -= 1f;
            if (OasisKeybindManager.IsPressed(OasisAction.MoveRight) || OasisKeybindManager.IsKeyCodePressed(KeyCode.RightArrow)) customX += 1f;
            if (OasisKeybindManager.IsPressed(OasisAction.MoveLeft) || OasisKeybindManager.IsKeyCodePressed(KeyCode.LeftArrow)) customX -= 1f;

            customX = Mathf.Clamp(customX, -1f, 1f);
            customY = Mathf.Clamp(customY, -1f, 1f);

            if (customX != 0f || customY != 0f)
            {
                moveInput = new Vector2(customX, customY);
                if (_input != null) _input.move = moveInput;
            }
            else if (_input != null)
            {
                // Only use _input.move if none of the default WASD keys were rebound away
                bool wasdDefault =
                    OasisKeybindManager.GetKey(OasisAction.MoveForward) == KeyCode.W &&
                    OasisKeybindManager.GetKey(OasisAction.MoveBack) == KeyCode.S &&
                    OasisKeybindManager.GetKey(OasisAction.MoveLeft) == KeyCode.A &&
                    OasisKeybindManager.GetKey(OasisAction.MoveRight) == KeyCode.D;
                if (wasdDefault)
                {
                    moveInput = _input.move;
                }
            }

            bool sprintInput = OasisKeybindManager.IsPressed(OasisAction.Sprint) ||
#if ENABLE_INPUT_SYSTEM
                (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
#endif
                (_input != null && _input.sprint && OasisKeybindManager.GetKey(OasisAction.Sprint) == KeyCode.LeftShift);

            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = sprintInput ? SprintSpeed : MoveSpeed;

            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            if (moveInput == Vector2.zero) targetSpeed = 0.0f;

            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = (_input != null && _input.analogMovement) ? moveInput.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

                // If starting from a stop, guarantee immediate snappy breakout so player never feels stuck
                if (moveInput != Vector2.zero && _speed < 2.0f && targetSpeed > 0f)
                {
                    _speed = Mathf.Max(_speed, 2.0f);
                }

                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            // normalise input direction
            Vector3 inputDirection = new Vector3(moveInput.x, 0.0f, moveInput.y).normalized;

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (moveInput != Vector2.zero)
            {
                // move
                inputDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            }

            // move the player
            if (_controller.enabled)
            {
                CollisionFlags flags = _controller.Move(inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
                // If player hits a ceiling while jumping upward, cancel upward velocity immediately
                if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
                {
                    _verticalVelocity = -1f;
                }
            }
        }

        private void JumpAndGravity()
        {
            // Enforce negative downward gravity at all times so player can never float up into the sky!
            if (Gravity >= -1f || float.IsNaN(Gravity))
            {
                Gravity = -18.0f;
            }

            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump: check custom keybind or _input.jump trigger
                bool jumpPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Jump) ||
#if ENABLE_INPUT_SYSTEM
                    (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
#endif
                    (_input != null && _input.jump && OasisKeybindManager.GetKey(OasisAction.Jump) == KeyCode.Space);

                if (jumpPressed && _jumpTimeoutDelta <= 0.0f)
                {
                    float safeJumpHeight = Mathf.Clamp(JumpHeight, 0.5f, 2.5f);
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(safeJumpHeight * -2f * Gravity);
                    Grounded = false;
                    _jumpTimeoutDelta = JumpTimeout;
                    if (_input != null) _input.jump = false;
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }

                // if we are not grounded, do not jump
                if (_input != null)
                {
                    _input.jump = false;
                }
            }

            // Always apply downward gravity every frame (clamp between -_terminalVelocity and +15m/s so upward velocity always decays back to ground)
            _verticalVelocity += Gravity * Time.deltaTime;
            _verticalVelocity = Mathf.Clamp(_verticalVelocity, -_terminalVelocity, 15.0f);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }
    }
}