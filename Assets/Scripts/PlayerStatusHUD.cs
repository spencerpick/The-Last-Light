// Simple health/stamina HUD that keeps itself bound to the active player and updates smoothly.
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class PlayerStatusHUD : MonoBehaviour
{
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    [Header("Targets (leave blank to auto-find the active Player)")]
    public Health2D health;   // Player's Health2D
    public Stamina stamina;   // Player's Stamina

    [Header("UI - Fill Images (Type=Filled, Horizontal, Left)")]
    public Image healthFill;    // PlayerHUD/Health_Bg/health_Fill
    public Image staminaFill;   // PlayerHUD/Stamina_Bg/stamina_Fill

    [Header("Colours")]
    public bool useStaticColors = true;                          // static colors by default
    public Color healthColor = new Color(1f, 0.15f, 0.15f, 1f);  // red
    public Color staminaColor = new Color(0.25f, 1f, 0.5f, 1f);  // green

    [Header("Optional Gradients (only if useStaticColors = false AND useGradient = true)")]
    public bool useGradient = false;
    public Gradient healthGradient;
    public Gradient staminaGradient;

    [Header("Layout")]
    public Corner anchorCorner = Corner.TopLeft;
    public Vector2 padding = new Vector2(16, 16);
    public Vector2 healthSize = new Vector2(240, 22);
    public Vector2 staminaSize = new Vector2(240, 14);
    public float barSpacing = 6f;

    [Header("Behaviour")]
    public bool smoothLerp = true;
    public float lerpSpeed = 10f;

    [Header("Low HP FX")]
    [Range(0f, 1f)] public float lowHealthThreshold = 0.25f;
    public bool pulseLowHealth = true;
    public float pulseSpeed = 3.2f;
    [Range(0f, 0.5f)] public float pulseAmplitude = 0.06f;

    [Header("Auto-Rebind")]
    [Tooltip("How often to try re-finding the live Player if references are missing/wrong.")]
    public float rebindInterval = 0.5f;

    RectTransform rt;
    RectTransform healthRT, staminaRT;
    float shownHealth = 1f;
    float shownStamina = 1f;
    float rebindTimer = 0f;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (healthFill) healthRT  = healthFill.rectTransform;
        if (staminaFill) staminaRT = staminaFill.rectTransform;
        ApplyAnchorAndSizes();
    }

    void OnEnable()
    {
        EnsureImageIsFilledHorizontalLeft(healthFill);
        EnsureImageIsFilledHorizontalLeft(staminaFill);

        TryRebind(force: true);     // bind once on enable
        ImmediateSnap();
        ApplyColoursInstant();
    }

    void Update()
    {
        // Periodically rebind to the active Player (handles respawns, prefabs, etc.)
        rebindTimer -= Time.unscaledDeltaTime;
        if (rebindTimer <= 0f)
        {
            TryRebind(force: false);
            rebindTimer = rebindInterval;
        }

        // Percentages (cast to float to be explicit)
        float hpPct = 1f;
        if (health && health.isActiveAndEnabled && health.gameObject.activeInHierarchy)
        {
            float cur = (float)health.currentHealth;
            float max = Mathf.Max(1f, (float)health.maxHealth);
            hpPct = Mathf.Clamp01(cur / max);
        }

        float stPct = 0f;
        if (stamina && stamina.isActiveAndEnabled && stamina.gameObject.activeInHierarchy)
        {
            stPct = stamina.Normalized;
        }

        // Smooth or snap
        shownHealth  = smoothLerp ? Mathf.Lerp(shownHealth, hpPct, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime)) : hpPct;
        shownStamina = smoothLerp ? Mathf.Lerp(shownStamina, stPct, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime)) : stPct;

        // Apply fill amounts
        if (healthFill) { healthFill.fillAmount  = shownHealth; healthFill.SetAllDirty(); }
        if (staminaFill) { staminaFill.fillAmount = shownStamina; staminaFill.SetAllDirty(); }

        // Colours
        ApplyColoursInstant();

        // Low-HP pulse
        if (healthRT)
        {
            bool low = hpPct <= Mathf.Max(0.0001f, lowHealthThreshold);
            if (pulseLowHealth && low)
            {
                float s = 1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * pulseSpeed) * pulseAmplitude;
                healthRT.localScale = new Vector3(s, s, 1f);
            }
            else healthRT.localScale = Vector3.one;
        }
    }

    // --- Rebind logic ---
    void TryRebind(bool force)
    {
        // If user hasn’t assigned refs or they point to an inactive/non-scene object, grab the active Player clone
        if (force || !IsValidLive(health) || !IsValidLive(stamina))
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p)
            {
                var h = p.GetComponent<Health2D>();
                var s = p.GetComponent<Stamina>();
                if (IsValidLive(h)) health = h;
                if (IsValidLive(s)) stamina = s;
            }
        }
    }

    static bool IsValidLive(Behaviour b)
    {
        return b && b.isActiveAndEnabled && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy;
    }

    void ImmediateSnap()
    {
        if (health && health.isActiveAndEnabled)
        {
            float cur = (float)health.currentHealth;
            float max = Mathf.Max(1f, (float)health.maxHealth);
            shownHealth = Mathf.Clamp01(cur / max);
        }
        if (stamina && stamina.isActiveAndEnabled)
        {
            shownStamina = stamina.Normalized;
        }

        if (healthFill) healthFill.fillAmount  = shownHealth;
        if (staminaFill) staminaFill.fillAmount = shownStamina;
    }

    void ApplyColoursInstant()
    {
        if (useStaticColors || !useGradient)
        {
            if (healthFill) healthFill.color  = healthColor;
            if (staminaFill) staminaFill.color = staminaColor;
        }
        else
        {
            if (healthFill && healthGradient != null) healthFill.color  = healthGradient.Evaluate(shownHealth);
            if (staminaFill && staminaGradient != null) staminaFill.color = staminaGradient.Evaluate(shownStamina);
        }
    }

    void ApplyAnchorAndSizes()
    {
        if (!rt) rt = GetComponent<RectTransform>();

        Vector2 min = Vector2.zero, max = Vector2.zero, pivot = Vector2.zero, pos = Vector2.zero;
        switch (anchorCorner)
        {
            case Corner.TopLeft: min = max = pivot = new Vector2(0f, 1f); pos = new Vector2(padding.x, -padding.y); break;
            case Corner.TopRight: min = max = pivot = new Vector2(1f, 1f); pos = new Vector2(-padding.x, -padding.y); break;
            case Corner.BottomLeft: min = max = pivot = new Vector2(0f, 0f); pos = new Vector2(padding.x, padding.y); break;
            case Corner.BottomRight: min = max = pivot = new Vector2(1f, 0f); pos = new Vector2(-padding.x, padding.y); break;
        }
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot; rt.anchoredPosition = pos;

        if (healthFill)
        {
            healthRT = healthFill.rectTransform;
            healthRT.anchorMin = healthRT.anchorMax = healthRT.pivot = new Vector2(0f, 1f);
            healthRT.sizeDelta = healthSize;
            healthRT.anchoredPosition = Vector2.zero;
        }
        if (staminaFill)
        {
            staminaRT = staminaFill.rectTransform;
            staminaRT.anchorMin = staminaRT.anchorMax = staminaRT.pivot = new Vector2(0f, 1f);
            staminaRT.sizeDelta = staminaSize;
            staminaRT.anchoredPosition = new Vector2(0f, -(healthSize.y + barSpacing));
        }
    }

    static void EnsureImageIsFilledHorizontalLeft(Image img)
    {
        if (!img) return;
        if (img.type != Image.Type.Filled) img.type = Image.Type.Filled;
        if (img.fillMethod != Image.FillMethod.Horizontal) img.fillMethod = Image.FillMethod.Horizontal;
        if (img.fillOrigin != (int)Image.OriginHorizontal.Left) img.fillOrigin = (int)Image.OriginHorizontal.Left;
        if (img.fillAmount <= 0f) img.fillAmount = 1f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!rt) rt = GetComponent<RectTransform>();
        ApplyAnchorAndSizes();
        ImmediateSnap();
        ApplyColoursInstant();
        EnsureImageIsFilledHorizontalLeft(healthFill);
        EnsureImageIsFilledHorizontalLeft(staminaFill);
    }
#endif
}
