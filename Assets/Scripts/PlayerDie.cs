using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerDie : MonoBehaviour
{
    public Canvas deathCanvas;
    public InputActionProperty returnAction;
    public InputActionProperty exitAction;
    public InputActionProperty showCanvasAction;

    public PlayerHealth playerHealth;

    private bool isDead = false;
    private bool canvasShownByButton = false;

    private void Start()
    {
        if (deathCanvas != null)
            deathCanvas.enabled = false;

        Time.timeScale = 1f;
    }

    private void OnEnable()
    {
        returnAction.action.performed += OnReturnPerformed;
        returnAction.action.Enable();

        exitAction.action.performed += OnExitPerformed;
        exitAction.action.Enable();

        showCanvasAction.action.performed += OnShowCanvasPerformed;
        showCanvasAction.action.Enable();
    }

    private void OnDisable()
    {
        returnAction.action.performed -= OnReturnPerformed;
        returnAction.action.Disable();

        exitAction.action.performed -= OnExitPerformed;
        exitAction.action.Disable();

        showCanvasAction.action.performed -= OnShowCanvasPerformed;
        showCanvasAction.action.Disable();
    }

    private void Update()
    {
        if (!isDead && playerHealth != null && playerHealth.currentHealth <= 0)
        {
            OnPlayerDeath();
        }
    }

    public void OnPlayerDeath()
    {
        if (isDead)
            return;

        isDead = true;
        ShowDeathCanvas();
    }

    private void OnShowCanvasPerformed(InputAction.CallbackContext context)
    {
        if (!isDead)
        {
            if (!canvasShownByButton)
            {
                canvasShownByButton = true;
                ShowDeathCanvas();
            }
            else
            {
                HideDeathCanvas();
                canvasShownByButton = false;
            }
        }
    }

    private void ShowDeathCanvas()
    {
        if (deathCanvas != null)
            deathCanvas.enabled = true;

        Time.timeScale = 0f;
    }

    private void HideDeathCanvas()
    {
        if (deathCanvas != null)
            deathCanvas.enabled = false;

        Time.timeScale = 1f;
    }

    private void OnReturnPerformed(InputAction.CallbackContext context)
    {
        if (!isDead && !canvasShownByButton) return;

        HideDeathCanvas();

        SceneManager.LoadScene(0);
    }

    private void OnExitPerformed(InputAction.CallbackContext context)
    {
        if (!isDead && !canvasShownByButton) return;

        HideDeathCanvas();

        Debug.Log("Exit game command received.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
