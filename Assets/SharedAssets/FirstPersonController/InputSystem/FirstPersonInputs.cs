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
            _ignoreInput = new InputAction(binding: "/Keyboard/escape");
            _ignoreInput.performed += context =>
            {
                // Only unlock cursor, do not permanently disable movement
                SetCursorState(false);
            };
            _ignoreInput.Enable();

            _enableInput = new InputAction(binding: "/Mouse/leftButton");
            _enableInput.performed += context =>
            {
                if (ShouldKeepCursorUnlocked())
                {
                    cursorLocked = false;
                    cursorInputForLook = false;
                    SetCursorState(false);
                    Cursor.visible = true;
                    return;
                }
                if (cursorLocked)
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
        }

        private bool ShouldKeepCursorUnlocked()
        {
            if (!this || !isActiveAndEnabled) return true;
            if (OasisShooterManager.Instance != null &&
                (OasisShooterManager.Instance.isMatchFinished || OasisShooterManager.Instance.isSimulationMode))
            {
                return true;
            }
            if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
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

        public void OnLook(InputValue value)
        {
            if (CameraManager != null)
            {
                CameraManager.NotifyPlayerMoved();
            }
            if (cursorInputForLook)
            {
                LookInput(value.Get<Vector2>());
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
                Cursor.visible = true;
                return;
            }
            SetCursorState(cursorLocked);
        }

        private void SetCursorState(bool newState)
        {
            if (ShouldKeepCursorUnlocked())
            {
                newState = false;
            }
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            if (!newState)
            {
                Cursor.visible = true;
            }
        }
    }

}