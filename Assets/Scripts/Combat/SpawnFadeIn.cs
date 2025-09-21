using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpawnFadeIn : MonoBehaviour
{
    [Header("Fade Settings")]
    public float duration = 2.5f;
    public float startAlpha = 0f;       // 0 = invisible, 1 = opaque
    public Color startTint = Color.black; // black overlay at start
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Targets")]
    public bool includeChildren = true;
    public bool affectSpriteRenderers = true;
    public bool affectLightIntensity = false; // optional: dim lights then restore

    struct SpriteInfo
    {
        public SpriteRenderer renderer;
        public Color originalColor;
    }

    struct LightInfo
    {
        public Light light;
        public float originalIntensity;
    }

    readonly List<SpriteInfo> spriteTargets = new List<SpriteInfo>();
    readonly List<LightInfo> lightTargets = new List<LightInfo>();

    void OnEnable()
    {
        CacheTargets();
        ApplyStartState();
        StartCoroutine(FadeRoutine());
    }

    void CacheTargets()
    {
        spriteTargets.Clear();
        lightTargets.Clear();

        if (affectSpriteRenderers)
        {
            if (includeChildren)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    spriteTargets.Add(new SpriteInfo { renderer = sr, originalColor = sr.color });
                }
            }
            else
            {
                var sr = GetComponent<SpriteRenderer>();
                if (sr)
                    spriteTargets.Add(new SpriteInfo { renderer = sr, originalColor = sr.color });
            }
        }

        if (affectLightIntensity)
        {
            if (includeChildren)
            {
                foreach (var l in GetComponentsInChildren<Light>(true))
                {
                    lightTargets.Add(new LightInfo { light = l, originalIntensity = l.intensity });
                }
            }
            else
            {
                var l = GetComponent<Light>();
                if (l)
                    lightTargets.Add(new LightInfo { light = l, originalIntensity = l.intensity });
            }
        }
    }

    void ApplyStartState()
    {
        foreach (var it in spriteTargets)
        {
            if (!it.renderer) continue;
            Color c = it.originalColor;
            // start darker and more transparent
            c.a = Mathf.Clamp01(startAlpha);
            Color darkened = Color.Lerp(it.originalColor, startTint, 1f);
            darkened.a = c.a;
            it.renderer.color = darkened;
        }

        foreach (var it in lightTargets)
        {
            if (!it.light) continue;
            it.light.intensity = 0f;
        }
    }

    IEnumerator FadeRoutine()
    {
        float t = 0f;
        while (t < duration)
        {
            float u = duration <= 0f ? 1f : Mathf.Clamp01(t / Mathf.Max(0.0001f, duration));
            float k = curve.Evaluate(u);
            // Lerp to original
            for (int i = 0; i < spriteTargets.Count; i++)
            {
                var info = spriteTargets[i];
                if (!info.renderer) continue;
                // Lerp color from startTint to original color
                Color target = info.originalColor;
                Color from = Color.Lerp(info.originalColor, startTint, 1f);
                Color now = Color.Lerp(from, target, k);
                now.a = Mathf.Lerp(Mathf.Clamp01(startAlpha), info.originalColor.a, k);
                info.renderer.color = now;
            }
            for (int i = 0; i < lightTargets.Count; i++)
            {
                var info = lightTargets[i];
                if (!info.light) continue;
                info.light.intensity = Mathf.Lerp(0f, info.originalIntensity, k);
            }
            t += Time.deltaTime;
            yield return null;
        }

        // Ensure final values
        for (int i = 0; i < spriteTargets.Count; i++)
        {
            var info = spriteTargets[i];
            if (!info.renderer) continue;
            info.renderer.color = info.originalColor;
        }
        for (int i = 0; i < lightTargets.Count; i++)
        {
            var info = lightTargets[i];
            if (!info.light) continue;
            info.light.intensity = info.originalIntensity;
        }

        // Finished: component no longer needed
        Destroy(this);
    }
}


