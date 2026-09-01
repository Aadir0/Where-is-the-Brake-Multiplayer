using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string MUSIC_VOLUME_PREFS_KEY = "MusicVolume";
    [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.75f;

    private float currentVolume = 0.75f;
    private AudioSource musicAudioSource;

    public float CurrentVolume => currentVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicAudioSource = GetComponent<AudioSource>();

        LoadVolume();
    }

    public void SetMusicVolume(float volume)
    {
        currentVolume = Mathf.Clamp01(volume);

        AudioListener.volume = currentVolume;

        if (musicAudioSource != null)
        {
            musicAudioSource.volume = currentVolume;
        }

        PlayerPrefs.SetFloat(MUSIC_VOLUME_PREFS_KEY, currentVolume);
        PlayerPrefs.Save();
    }

    public float GetMusicVolume()
    {
        return currentVolume;
    }

    private void LoadVolume()
    {
        currentVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_PREFS_KEY, defaultVolume);
        AudioListener.volume = currentVolume;

        if (musicAudioSource != null)
        {
            musicAudioSource.volume = currentVolume;
        }
    }
}

