using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EmberShardCounterUI : MonoBehaviour
{
    [Header("UI Refs")]
    public Text countText;           // assign a Text (legacy) under HUDCanvas
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


