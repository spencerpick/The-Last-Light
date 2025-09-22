// Manages a simple temporary “blindness” effect on the player’s Light2D.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class PlayerVisionController : MonoBehaviour
{
    [Header("Refs")]
    public Light2D playerLight;             // assign the light circle on the player

    [Header("Blindness")]
    public float defaultBlindSeconds = 5f;
    public float blindInnerRadius = 0.6f;   // small radius while blinded
    public float fadeSeconds = 0.25f;       // fade in/out speed
    [Tooltip("If light type is Sprite, scale to this factor while blinded (1 = original size).")]
    public float blindSpriteScale = 0.25f;
    [Tooltip("Disable any EmberFlicker scripts while blinded so they don't fight the effect.")]
    public bool disableFlickerDuringBlind = true;

    // store original values
    float origInner;
    float origOuter;
    float origIntensity;
    Vector3 origScale;
    Coroutine active;
    bool isBlinded;
    float blindUntilTime;
    List<Behaviour> storedDisabledFlickers;

    void Awake()
    {
        TryResolveLight();
    }
    void OnEnable() { if (!playerLight) TryResolveLight(); }

    public void BlindForSeconds(float seconds)
    {
        if (!playerLight) TryResolveLight();
        if (!playerLight)
        {
            Debug.LogWarning("[PlayerVision] No Light2D found under Player at runtime. Add a child Light2D (Point recommended).", this);
            return;
        }
        // If already blinded, just reset the timer (no stacking)
        if (isBlinded)
        {
            blindUntilTime = Time.time + Mathf.Max(0.01f, seconds);
            return;
        }

        if (active != null) StopCoroutine(active);
        active = StartCoroutine(BlindRoutine(seconds));
    }

    IEnumerator BlindRoutine(float seconds)
    {
        // Mark blinded and set end time
        isBlinded = true;
        blindUntilTime = Time.time + Mathf.Max(0.01f, seconds);

        // Pause flicker while blinded (optional)
        if (disableFlickerDuringBlind && playerLight && storedDisabledFlickers == null)
        {
            storedDisabledFlickers = new List<Behaviour>();
            var bhs = playerLight.GetComponents<Behaviour>();
            foreach (var b in bhs)
            {
                if (!b || !b.enabled) continue;
                var typeName = b.GetType().Name;
                if (typeName.Contains("Flicker") || typeName.Contains("EmberFlicker"))
                {
                    b.enabled = false;
                    storedDisabledFlickers.Add(b);
                }
            }
        }

        // fade to blind
        if (playerLight.lightType == Light2D.LightType.Point)
        {
            yield return FadeLight(origInner, origOuter, origIntensity, blindInnerRadius, blindInnerRadius + 0.8f, origIntensity * 0.8f, fadeSeconds);
        }
        else // Sprite / other types -> scale transform + intensity
        {
            yield return FadeLightScale(origScale, origIntensity,
                                        Vector3.one * Mathf.Max(0.02f, blindSpriteScale), origIntensity * 0.8f,
                                        fadeSeconds);
        }
        // hold (non-stacking: additional hits extend/reset blindUntilTime)
        while (Time.time < blindUntilTime)
            yield return null;
        // fade back
        if (playerLight.lightType == Light2D.LightType.Point)
        {
            yield return FadeLight(playerLight.pointLightInnerRadius, playerLight.pointLightOuterRadius, playerLight.intensity,
                                   origInner, origOuter, origIntensity, fadeSeconds);
        }
        else
        {
            yield return FadeLightScale(playerLight.transform.localScale, playerLight.intensity,
                                        origScale, origIntensity, fadeSeconds);
        }

        // Re-enable flicker
        if (storedDisabledFlickers != null)
        {
            foreach (var b in storedDisabledFlickers)
                if (b) b.enabled = true;
            storedDisabledFlickers = null;
        }
        isBlinded = false;
        active = null;
    }

    IEnumerator FadeLight(float fromInner, float fromOuter, float fromIntensity,
                          float toInner, float toOuter, float toIntensity, float duration)
    {
        if (!playerLight) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = duration > 0.0001f ? Mathf.Clamp01(t / duration) : 1f;
            playerLight.pointLightInnerRadius = Mathf.Lerp(fromInner, toInner, k);
            playerLight.pointLightOuterRadius = Mathf.Lerp(fromOuter, toOuter, k);
            playerLight.intensity = Mathf.Lerp(fromIntensity, toIntensity, k);
            yield return null;
        }
        playerLight.pointLightInnerRadius = toInner;
        playerLight.pointLightOuterRadius = toOuter;
        playerLight.intensity = toIntensity;
    }

    IEnumerator FadeLightScale(Vector3 fromScale, float fromIntensity,
                               Vector3 toScale, float toIntensity, float duration)
    {
        if (!playerLight) yield break;
        float t = 0f;
        var tr = playerLight.transform;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = duration > 0.0001f ? Mathf.Clamp01(t / duration) : 1f;
            tr.localScale = Vector3.Lerp(fromScale, toScale, k);
            playerLight.intensity = Mathf.Lerp(fromIntensity, toIntensity, k);
            yield return null;
        }
        tr.localScale = toScale;
        playerLight.intensity = toIntensity;
    }

    void TryResolveLight()
    {
        // Try local children first (including inactive)
        if (!playerLight)
            playerLight = GetComponentInChildren<Light2D>(true);

        // If still not found, try the Player-tagged object root and search its children
        if (!playerLight)
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo) playerLight = playerGo.GetComponentInChildren<Light2D>(true);
        }

        if (playerLight)
        {
            origInner = playerLight.pointLightInnerRadius;
            origOuter = playerLight.pointLightOuterRadius;
            origIntensity = playerLight.intensity;
            origScale = playerLight.transform.localScale;
        }
    }
}


