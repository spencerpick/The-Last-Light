using UnityEngine;

namespace Audio
{
    public static class OneShotAudio
    {
        // Returns the effective duration that will elapse until the sound finishes (seconds)
        public static float Play(Vector3 worldPos, AudioClip clip, float volume = 1f, float pitchMin = 1f, float pitchMax = 1f)
        {
            if (!clip) return 0f;
            float pitch = Mathf.Clamp(Random.Range(Mathf.Min(pitchMin, pitchMax), Mathf.Max(pitchMin, pitchMax)), 0.1f, 3f);
            float duration = clip.length / Mathf.Max(0.01f, pitch);

            var go = new GameObject("OneShotAudio");
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f; // 2D
            src.playOnAwake = false;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = pitch;
            src.priority = 64;
            src.Play();
            Object.Destroy(go, duration);
            return duration;
        }
    }
}


