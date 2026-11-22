using UnityEngine;

// Sons du jeu, générés par le code (aucun fichier audio à importer) : pose, tir, chute, victoire.
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Range(0f, 1f)] public float volume = 0.6f;
    public int maxShotsPerFrame = 2;          // évite la cacophonie quand beaucoup de soldats tirent

    private AudioSource source;
    private AudioClip placeClip, shotClip, deathClip, victoryClip, explosionClip, whistleClip, collapseClip;
    private int shotsThisFrame;
    private int lastFrame = -1;
    private const int Rate = 44100;

    void Awake()
    {
        Instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        placeClip = MakeTone("Place", 0.09f, 520f, 880f, 0f);
        shotClip = MakeTone("Shot", 0.07f, 180f, 90f, 0.85f);
        deathClip = MakeTone("Death", 0.25f, 300f, 110f, 0.3f);
        explosionClip = MakeTone("Explosion", 0.9f, 90f, 30f, 0.9f);
        whistleClip = MakeTone("Whistle", 0.8f, 1400f, 600f, 0.05f);
        collapseClip = MakeTone("Collapse", 1.2f, 70f, 25f, 0.95f);
        victoryClip = MakeArpeggio("Victory", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void Place() { if (Instance != null) Instance.Play(Instance.placeClip, 0.8f); }
    public static void Death() { if (Instance != null) Instance.Play(Instance.deathClip, 0.6f); }
    public static void Explosion() { if (Instance != null) Instance.Play(Instance.explosionClip, 1f); }
    public static void Whistle() { if (Instance != null) Instance.Play(Instance.whistleClip, 0.35f); }
    public static void Collapse() { if (Instance != null) Instance.Play(Instance.collapseClip, 0.8f); }
    public static void Victory() { if (Instance != null) Instance.Play(Instance.victoryClip, 1f); }

    public static void Shot()
    {
        if (Instance == null) return;
        if (Time.frameCount != Instance.lastFrame) { Instance.lastFrame = Time.frameCount; Instance.shotsThisFrame = 0; }
        if (Instance.shotsThisFrame >= Instance.maxShotsPerFrame) return;
        Instance.shotsThisFrame++;
        Instance.Play(Instance.shotClip, 0.35f);
    }

    private void Play(AudioClip clip, float gain)
    {
        source.pitch = Random.Range(0.95f, 1.05f);
        source.PlayOneShot(clip, volume * gain);
    }

    // Son court : fréquence qui glisse de f0 à f1, avec une part de bruit (pour le tir) et une décroissance rapide.
    private static AudioClip MakeTone(string name, float duration, float f0, float f1, float noise)
    {
        int n = Mathf.CeilToInt(duration * Rate);
        var data = new float[n];
        float phase = 0f;
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / Rate;
            float tone = Mathf.Sin(phase);
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float env = Mathf.Min(1f, i / (Rate * 0.004f)) * Mathf.Pow(1f - t, 3f);
            data[i] = Mathf.Lerp(tone, white, noise) * env * 0.8f;
        }
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip MakeArpeggio(string name, float[] notes, float noteDuration)
    {
        int per = Mathf.CeilToInt(noteDuration * Rate);
        int n = per * notes.Length + Rate / 3;
        var data = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            float phase = 0f;
            int start = k * per;
            int len = k == notes.Length - 1 ? n - start : per;
            for (int i = 0; i < len; i++)
            {
                phase += 2f * Mathf.PI * notes[k] / Rate;
                float t = (float)i / len;
                float env = Mathf.Min(1f, i / (Rate * 0.005f)) * (1f - t);
                data[start + i] += (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f)) * env * 0.5f;
            }
        }
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
