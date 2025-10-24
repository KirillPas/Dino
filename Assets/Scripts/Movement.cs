using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class FuturiftCapsuleInputSystem : MonoBehaviour
{
    public Transform target;
    public float moveSpeed = 5f;
    public float smoothTime = 0.3f;
    public float rotationSpeed = 50f;

    private Vector3 velocity = Vector3.zero;
    private Vector2 joystickInput;

    // Ссылка на InputAction для стика джойстика, настраиваемая в инспекторе или через код
    public InputAction joystickAction;

    private void OnEnable()
    {
        joystickAction.Enable();
    }

    private void OnDisable()
    {
        joystickAction.Disable();
    }

    void Update()
    {
        if (target != null)
        {
            transform.position = Vector3.SmoothDamp(transform.position, target.position, ref velocity, smoothTime, moveSpeed);
        }

        joystickInput = joystickAction.ReadValue<Vector2>();

        float rotateX = joystickInput.y;
        float rotateZ = joystickInput.x;

        transform.Rotate(rotateX * rotationSpeed * Time.deltaTime, 0f, rotateZ * rotationSpeed * Time.deltaTime, Space.Self);
    }
}
