using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DinosaurVRAttackNoInterfaces : MonoBehaviour
{
    public InputActionProperty attackAction;   // Input Action для кнопки атаки (Unity Input System)
    public BoxCollider attackBoxCollider;      // Box Collider зоны атаки (триггер), назначается через инспектор
    public float damage = 20f;
    public float rotationAngle = 60f;           // Угол поворота при атаке
    public float attackDuration = 0.5f;         // Время включенного коллайдера для нанесения урона
    public float rotateDuration = 0.3f;         // Время анимации поворота

    private bool isAttacking = false;

    private void OnEnable()
    {
        attackAction.action.performed += OnAttackPerformed;
        attackAction.action.Enable();
    }

    private void OnDisable()
    {
        attackAction.action.performed -= OnAttackPerformed;
        attackAction.action.Disable();
    }

    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (!isAttacking)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = startRotation * Quaternion.Euler(0f, rotationAngle, 0f);

        // Плавный поворот на заданный угол
        float elapsed = 0f;
        while (elapsed < rotateDuration)
        {
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, elapsed / rotateDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRotation;

        // Включаем атакующий коллайдер
        attackBoxCollider.enabled = true;

        yield return new WaitForSeconds(attackDuration);

        // Выключаем коллайдер после атаки
        attackBoxCollider.enabled = false;

        // Возвращаем исходный поворот плавно
        elapsed = 0f;
        while (elapsed < rotateDuration)
        {
            transform.rotation = Quaternion.Slerp(targetRotation, startRotation, elapsed / rotateDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = startRotation;

        isAttacking = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (attackBoxCollider.enabled)
        {
            // Ищем скрипт EnemyDamage на объекте и вызываем метод ApplyDamage
            EnemyDamage enemy = other.GetComponent<EnemyDamage>();
            if (enemy != null)
            {
                enemy.ApplyDamage(damage);
                Debug.Log($"Dinosaur VR attacked {other.name} for {damage} damage.");
            }
        }
    }
}
