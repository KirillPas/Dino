using UnityEngine;

public class RobotFollower : MonoBehaviour
{
    [Header("Robot Settings")]
    public Transform dinoTransform;
    public Vector3 followOffset = new Vector3(0f, 0f, -2f);
    [Header("Camera Pitch Limits")]
    public float maxCameraPitch = 30f; // Макс. наклон камеры
    public float minCameraPitch = -15f; // Мин. наклон камеры

    private Vector3 initialOffset;

    void Start()
    {
        initialOffset = followOffset;
    }

    void LateUpdate()
    {
        // Следование за роботом
        Vector3 targetPosition = dinoTransform.position + dinoTransform.TransformDirection(followOffset);
        transform.position = targetPosition;

        //yaw и pitch робота с ограничением
        float robotPitch = dinoTransform.eulerAngles.x;
        robotPitch = robotPitch > 180 ? robotPitch - 360 : robotPitch; // Нормализация
        robotPitch = Mathf.Clamp(robotPitch, minCameraPitch, maxCameraPitch);

        Vector3 targetRotation = new Vector3(robotPitch, dinoTransform.eulerAngles.y, 0f);
        transform.rotation = Quaternion.Euler(targetRotation);
    }

    public void UpdateOffset(Vector3 newOffset)
    {
        followOffset = newOffset;
    }
}