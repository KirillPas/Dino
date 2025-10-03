using UnityEngine;
using UnityEngine.AI;
public interface IDamageable
{
    void TakeDamage(int damage);
}

public class Enemy : MonoBehaviour
{
    public Transform player;                   // Ссылка на игрока
    public float attackRange = 2f;             // Дальность атаки
    public float sightRange = 10f;             // Радиус видимости
    public float attackCooldown = 1.5f;        // Задержка между атаками
    public int damage = 10;                    // Урон наносимый врагом

    private NavMeshAgent agent;                // Компонент навигации врага
    private float lastAttackTime = 0f;
    private IDamageable playerDamageable;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (player == null)
        {
            Debug.LogError("Player transform is not assigned!");
            return;
        }

        playerDamageable = player.GetComponent<IDamageable>();
        if (playerDamageable == null)
        {
            Debug.LogWarning("Player does not implement IDamageable!");
        }
    }

    void Update()
    {
        if (player == null)
            return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= attackRange)
        {
            agent.isStopped = true;
            Attack();
        }
        else if (distanceToPlayer <= sightRange)
        {
            agent.isStopped = false;
            agent.SetDestination(player.position);
        }
        else
        {
            agent.isStopped = true;
            // Враг стоит на месте
        }
    }

    void Attack()
    {
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            lastAttackTime = Time.time;
            if (playerDamageable != null)
            {
                playerDamageable.TakeDamage(damage);
                Debug.Log($"Enemy attacks player for {damage} damage!");
            }
            else
            {
                Debug.Log("Enemy attacks, but player cannot take damage.");
            }
        }
    }
}
