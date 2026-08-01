using UnityEngine;

namespace GypsyAliens.Audio
{
    /// <summary>
    /// Master game volume via <see cref="AudioListener.volume"/> (music + SFX).
    /// Default is half of Unity's full listener volume.
    /// </summary>
    public static class GameAudioSettings
    {
        public const string PrefsKey = "GypsyAliens.MasterVolume";
        public const float DefaultVolume = 0.5f;

        public static float MasterVolume
        {
            get => AudioListener.volume;
            set => SetMasterVolume(value);
        }

        public static void ApplySavedOrDefault()
        {
            var volume = PlayerPrefs.HasKey(PrefsKey)
                ? PlayerPrefs.GetFloat(PrefsKey, DefaultVolume)
                : DefaultVolume;
            AudioListener.volume = Mathf.Clamp01(volume);
        }

        public static void SetMasterVolume(float volume)
        {
            var clamped = Mathf.Clamp01(volume);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(PrefsKey, clamped);
            PlayerPrefs.Save();
        }
    }
}
