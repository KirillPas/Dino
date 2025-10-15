using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DinosaurAttack : MonoBehaviour
{
    public InputActionProperty attackAction;
    public BoxCollider attackBoxCollider;
    [SerializeField] private Futurift.SimpleController simpleController;

    public float damage = 20f;
    public float attackDuration = 0.5f;
    public float attackDelay = 1.5f;

    [SerializeField] AudioClip attackClip;
    [SerializeField] AudioSource audioattack;

    private bool isAttacking = false;

    private void Awake()
    {
        if (attackBoxCollider != null)
            attackBoxCollider.enabled = false;

        if (simpleController == null)
            Debug.LogWarning("SimpleController not assigned in DinosaurAttack.");
    }

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
        audioattack.PlayOneShot(attackClip);
        // Блокируем движение игрока, вызывая SetAttacking(true)
        if (simpleController != null)
        {
            simpleController.SetAttacking(true);
        }

        yield return new WaitForSeconds(attackDelay);

        // Включаем коллайдер для атаки
        attackBoxCollider.enabled = true;

        yield return new WaitForSeconds(attackDuration);

        // Выключаем коллайдер после атаки
        attackBoxCollider.enabled = false;

        // Разблокируем движение игрока
        if (simpleController != null)
        {
            simpleController.SetAttacking(false);
        }

        isAttacking = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (attackBoxCollider.enabled)
        {
            var enemy = other.GetComponent<EnemyDamage>();
            if (enemy != null)
            {
                enemy.ApplyDamage(damage);
                Debug.Log($"Dinosaur attacked {other.name} for {damage} damage.");
            }
        }
    }
}
