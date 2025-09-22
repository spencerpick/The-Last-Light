// Simple stamina pool with regen, delay, and a couple of events for UI/FX hooks.
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class Stamina : MonoBehaviour
{
    [Header("Stamina")]
    public float maxStamina = 100f;
    public float currentStamina = 100f;

    [Header("Regen")]
    public float regenPerSecond = 20f;
    public float regenDelayAfterUse = 1.0f;

    [Header("Events")]
    public UnityEvent onStaminaChanged;   // fired on any change
    public UnityEvent onDepleted;         // fired when hits 0
    public UnityEvent onFull;             // fired when returns to full

    float regenCooldown = 0f;

    public float Normalized => maxStamina <= 0f ? 0f : Mathf.Clamp01(currentStamina / maxStamina);

    void Awake()
    {
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
    }

    void Update()
    {
        if (regenCooldown > 0f)
        {
            regenCooldown -= Time.deltaTime;
            return;
        }

        if (currentStamina < maxStamina)
        {
            float before = currentStamina;
            currentStamina = Mathf.Min(maxStamina, currentStamina + regenPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(before, currentStamina))
                onStaminaChanged?.Invoke();

            if (Mathf.Approximately(currentStamina, maxStamina))
                onFull?.Invoke();
        }
    }

    /// <summary>Consume a fixed amount of stamina (instant).</summary>
    public bool TryConsume(float amount)
    {
        if (amount <= 0f) return true;
        if (currentStamina < amount) return false;

        float before = currentStamina;
        currentStamina -= amount;
        regenCooldown = regenDelayAfterUse;

        if (!Mathf.Approximately(before, currentStamina))
            onStaminaChanged?.Invoke();

        if (currentStamina <= 0f) onDepleted?.Invoke();
        return true;
    }

    /// <summary>Consume stamina at a per-second rate (call every frame you want to drain).</summary>
    public bool ConsumePerSecond(float perSecond)
    {
        return TryConsume(perSecond * Time.deltaTime);
    }

    /// <summary>Force set stamina (clamped). Use for pickups, etc.</summary>
    public void SetStamina(float value, bool resetRegenDelay = false)
    {
        float clamped = Mathf.Clamp(value, 0f, maxStamina);
        if (!Mathf.Approximately(currentStamina, clamped))
        {
            currentStamina = clamped;
            onStaminaChanged?.Invoke();
        }
        if (resetRegenDelay) regenCooldown = regenDelayAfterUse;
        if (Mathf.Approximately(currentStamina, 0f)) onDepleted?.Invoke();
        if (Mathf.Approximately(currentStamina, maxStamina)) onFull?.Invoke();
    }
}
