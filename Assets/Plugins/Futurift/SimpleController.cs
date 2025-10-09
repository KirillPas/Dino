using Futurift.DataSenders;
using Futurift.Options;
using Unity.VisualScripting;
using UnityEngine;

namespace Futurift
{
    public class SimpleController : MonoBehaviour
    {
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private int port = 6065;

        // Настройки для углов
        [SerializeField] private float initialPitch = 0.0f;
        [SerializeField] private float initialRoll = 0.0f;
        // Настройки интервала передачи данных
        [SerializeField] private int interval = 100;

        private FutuRiftController _controller;
        private Vector3 _lastPosition;

        private void Awake()
        {
            var udpOptions = new UdpOptions
            {
                ip = ipAddress,
                port = port
            };
            var futuRiftOptions = new FutuRiftOptions
            {
                interval = interval
            };

            // Создаем экземпляр FutuRiftController с настройками
            _controller = new FutuRiftController(
                dataSender: new UdpPortSender(udpOptions),
                futuRiftOptions: futuRiftOptions
            )
            {
                Pitch = initialPitch,
                Roll = initialRoll
            };

            _lastPosition = transform.position;
        }

        private void Update()
        {
            // Управление углами с клавиш (для отладки)
            if (Input.GetKeyDown(KeyCode.UpArrow))
                _controller.Pitch += 1;

            if (Input.GetKeyDown(KeyCode.DownArrow))
                _controller.Pitch -= 1;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                _controller.Roll -= 1;

            if (Input.GetKeyDown(KeyCode.RightArrow))
                _controller.Roll += 1;

            // Считываем текущие углы из объекта и передаем в Futurift
            var euler = transform.eulerAngles;
            _controller.Pitch = (euler.x > 180 ? euler.x - 360 : euler.x);
            _controller.Roll = (euler.z > 180 ? euler.z - 360 : euler.z);

            // Вычисляем движение персонажа
            Vector3 deltaPosition = transform.position - _lastPosition;

            // Передаем скорость движения в контроллер Futurift
            //_controller = deltaPosition.x / Time.deltaTime;
            //_controller = deltaPosition.z / Time.deltaTime;
            
            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            _controller.Start();
        }

        private void OnDisable()
        {
            _controller?.Stop();
        }
    }
}
