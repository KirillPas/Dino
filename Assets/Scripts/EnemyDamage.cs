using UnityEngine;

public class EnemyDamage : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;
    public Animator animator;

    void Start()
    {
        currentHealth = maxHealth;
    }

    // Метод для получения урона
    public void ApplyDamage(float amount)
    {
        currentHealth -= amount;
        Debug.Log($"{gameObject.name} получил урон: {amount}. Текущее здоровье: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }
    private void Die()
    {
        Debug.Log($"{gameObject.name} погиб.");
        animator.SetBool("Die", true);
    }
}
