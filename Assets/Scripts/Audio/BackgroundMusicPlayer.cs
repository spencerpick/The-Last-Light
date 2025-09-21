using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BackgroundMusicPlayer : MonoBehaviour
{
    [Header("Playlist")]
    public List<AudioClip> tracks = new List<AudioClip>();
    public bool shuffle = true;
    public bool loopPlaylist = true;
    public bool playOnStart = true;

    [Header("Audio")] 
    [Range(0f, 1f)] public float volume = 0.6f;
    public float crossfadeSeconds = 0f; // set >0 for simple crossfade
    public AudioSource audioSource;      // optional; created if missing

    int lastIndex = -1;
    bool started;

    void Awake()
    {
        if (!audioSource)
        {
            audioSource = GetComponent<AudioSource>();
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.loop = false;       // we control next track
        audioSource.volume = volume;
    }

    void Start()
    {
        if (playOnStart) StartPlaylist();
    }

    void Update()
    {
        if (!started) return;
        if (!audioSource || tracks.Count == 0) return;

        if (!audioSource.isPlaying)
        {
            PlayNext();
        }
    }

    public void StartPlaylist()
    {
        started = true;
        PlayNext(true);
    }

    public void StopPlaylist()
    {
        started = false;
        if (audioSource) audioSource.Stop();
    }

    void PlayNext(bool force = false)
    {
        if (tracks.Count == 0) return;

        int nextIndex = 0;
        if (shuffle)
        {
            // avoid immediate repeat when possible
            if (tracks.Count == 1) nextIndex = 0;
            else
            {
                int tries = 0;
                do { nextIndex = Random.Range(0, tracks.Count); tries++; }
                while (nextIndex == lastIndex && tries < 5);
            }
        }
        else
        {
            nextIndex = (lastIndex + 1) % tracks.Count;
        }

        if (!loopPlaylist && nextIndex <= lastIndex && !force)
        {
            // reached end and no looping requested
            started = false;
            return;
        }

        lastIndex = nextIndex;
        var clip = tracks[nextIndex];
        if (!clip) return;

        if (crossfadeSeconds > 0f && audioSource.isPlaying)
        {
            StopAllCoroutines();
            StartCoroutine(CrossfadeTo(clip, crossfadeSeconds));
        }
        else
        {
            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.Play();
        }
    }

    System.Collections.IEnumerator CrossfadeTo(AudioClip next, float seconds)
    {
        float t = 0f;
        float startVol = audioSource.volume;
        while (t < seconds)
        {
            t += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(t / seconds));
            yield return null;
        }
        audioSource.Stop();
        audioSource.clip = next;
        audioSource.Play();
        t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(0f, volume, Mathf.Clamp01(t / seconds));
            yield return null;
        }
        audioSource.volume = volume;
    }
}


