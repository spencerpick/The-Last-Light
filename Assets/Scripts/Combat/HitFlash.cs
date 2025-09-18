using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Hit Flash")]
public class HitFlash : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("If empty, all SpriteRenderers in children are auto-collected.")]
    [SerializeField] private List<SpriteRenderer> renderers = new List<SpriteRenderer>();

    [Header("Flash")]
    public Color flashColor = Color.red;
    [Range(0f, 1f)] public float maxBlend = 0.6f;   // mix amount toward flashColor
    public float riseTime = 0.04f;                  // time to reach maxBlend
    public float fallTime = 0.15f;                  // time to fade back

    private Color[] baseColors;
    private Coroutine routine;

    void Awake()
    {
        if (renderers.Count == 0)
            renderers.AddRange(GetComponentsInChildren<SpriteRenderer>(true));

        baseColors = new Color[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i]) baseColors[i] = renderers[i].color;
    }

    public void Flash()
    {
        if (!isActiveAndEnabled) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(DoFlash());
    }

    // Convenience so you can hook a UnityEvent<float,float> if you ever want to.
    public void OnDamagedEvent(float _newHealth, float _damageDealt) => Flash();

    // Convenience so BroadcastMessage("OnDamaged", amount) also works.
    public void OnDamaged(float _amount) => Flash();

    private IEnumerator DoFlash()
    {
        float t = 0f;
        while (t < riseTime)
        {
            t += Time.deltaTime;
            SetBlend(riseTime > 0f ? Mathf.Lerp(0f, maxBlend, t / riseTime) : maxBlend);
            yield return null;
        }
        t = 0f;
        while (t < fallTime)
        {
            t += Time.deltaTime;
            SetBlend(fallTime > 0f ? Mathf.Lerp(maxBlend, 0f, t / fallTime) : 0f);
            yield return null;
        }
        SetBlend(0f);
        routine = null;
    }

    private void SetBlend(float a)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (!r) continue;

            var baseCol = baseColors[i];
            var target = new Color(flashColor.r, flashColor.g, flashColor.b, baseCol.a);
            r.color = Color.Lerp(baseCol, target, a);
        }
    }

    void OnDisable() => Restore();
    void OnDestroy() => Restore();

    private void Restore()
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i]) renderers[i].color = baseColors[i];
    }
}
