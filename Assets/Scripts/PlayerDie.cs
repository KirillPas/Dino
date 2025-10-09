using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerDie : MonoBehaviour
{
    public Canvas deathCanvas;
    public InputActionProperty returnAction;
    public PlayerHealth playerHealth;

    private bool isDead = false;

    private void Start()
    {
        if (deathCanvas != null)
            deathCanvas.enabled = false;
        Time.timeScale = 1f;
    }
    private void Update()
    {
        if (!isDead && playerHealth != null && playerHealth.currentHealth <= 0)
        {
            OnPlayerDeath();
        }
    }
    private void OnEnable()
    {
        returnAction.action.performed += OnReturnPerformed;
        returnAction.action.Enable();
    }

    private void OnDisable()
    {
        returnAction.action.performed -= OnReturnPerformed;
        returnAction.action.Disable();
    }

    public void OnPlayerDeath()
    {

        if (isDead) return;

        isDead = true;

        if (deathCanvas != null)
        {
            deathCanvas.enabled = true;
        }
        Time.timeScale = 0f; 
    }

    private void OnReturnPerformed(InputAction.CallbackContext context)
    {
        if (!isDead) return;

        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }
}
