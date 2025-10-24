using Futurift.DataSenders;
using Futurift.Options;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Futurift
{
    public class FutRiftV2Controller : MonoBehaviour
    {
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private int port = 6065;

        private FutuRiftController _controller;

        private @Testmovement controls;

        private float maxPitch = 21f;
        private float maxRoll = 18f;
        private float maxYaw = 30f;

        private Vector2 moveInput = Vector2.zero;
        private Vector2 rotateInput = Vector2.zero;

        private void Awake()
        {
            var udpOptions = new UdpOptions
            {
                ip = ipAddress,
                port = port
            };

            _controller = new FutuRiftController(new UdpPortSender(udpOptions));

            controls = new @Testmovement();

            controls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
            controls.Player.Move.canceled += ctx => moveInput = Vector2.zero;

            controls.Player.Rotate.performed += ctx => rotateInput = ctx.ReadValue<Vector2>();
            controls.Player.Rotate.canceled += ctx => rotateInput = Vector2.zero;
        }

        private void OnEnable()
        {
            controls.Player.Enable();
            _controller?.Start();
        }

        private void OnDisable()
        {
            controls.Player.Disable();
            _controller?.Stop();
        }

        private void Update()
        {
            _controller.Pitch = Mathf.Clamp(-moveInput.y * maxPitch, -15f, maxPitch);
            _controller.Roll = Mathf.Clamp(-moveInput.x * maxRoll, -maxRoll, maxRoll);

            float yaw = Mathf.Clamp(rotateInput.x * maxYaw, -maxYaw, maxYaw);

            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Debug.Log($"Move Input: {moveInput}, Rotate Input: {rotateInput}");
        }
    }
}
