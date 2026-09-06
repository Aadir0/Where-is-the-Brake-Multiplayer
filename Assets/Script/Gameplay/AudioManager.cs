using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string MUSIC_VOLUME_PREFS_KEY = "MusicVolume";
    private const string SFX_VOLUME_PREFS_KEY = "SfxVolume";

    [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float defaultSfxVolume = 0.75f;

    private float currentVolume = 0.75f;
    private float currentSfxVolume = 0.75f;
    private AudioSource musicAudioSource;

    public float CurrentVolume => currentVolume;
    public float CurrentSfxVolume => currentSfxVolume;

    public event System.Action<float> OnSfxVolumeChanged;
    public event System.Action<float> OnMusicVolumeChanged;

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

        if (musicAudioSource != null)
        {
            musicAudioSource.volume = currentVolume;
        }

        PlayerPrefs.SetFloat(MUSIC_VOLUME_PREFS_KEY, currentVolume);
        PlayerPrefs.Save();
        OnMusicVolumeChanged?.Invoke(currentVolume);
    }

    public float GetMusicVolume()
    {
        return currentVolume;
    }

    public void SetSfxVolume(float volume)
    {
        currentSfxVolume = Mathf.Clamp01(volume);

        PlayerPrefs.SetFloat(SFX_VOLUME_PREFS_KEY, currentSfxVolume);
        PlayerPrefs.Save();
        OnSfxVolumeChanged?.Invoke(currentSfxVolume);
    }

    public float GetSfxVolume()
    {
        return currentSfxVolume;
    }

    private void LoadVolume()
    {
        currentVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_PREFS_KEY, defaultVolume);
        currentSfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_PREFS_KEY, defaultSfxVolume);

        if (musicAudioSource != null)
        {
            musicAudioSource.volume = currentVolume;
        }
    }
}

