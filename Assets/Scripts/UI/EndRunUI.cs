// Fades to black and shows simple run stats (time, shards, kills).
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

[DisallowMultipleComponent]
public class EndRunUI : MonoBehaviour
{
    [Header("Refs")]
    public CanvasGroup fadeGroup;          // full-screen CanvasGroup to fade
    public TMP_Text timeText;
    public TMP_Text shardsText;
    public TMP_Text killsText;

    [Header("Settings")]
    public float defaultFadeSeconds = 5f;

    void Awake()
    {
        if (!fadeGroup) fadeGroup = GetComponentInChildren<CanvasGroup>(true);
        if (fadeGroup)
        {
            // Keep the GameObject active so we can StartCoroutine; just make it invisible/non-interactive.
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable = false;
        }
        if (timeText) timeText.text = "";
        if (shardsText) shardsText.text = "";
        if (killsText) killsText.text = "";
    }

    public void StartEndRun(float fadeSeconds, float runTime, int shards, int kills)
    {
        StopAllCoroutines();
        StartCoroutine(FadeAndShow(fadeSeconds > 0 ? fadeSeconds : defaultFadeSeconds, runTime, shards, kills));
    }

    IEnumerator FadeAndShow(float seconds, float runTime, int shards, int kills)
    {
        if (fadeGroup)
        {
            fadeGroup.blocksRaycasts = true;
            fadeGroup.interactable = true;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                fadeGroup.alpha = Mathf.Clamp01(t / seconds);
                yield return null;
            }
            fadeGroup.alpha = 1f;
        }

        if (timeText) timeText.text = $"Time: {runTime:F1}s";
        if (shardsText) shardsText.text = $"Ember Shards: {shards}";
        if (killsText) killsText.text = $"Enemies Defeated: {kills}";
    }
}


