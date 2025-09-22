// Tiny flicker for a Light2D: randomizes intensity and scale every few frames.
using UnityEngine;
using UnityEngine.Rendering.Universal; // Needed for Light2D

public class EmberFlicker : MonoBehaviour
{
    [Header("References")]
    public Light2D light2D;

    [Header("Intensity Flicker")]
    public float intensityMin = 0.7f;
    public float intensityMax = 1.1f;

    [Header("Size Flicker")]
    public float sizeMin = 3.5f;
    public float sizeMax = 4.5f;

    [Header("Flicker Timing")]
    public float flickerSpeed = 0.12f; // Lower = faster flicker

    private float flickerTimer = 0f;

    void Reset()
    {
        light2D = GetComponent<Light2D>();
    }

    void Update()
    {
        flickerTimer += Time.deltaTime;
        if (flickerTimer >= flickerSpeed)
        {
            // Flicker the intensity
            if (light2D != null)
                light2D.intensity = Random.Range(intensityMin, intensityMax);

            // Flicker the size (scale the parent GameObject)
            float newScale = Random.Range(sizeMin, sizeMax);
            transform.localScale = new Vector3(newScale, newScale, 1f);

            flickerTimer = 0f;
        }
    }
}
