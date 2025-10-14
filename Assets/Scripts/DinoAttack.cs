using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System;

public class DinosaurAttack : MonoBehaviour
{
    public InputActionProperty attackAction;   // Input Action для кнопки атаки (Unity Input System)
    public BoxCollider attackBoxCollider;      // Box Collider зоны атаки (триггер), назначается через инспектор
    public float damage = 20f;
    public float rotationAngle = 45f;           // Угол поворота при атаке
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

        // Плавный поворот вперёд
        yield return SmoothRotate(startRotation, targetRotation, rotateDuration);

        attackBoxCollider.enabled = true;

        yield return new WaitForSeconds(attackDuration);

        attackBoxCollider.enabled = false;
        isAttacking = false;
    }
    private IEnumerator SmoothRotate(Quaternion startRotation, Quaternion targetRotation, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.rotation = targetRotation; // Точная установка в конце
    }

    private void OnTriggerEnter(Collider other)
    {
        if (attackBoxCollider.enabled)
        {
            EnemyDamage enemy = other.GetComponent<EnemyDamage>();
            if (enemy != null)
            {
                enemy.ApplyDamage(damage);
                Debug.Log($"Dinosaur attacked {other.name} for {damage} damage.");
            }
        }
    }
}
