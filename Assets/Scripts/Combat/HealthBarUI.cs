// World-space or screen-space health bar that fades in on damage and out when back to full.
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[RequireComponent(typeof(RectTransform))]
public class HealthBarUI : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Leave empty to auto-find a Health2D on this object or its parents.")]
    public Health2D target;

    [Header("UI")]
    [Tooltip("Assign the red Fill Image of your bar.")]
    public Image fill;
    [Tooltip("Optional CanvasGroup on the bar root for smooth fades.")]
    public CanvasGroup canvasGroup;

    [Header("Behaviour")]
    [Tooltip("If true, the bar only shows when damaged, then fades out.")]
    public bool showOnlyWhenDamaged = true;
    [Tooltip("How long to stay fully visible after damage/heal before starting to fade.")]
    public float holdSeconds = 1.2f;
    [Tooltip("Fade out duration after hold time (seconds).")]
    public float fadeOutSeconds = 0.35f;

    [Header("Color control (optional)")]
    [Tooltip("If true, the script will drive fill.color using the gradient below. Leave OFF to preserve your Inspector color.")]
    public bool useGradientColor = false;
    [Tooltip("Evaluated by current HP% if useGradientColor is true.")]
    public Gradient colorByHealth;

    float visibleTimer = 0f;

    // cached delegates so RemoveListener works 1:1
    UnityAction damagedHandler;
    UnityAction healedHandler;
    UnityAction diedHandler;

    void Awake()
    {
        if (!target) target = GetComponentInParent<Health2D>();
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        damagedHandler = OnDamaged;
        healedHandler  = OnHealed;
        diedHandler    = OnDied;
        ApplyImmediate();
    }

    void OnEnable()
    {
        Subscribe();
        ApplyImmediate();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Subscribe()
    {
        if (!target) target = GetComponentInParent<Health2D>();
        if (!target) return;

        target.onDamaged.RemoveListener(damagedHandler);
        target.onHealed.RemoveListener(healedHandler);
        target.onDied.RemoveListener(diedHandler);

        target.onDamaged.AddListener(damagedHandler);
        target.onHealed.AddListener(healedHandler);
        target.onDied.AddListener(diedHandler);
    }

    void Unsubscribe()
    {
        if (!target) return;
        target.onDamaged.RemoveListener(damagedHandler);
        target.onHealed.RemoveListener(healedHandler);
        target.onDied.RemoveListener(diedHandler);
    }

    void Update()
    {
        if (!target) return;

        UpdateFillVisuals();

        if (showOnlyWhenDamaged)
        {
            if (target.currentHealth < target.maxHealth && visibleTimer <= 0f)
                SetAlpha(1f);

            if (visibleTimer > 0f)
            {
                visibleTimer -= Time.deltaTime;

                float alpha = 1f;
                if (visibleTimer < fadeOutSeconds && fadeOutSeconds > 0f)
                {
                    float t = 1f - (visibleTimer / fadeOutSeconds);
                    alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t));
                }
                SetAlpha(alpha);

                if (visibleTimer <= 0f && target.currentHealth >= target.maxHealth)
                    SetAlpha(0f);
            }
            else if (target.currentHealth >= target.maxHealth)
            {
                SetAlpha(0f);
            }
        }
        else
        {
            SetAlpha(1f);
        }
    }

    // ───────── event handlers
    void OnDamaged()
    {
        visibleTimer = holdSeconds + fadeOutSeconds;
        UpdateFillVisuals();
    }

    void OnHealed()
    {
        visibleTimer = Mathf.Max(visibleTimer, 0.5f);
        UpdateFillVisuals();
    }

    void OnDied()
    {
        SetAlpha(0f);
    }

    // ───────── helpers
    void ApplyImmediate()
    {
        UpdateFillVisuals();
        if (showOnlyWhenDamaged)
            SetAlpha(target && target.currentHealth < target.maxHealth ? 1f : 0f);
        else
            SetAlpha(1f);
    }

    void UpdateFillVisuals()
    {
        if (!target || !fill) return;

        float pct = (target.maxHealth <= 0f) ? 0f : Mathf.Clamp01(target.currentHealth / target.maxHealth);
        fill.fillAmount = pct;

        if (useGradientColor && colorByHealth != null)
            fill.color = colorByHealth.Evaluate(pct);
        // else: leave fill.color exactly as set in the Inspector (so it stays red)
    }

    void SetAlpha(float a)
    {
        if (!canvasGroup) return;
        a = Mathf.Clamp01(a);
        canvasGroup.alpha = a;
        canvasGroup.interactable = a > 0.999f;
        canvasGroup.blocksRaycasts = a > 0.001f;
    }
}
