using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class HealthBarUI : MonoBehaviour
{
    [Header("Target")]
    public Health2D target;          // leave empty to auto-find in parent

    [Header("UI")]
    public Image fill;               // assign your red Fill image
    public Image background;         // optional: assign Background image (this object) for alpha control

    [Header("Behaviour")]
    public bool hideWhenFull = true; // auto-hide when at full HP
    public float fadeOutDelay = 1.5f;
    public float fadeSpeed = 5f;

    float lastHp = -1f;
    float lastMax = -1f;
    float lastDamageTime = -999f;
    float currentAlpha = 1f;

    void Awake()
    {
        if (!target) target = GetComponentInParent<Health2D>();
        if (!fill) Debug.LogWarning("[HealthBarUI] Fill Image not assigned.");
        if (!background) background = GetComponent<Image>();
        RefreshImmediate(true);

        // Try to hook events if the target has them
        if (target != null)
        {
            try
            {
                target.onDamaged.AddListener(OnDamaged);
                target.onDied.AddListener(OnDied);
            }
            catch { /* if your Health2D doesn't expose events, polling still works */ }
        }
    }

    void OnDestroy()
    {
        if (target != null)
        {
            try
            {
                target.onDamaged.RemoveListener(OnDamaged);
                target.onDied.RemoveListener(OnDied);
            }
            catch { }
        }
    }

    void Update()
    {
        if (!target || !fill) return;

        // Poll health each frame so the bar always tracks even if events aren't fired
        if (!Mathf.Approximately(target.currentHealth, lastHp) ||
            !Mathf.Approximately(target.maxHealth, lastMax))
        {
            // if HP dropped, mark as recently damaged (affects fade)
            if (target.currentHealth < lastHp) lastDamageTime = Time.time;

            RefreshImmediate(true);
            lastHp = target.currentHealth;
            lastMax = target.maxHealth;
            currentAlpha = 1f; // pop visible on change
        }

        // Fade / hide logic
        float targetAlpha = 1f;
        bool isFull = Mathf.Approximately(target.currentHealth, target.maxHealth);
        if (hideWhenFull && isFull && Time.time > lastDamageTime + fadeOutDelay)
            targetAlpha = 0f;

        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
        ApplyAlpha(currentAlpha);
    }

    void ApplyAlpha(float a)
    {
        if (fill)
        {
            var c = fill.color; c.a = a; fill.color = c;
        }
        if (background)
        {
            var c = background.color; c.a = a * 0.6f; background.color = c;
        }
    }

    void OnDamaged(float newHealth, float damageDealt)
    {
        lastDamageTime = Time.time;
        RefreshImmediate(true);
        currentAlpha = 1f; // ensure visible on hit
    }

    void OnDied()
    {
        RefreshImmediate(true);
        ApplyAlpha(0f);
        // If parent isn't destroyed immediately, remove the bar
        Destroy(gameObject);
    }

    public void RefreshImmediate(bool force = false)
    {
        if (!target || !fill) return;
        float ratio = (target.maxHealth > 0f) ? target.currentHealth / target.maxHealth : 0f;
        if (force || !Mathf.Approximately(fill.fillAmount, ratio))
            fill.fillAmount = Mathf.Clamp01(ratio);
    }
}
