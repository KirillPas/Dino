using UnityEngine;
using Bhaptics.SDK2;

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

        // Запускаем вибрацию от bhaptics при получении урона
        int taktil1 = BhapticsLibrary.Play(eventId:"damage"); // "damage" - ID вашего haptic паттерна
        if (taktil1 == -1)
        {
            Debug.Log("Не удалось запустить вибрацию bhaptics.");
        }
        else
        {
            Debug.Log("Вибрация жилета успешно запущена, taktil1: " + taktil1);
        }

        Debug.Log($"Player health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Death.PlayOneShot(death);
        int taktil2 = BhapticsLibrary.Play(eventId: "died");
        if (taktil2 == -1)
        {
            Debug.Log("Не удалось запустить вибрацию bhaptics.");
        }
        else
        {
            Debug.Log("Вибрация жилета успешно запущена, taktil2: " + taktil2);
        }
        Debug.Log("Player died.");
    }
}
