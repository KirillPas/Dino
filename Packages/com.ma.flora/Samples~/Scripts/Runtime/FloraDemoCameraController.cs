// Copyright © Magnetic Arcade. All Rights Reserved.

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using UnityEngine;

namespace MA.Flora.Demo
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora Demo/Camera Controller")]
    public class FloraDemoCameraController : MonoBehaviour
    {
        class CameraState
        {
            public float Yaw;
            public float Pitch;
            public float Roll;
            public float X;
            public float Y;
            public float Z;

            public void SetFromTransform(Transform t)
            {
                Pitch = t.eulerAngles.x;
                Yaw = t.eulerAngles.y;
                Roll = t.eulerAngles.z;
                X = t.position.x;
                Y = t.position.y;
                Z = t.position.z;
            }

            public void Translate(Vector3 translation)
            {
                Vector3 rotatedTranslation = Quaternion.Euler(Pitch, Yaw, Roll) * translation;

                X += rotatedTranslation.x;
                Y += rotatedTranslation.y;
                Z += rotatedTranslation.z;
            }

            public void LerpTowards(CameraState target, float positionLerpPct, float rotationLerpPct)
            {
                Yaw = Mathf.Lerp(Yaw, target.Yaw, rotationLerpPct);
                Pitch = Mathf.Lerp(Pitch, target.Pitch, rotationLerpPct);
                Roll = Mathf.Lerp(Roll, target.Roll, rotationLerpPct);

                X = Mathf.Lerp(X, target.X, positionLerpPct);
                Y = Mathf.Lerp(Y, target.Y, positionLerpPct);
                Z = Mathf.Lerp(Z, target.Z, positionLerpPct);
            }

            public void UpdateTransform(Transform t)
            {
                t.eulerAngles = new Vector3(Pitch, Yaw, Roll);
                t.position = new Vector3(X, Y, Z);
            }
        }

        CameraState m_TargetCameraState = new CameraState();
        CameraState m_InterpolatingCameraState = new CameraState();

        [Header("Movement Settings")]
        [Tooltip("Exponential boost factor on translation, controllable by mouse wheel.")]
        public float Boost = 3.5f;

        [Tooltip("Time it takes to interpolate camera position 99% of the way to the target."), Range(0.001f, 1f)]
        public float PositionLerpTime = 0.2f;

        [Header("Rotation Settings")]
        [Tooltip("X = Change in mouse position.\nY = Multiplicative factor for camera rotation.")]
        public AnimationCurve MouseSensitivityCurve = new AnimationCurve(new Keyframe(0f, 0.5f, 0f, 5f), new Keyframe(1f, 2.5f, 0f, 0f));

        [Tooltip("Time it takes to interpolate camera rotation 99% of the way to the target."), Range(0.001f, 1f)]
        public float RotationLerpTime = 0.01f;

        [Tooltip("Whether or not to invert our Y axis for mouse input to rotation.")]
        public bool InvertY = false;

#if ENABLE_INPUT_SYSTEM
        InputAction m_MovementAction;
        InputAction m_VerticalMovementAction;
        InputAction m_LookAction;
        InputAction m_BoostFactorAction;
        bool        m_MouseRightButtonPressed;

        void Start()
        {
            var map = new InputActionMap("Simple Camera Controller");

            m_LookAction = map.AddAction("look", binding: "<Mouse>/delta");
            m_MovementAction = map.AddAction("move", binding: "<Gamepad>/leftStick");
            m_VerticalMovementAction = map.AddAction("Vertical Movement");
            m_BoostFactorAction = map.AddAction("Boost Factor", binding: "<Mouse>/scroll");

            m_LookAction.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=15, y=15)");
            m_MovementAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/w")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d")
                .With("Right", "<Keyboard>/rightArrow");
            m_VerticalMovementAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/pageUp")
                .With("Down", "<Keyboard>/pageDown")
                .With("Up", "<Keyboard>/e")
                .With("Down", "<Keyboard>/q")
                .With("Up", "<Gamepad>/rightshoulder")
                .With("Down", "<Gamepad>/leftshoulder");
            m_BoostFactorAction.AddBinding("<Gamepad>/Dpad").WithProcessor("scaleVector2(x=1, y=4)");

            m_MovementAction.Enable();
            m_LookAction.Enable();
            m_VerticalMovementAction.Enable();
            m_BoostFactorAction.Enable();
        }

