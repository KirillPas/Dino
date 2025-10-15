using UnityEngine;
public class PlayerHealth : MonoBehaviour, IDamageable
{
    public int maxHealth = 150;
    public int currentHealth;
    [SerializeField] AudioSource Death;
    [SerializeField] AudioSource Damage;
    public AudioClip death;
    public AudioClip _damage;

    void Start()
    {
        currentHealth = maxHealth;
    }
    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        Damage.PlayOneShot(_damage);
        Debug.Log($"Player health: {currentHealth}/{maxHealth}");
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    void Die()
    {
        Death.PlayOneShot(death);
        Debug.Log("Player died.");
        // Логика смерти игрока
    }
}
