using UnityEngine;
using UnityEngine.AI;

public class EnemyDamage : MonoBehaviour
{
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public Animator animator;

    private bool isDead = false;
    private NavMeshAgent agent;

    void Start()
    {
        currentHealth = maxHealth;
        agent = GetComponent<NavMeshAgent>();
    }

    public void ApplyDamage(float amount)
    {
        if (isDead) return;

        currentHealth -= amount;
        Debug.Log($"{gameObject.name} получил урон: {amount}. Текущее здоровье: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        Debug.Log($"{gameObject.name} погиб.");

        // Останавливаем навигацию
        if (agent != null)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        animator.SetBool("Die", true);
        animator.SetBool("Attack", false);
    }

    public bool IsDead()
    {
        return isDead;
    }
}