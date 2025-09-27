using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DinosaurAttack : MonoBehaviour
{
    public InputActionProperty attackAction;   // Input Action для кнопки атаки
    public Collider tailAttackCollider;         // Триггер коллайдер на хвосте
    public float damage = 20f;

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
        // Плавный разворот на 60 градусов вправо
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = startRotation * Quaternion.Euler(0, 60, 0);

        float rotateDuration = 0.3f;
        float elapsed = 0f;

        while (elapsed < rotateDuration)
        {
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, elapsed / rotateDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRotation;

        // Включаем коллайдер хвоста для нанесения урона
        tailAttackCollider.enabled = true;

        // Ждем, пока продолжается удар
        yield return new WaitForSeconds(0.5f);

        // Выключаем коллайдер
        tailAttackCollider.enabled = false;

        // Возвращаем исходный разворот
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

    // Обработка попаданий по врагам в зоне коллайдера хвоста
    private void OnTriggerEnter(Collider other)
    {
        if (tailAttackCollider.enabled)
        {
            EnemyDamage enemy = other.GetComponent<EnemyDamage>();
            if (enemy != null)
            {
                enemy.ApplyDamage(damage);
                Debug.Log($"Hit {other.name} for {damage} damage.");
            }
        }
    }
}
