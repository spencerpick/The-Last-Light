// Tiny HUD updater for shard count (TextMeshPro).
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class EmberShardCounterUI : MonoBehaviour
{
    [Header("UI Refs")]
    public TMP_Text countText;       // assign TextMeshProUGUI under HUDCanvas
    public string prefix = "x";     // shown before the number
    public int lastValue = -1;

    void Update()
    {
        if (!countText) return;
        int v = GameManager.Instance ? GameManager.Instance.GetEmberFragments() : 0;
        if (v != lastValue)
        {
            lastValue = v;
            countText.text = prefix + v.ToString();
        }
    }
}


