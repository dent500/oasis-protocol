using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    public class StarterAssetsInputs : MonoBehaviour
    {
        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;

        [Header("Movement Settings")]
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;

        public PlayerManager CameraManager;

#if ENABLE_INPUT_SYSTEM
        private InputAction _ignoreInput;
        private InputAction _enableInput;
        private InputAction _touchFocus;
#endif

        private void Start()
        {
#if ENABLE_INPUT_SYSTEM
            _enableInput = new InputAction(binding: "/Mouse/leftButton");
            _enableInput.performed += context =>
            {
                if (!ShouldKeepCursorUnlocked())
                {
                    SetCursorState(true);
                }
            };
            _enableInput.Enable();

            _touchFocus = new InputAction(binding: "<pointer>/press");
            _touchFocus.performed += context =>
            {
                if (CameraManager != null) CameraManager.NotifyPlayerMoved();
            };
            _touchFocus.Enable();
#endif
            SetCursorState(!ShouldKeepCursorUnlocked());
        }

        private void Update()
        {
            bool shouldUnlock = ShouldKeepCursorUnlocked();
            if (shouldUnlock)
            {
                if (cursorLocked || !Cursor.visible || Cursor.lockState != CursorLockMode.None)
                {
                    cursorLocked = false;
                    cursorInputForLook = false;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            else
            {
                // In active gameplay, GUARANTEE cursor is locked in the center and completely invisible
                if (!cursorLocked || Cursor.visible || Cursor.lockState != CursorLockMode.Locked)
                {
                    cursorLocked = true;
                    cursorInputForLook = true;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    cursorLocked = true;
                    cursorInputForLook = true;
                }
#else
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    cursorLocked = true;
                    cursorInputForLook = true;
                }
#endif
            }
        }

        private void OnGUI()
        {
            // OnGUI runs after Update/LateUpdate. Prevent any IMGUI elements from making cursor visible during gameplay.
            if (!ShouldKeepCursorUnlocked())
            {
                if (Cursor.visible || Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    cursorLocked = true;
                    cursorInputForLook = true;
                }
            }
        }

        public bool ShouldKeepCursorUnlocked()
        {
            if (!this || !isActiveAndEnabled) return true;
            if (InGamePauseManager.Instance != null && InGamePauseManager.Instance.IsPaused)
            {
                return true;
            }
            if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
            {
                return true;
            }
            if (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isMatchFinished || OasisShooterManager.Instance.isSimulationMode))
            {
                return true;
            }
            if (OasisMapSystem.Instance != null && OasisMapSystem.Instance.IsFullscreenOpen)
            {
                return true;
            }
            if (OasisAITelemetryLogger.Instance != null && OasisAITelemetryLogger.Instance.showLiveTelemetryHUD)
            {
                return true;
            }
            return false;
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            if (_ignoreInput != null) { _ignoreInput.Disable(); _ignoreInput.Dispose(); _ignoreInput = null; }
            if (_enableInput != null) { _enableInput.Disable(); _enableInput.Dispose(); _enableInput = null; }
            if (_touchFocus != null) { _touchFocus.Disable(); _touchFocus.Dispose(); _touchFocus = null; }
#endif
        }


#if ENABLE_INPUT_SYSTEM
        public void OnMove(InputValue value)
        {
            if (CameraManager != null)
            {
                CameraManager.NotifyPlayerMoved();
            }
            MoveInput(value.Get<Vector2>());
        }

        private int _ignoreLookFrames = 0;

        public void OnLook(InputValue value)
        {
            if (CameraManager != null)
            {
                CameraManager.NotifyPlayerMoved();
            }
            if (cursorInputForLook && cursorLocked && !ShouldKeepCursorUnlocked())
            {
                if (_ignoreLookFrames > 0)
                {
                    _ignoreLookFrames--;
                    LookInput(Vector2.zero);
                    return;
                }
                Vector2 lookDelta = value.Get<Vector2>();
                LookInput(lookDelta);
            }
            else
            {
                LookInput(Vector2.zero);
            }
        }

        public void OnJump(InputValue value)
        {
            if (CameraManager != null)
            {
                CameraManager.NotifyPlayerMoved();
            }
            JumpInput(value.isPressed);
        }

        public void OnSprint(InputValue value)
        {
            if (CameraManager != null)
            {
                CameraManager.NotifyPlayerMoved();
            }
            SprintInput(value.isPressed);
        }
#endif


        public void MoveInput(Vector2 newMoveDirection)
        {
            move = newMoveDirection;
        }

        public void LookInput(Vector2 newLookDirection)
        {
            look = newLookDirection;
        }

        public void JumpInput(bool newJumpState)
        {
            jump = newJumpState;
        }

        public void SprintInput(bool newSprintState)
        {
            sprint = newSprintState;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (ShouldKeepCursorUnlocked())
            {
                SetCursorState(false);
                return;
            }
            SetCursorState(cursorLocked);
        }

        public void SetCursorState(bool newState)
        {
            if (ShouldKeepCursorUnlocked())
            {
                newState = false;
            }
            if (newState && !cursorLocked)
            {
                _ignoreLookFrames = 2;
            }
            cursorLocked = newState;
            cursorInputForLook = newState;
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !newState;
            if (newState)
            {
                look = Vector2.zero;
            }
        }
    }

}