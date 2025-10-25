using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DinosaurAttack : MonoBehaviour
{
    public InputActionProperty attackAction;
    public BoxCollider attackBoxCollider;

    public float damage = 20f;
    public float attackDuration = 0.5f;
    public float attackDelay = 1.25f;

    [SerializeField] AudioClip attackClip;
    [SerializeField] AudioSource audioattack;

    [SerializeField] Futurift.FutRiftV2Controller rightattack;

    private bool isAttacking = false;
    private void Awake()
    {
        if (attackBoxCollider != null)
            attackBoxCollider.enabled = false;
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
        rightattack.currentRoll = 6f;
        rightattack.currentYaw = 60f;

        yield return new WaitForSeconds(attackDelay);

        // Включаем коллайдер для атаки
        attackBoxCollider.enabled = true;

        yield return new WaitForSeconds(attackDuration);

        // Выключаем коллайдер после атаки
        attackBoxCollider.enabled = false;

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