#endif

        void OnEnable()
        {
            m_TargetCameraState.SetFromTransform(transform);
            m_InterpolatingCameraState.SetFromTransform(transform);
        }

        Vector3 GetInputTranslationDirection()
        {
            Vector3 direction = Vector3.zero;
#if ENABLE_INPUT_SYSTEM
            var moveDelta = m_MovementAction.ReadValue<Vector2>();
            direction.x = moveDelta.x;
            direction.z = moveDelta.y;
            direction.y = m_VerticalMovementAction.ReadValue<Vector2>().y;
#else
            if (Input.GetKey(KeyCode.W))
            {
                direction += Vector3.forward;
            }
            if (Input.GetKey(KeyCode.S))
            {
                direction += Vector3.back;
            }
            if (Input.GetKey(KeyCode.A))
            {
                direction += Vector3.left;
            }
            if (Input.GetKey(KeyCode.D))
            {
                direction += Vector3.right;
            }
            if (Input.GetKey(KeyCode.Q))
            {
                direction += Vector3.down;
            }
            if (Input.GetKey(KeyCode.E))
            {
                direction += Vector3.up;
            }
#endif
            return direction;
        }

        void Update()
        {
            // Exit Sample

            if (IsEscapePressed())
            {
                Application.Quit();
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #endif
            }

            // Hide and lock cursor when right mouse button pressed
            if (IsRightMouseButtonDown())
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            // Unlock and show cursor when right mouse button released
            if (IsRightMouseButtonUp())
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            // Rotation
            if (IsCameraRotationAllowed())
            {
                var mouseMovement = GetInputLookRotation() * Time.deltaTime * 5;
                if (InvertY)
                    mouseMovement.y = -mouseMovement.y;

                var mouseSensitivityFactor = MouseSensitivityCurve.Evaluate(mouseMovement.magnitude);

                m_TargetCameraState.Yaw += mouseMovement.x * mouseSensitivityFactor;
                m_TargetCameraState.Pitch += mouseMovement.y * mouseSensitivityFactor;
            }

            // Translation
            var translation = GetInputTranslationDirection() * Time.deltaTime;

            // Speed up movement when shift key held
            if (IsBoostPressed())
            {
                translation *= 10.0f;
            }

            // Modify movement by a boost factor (defined in Inspector and modified in play mode through the mouse scroll wheel)
            Boost += GetBoostFactor();
            translation *= Mathf.Pow(2.0f, Boost);

            m_TargetCameraState.Translate(translation);

            // Framerate-independent interpolation
            // Calculate the lerp amount, such that we get 99% of the way to our target in the specified time
            var positionLerpPct = 1f - Mathf.Exp((Mathf.Log(1f - 0.99f) / PositionLerpTime) * Time.deltaTime);
            var rotationLerpPct = 1f - Mathf.Exp((Mathf.Log(1f - 0.99f) / RotationLerpTime) * Time.deltaTime);
            m_InterpolatingCameraState.LerpTowards(m_TargetCameraState, positionLerpPct, rotationLerpPct);

            m_InterpolatingCameraState.UpdateTransform(transform);
        }

        float GetBoostFactor()
        {
#if ENABLE_INPUT_SYSTEM
            return m_BoostFactorAction.ReadValue<Vector2>().y * 0.01f;
#else
            return Input.mouseScrollDelta.y * 0.2f;
#endif
        }

        Vector2 GetInputLookRotation()
        {
#if ENABLE_INPUT_SYSTEM
            return m_LookAction.ReadValue<Vector2>();
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10;
#endif
        }

        bool IsBoostPressed()
        {
#if ENABLE_INPUT_SYSTEM
            bool boost = Keyboard.current != null ? Keyboard.current.leftShiftKey.isPressed : false;
            boost |= Gamepad.current != null ? Gamepad.current.xButton.isPressed : false;
            return boost;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null ? Keyboard.current.escapeKey.isPressed : false;
#else
            return Input.GetKey(KeyCode.Escape);
#endif
        }

        bool IsCameraRotationAllowed()
        {
#if ENABLE_INPUT_SYSTEM
            bool canRotate = Mouse.current != null ? Mouse.current.rightButton.isPressed : false;
            canRotate |= Gamepad.current != null ? Gamepad.current.rightStick.ReadValue().magnitude > 0 : false;
            return canRotate;
#else
            return Input.GetMouseButton(1);
#endif
        }

        bool IsRightMouseButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.rightButton.isPressed : false;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        bool IsRightMouseButtonUp()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? !Mouse.current.rightButton.isPressed : false;
#else
            return Input.GetMouseButtonUp(1);
#endif
        }
    }
}
