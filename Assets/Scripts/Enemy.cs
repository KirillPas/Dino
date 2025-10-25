using UnityEngine;
using UnityEngine.AI;
public interface IDamageable
{
    void TakeDamage(int damage);
}
public class Enemy : MonoBehaviour
{
    public Transform player;
    public float attackRange = 2f;
    public float sightRange = 10f;
    public float attackCooldown = 1.5f;
    public int damage = 10;
    public Animator animator;

    private NavMeshAgent agent;
    private float lastAttackTime = 0f;
    private IDamageable playerDamageable;
    private EnemyDamage enemyDamage; // Ссылка на компонент здоровья

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyDamage = GetComponent<EnemyDamage>(); // Получаем компонент здоровья
        
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
        // Проверяем, мертв ли враг через компонент EnemyDamage
        if (enemyDamage != null && enemyDamage.IsDead())
            return;

        if (player == null)
            return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= attackRange)
        {
            agent.isStopped = true;
            animator.SetBool("Attack", true);
            Attack();
        }
        else if (distanceToPlayer <= sightRange)
        {
            agent.isStopped = false;
            agent.SetDestination(player.position);
            animator.SetBool("Sleep", false);
            animator.SetBool("Attack", false);
            animator.SetBool("Speed", true);
        }
        else
        {
            agent.isStopped = true;
            animator.SetBool("Sleep", true);
            animator.SetBool("Speed", false);
        }
    }

    void Attack()
    {
        if (enemyDamage != null && enemyDamage.IsDead()) return;

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