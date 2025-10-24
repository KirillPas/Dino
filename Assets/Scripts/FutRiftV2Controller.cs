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

        [SerializeField] private float maxPitch = 21f;
        [SerializeField] private float maxRoll = 18f;
        [SerializeField] private float maxYaw = 30f;

        [SerializeField] private float rSpeed = 5f;
        [SerializeField] private float slSpeed = 2.5f;

        public float currentYaw = 0f;
        public float currentPitch = 0f;
        public float currentRoll = 0f;     

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
            float targetPitch = Mathf.Clamp(-moveInput.y * maxPitch, -15f, maxPitch);
            float targetRoll = Mathf.Clamp(-moveInput.x * maxRoll, -maxRoll, maxRoll);
            float targetYaw = Mathf.Clamp(rotateInput.x * maxYaw, -maxYaw, maxYaw);

            currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * slSpeed);
            currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * slSpeed);
            currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * rSpeed);

            _controller.Pitch = currentPitch;
            _controller.Roll = currentRoll;

            transform.localRotation = Quaternion.Euler(0f, currentYaw, 0f);

            Debug.Log($"Pitch: {currentPitch}, Roll: {currentRoll}, Yaw: {currentYaw}");
        }
    }
}
