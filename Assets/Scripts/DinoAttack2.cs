using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DinoAttack2 : MonoBehaviour
{
    public InputActionProperty ramAttackAction; // Input Action для атаки тараном (напр. триггер контроллера)
    public Collider ramCollider;                 // Коллайдер атаки (IsTrigger = true)
    public float damage = 10f;
    public float ramDuration = 1.0f;
    public float ramSpeed = 30f;
    public float attackdelay = 1.5f;

    [SerializeField] Futurift.FutRiftV2Controller tarranattack;

    private bool isRamming = false;
    private Vector3 ramDirection;
    private Vector3 startPosition;

    private void OnEnable()
    {
        ramAttackAction.action.performed += OnRamPerformed;
        ramAttackAction.action.Enable();
    }

    private void OnDisable()
    {
        ramAttackAction.action.performed -= OnRamPerformed;
        ramAttackAction.action.Disable();
    }

    private void OnRamPerformed(InputAction.CallbackContext context)
    {
        if (!isRamming)
        {
            // Пример направления тарана - вперед от объекта
            ramDirection = transform.forward;
            startPosition = transform.position;
            StartCoroutine(RamRoutine());
        }
    }

    private IEnumerator RamRoutine()
    {
        isRamming = true;
        ramCollider.enabled = true;
        tarranattack.currentPitch = -8f;

        float elapsed = 0f;

        while (elapsed < ramDuration)
        {
            transform.position += ramDirection * ramSpeed * Time.deltaTime;
            elapsed += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(attackdelay);
        ramCollider.enabled = false;
        isRamming = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isRamming) return;

        if ((1 << other.gameObject.layer) != 0)
        {
            EnemyDamage enemy = other.GetComponent<EnemyDamage>();
            if (enemy != null)
            {
                enemy.ApplyDamage(damage);
                Debug.Log($"Ram attack hit {other.name} for {damage} damage.");
            }
        }
    }
}
