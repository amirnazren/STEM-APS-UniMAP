using System;
using UnityEngine;

namespace EcoBotBuilders.Audio
{
    /// <summary>
    /// Manages adaptive background music and sound effects with day/night crossfading.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AdaptiveAudioEngine : MonoBehaviour
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSourceA;
        [SerializeField] private AudioSource musicSourceB;
        [SerializeField] private AudioSource sfxSource;

        [Header("Music Libraries")]
        [SerializeField] private AudioClip[] dayTracks;
        [SerializeField] private AudioClip[] nightTracks;

        [Header("Mixing")]
        [SerializeField, Min(0f)] private float crossfadeDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Min(0f)] private float volumeMultiplier = 1f;

        [Header("Timing")]
        [SerializeField] private bool startMuted;
        [SerializeField] private bool playOnStart = true;

        private static AdaptiveAudioEngine instance;
        private AudioSource activeMusicSource;
        private AudioSource inactiveMusicSource;
        private bool currentIsDay = true;
        private bool isCrossfading;
        private float crossfadeTimer;
        private AudioClip currentTrack;
        private int dayTrackIndex;
        private int nightTrackIndex;
        private bool isMuted;
        private float cachedMusicVolume = 1f;
        private float cachedSfxVolume = 1f;

        /// <summary>
        /// Gets the singleton AdaptiveAudioEngine instance, if one exists in the scene.
        /// </summary>
        public static AdaptiveAudioEngine Instance => instance;

        /// <summary>
        /// Gets whether the engine is currently muted.
        /// </summary>
        public bool IsMuted => isMuted;

        /// <summary>
        /// Gets or sets a global multiplier applied to music and SFX volume.
        /// </summary>
        public float VolumeMultiplier
        {
            get => volumeMultiplier;
            set
            {
                volumeMultiplier = Mathf.Max(0f, value);
                ApplyVolumes();
            }
        }

        /// <summary>
        /// Returns true if the current phase is day.
        /// </summary>
        public bool IsDayPhase => currentIsDay;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureSources();
            ConfigureSources();
            SetMuted(startMuted);
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayPhaseTrack(currentIsDay);
            }
        }

        private void Update()
        {
            if (!isCrossfading)
            {
                return;
            }

            crossfadeTimer += Time.deltaTime;
            float t = crossfadeDuration <= 0f ? 1f : Mathf.Clamp01(crossfadeTimer / crossfadeDuration);
            UpdateCrossfade(t);

            if (t >= 1f)
            {
                CompleteCrossfade();
            }
        }

        private void OnDisable()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Updates the active day phase and crossfades music if the phase changes.
        /// </summary>
        /// <param name="isDay">True when the game is in daytime.</param>
        public void SetDayPhase(bool isDay)
        {
            if (currentIsDay == isDay)
            {
                return;
            }

            currentIsDay = isDay;
            PlayPhaseTrack(isDay);
        }

        /// <summary>
        /// Plays a sound effect one-shot using the SFX source.
        /// </summary>
        /// <param name="clip">The clip to play.</param>
        public void PlaySfx(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (sfxSource == null)
            {
                EnsureSources();
            }

            if (sfxSource == null)
            {
                return;
            }

            sfxSource.PlayOneShot(clip, GetEffectiveSfxVolume());
        }

        /// <summary>
        /// Stops all currently playing music and SFX.
        /// </summary>
        public void StopAllAudio()
        {
            if (musicSourceA != null) musicSourceA.Stop();
            if (musicSourceB != null) musicSourceB.Stop();
            if (sfxSource != null) sfxSource.Stop();
            isCrossfading = false;
            currentTrack = null;
        }

        /// <summary>
        /// Mutes all engine audio.
        /// </summary>
        public void Mute()
        {
            SetMuted(true);
        }

        /// <summary>
        /// Restores engine audio using the configured volume settings.
        /// </summary>
        public void Unmute()
        {
            SetMuted(false);
        }

        /// <summary>
        /// Sets the mute state for the entire audio engine.
        /// </summary>
        /// <param name="muted">Whether audio should be muted.</param>
        public void SetMuted(bool muted)
        {
            isMuted = muted;
            ApplyVolumes();
        }

        /// <summary>
        /// Sets the music and SFX volume multipliers.
        /// </summary>
        /// <param name="music">Music volume in the 0-1 range.</param>
        /// <param name="sfx">SFX volume in the 0-1 range.</param>
        public void SetVolumes(float music, float sfx)
        {
            cachedMusicVolume = Mathf.Clamp01(music);
            cachedSfxVolume = Mathf.Clamp01(sfx);
            ApplyVolumes();
        }

        private void PlayPhaseTrack(bool dayPhase)
        {
            AudioClip nextTrack = SelectNextTrack(dayPhase);
            if (nextTrack == null)
            {
                return;
            }

            if (activeMusicSource == null || inactiveMusicSource == null)
            {
                EnsureSources();
            }

            if (activeMusicSource == null || inactiveMusicSource == null)
            {
                return;
            }

            if (!activeMusicSource.isPlaying || currentTrack == null)
            {
                StartTrackImmediately(nextTrack);
                return;
            }

            if (currentTrack == nextTrack && activeMusicSource.clip == nextTrack)
            {
                return;
            }

            BeginCrossfade(nextTrack);
        }

        private void StartTrackImmediately(AudioClip clip)
        {
            currentTrack = clip;
            isCrossfading = false;

            activeMusicSource.Stop();
            activeMusicSource.clip = clip;
            activeMusicSource.loop = true;
            activeMusicSource.volume = 0f;
            activeMusicSource.Play();
            ApplyVolumes();
        }

        private void BeginCrossfade(AudioClip nextTrack)
        {
            if (inactiveMusicSource == null || activeMusicSource == null)
            {
                return;
            }

            currentTrack = nextTrack;
            isCrossfading = true;
            crossfadeTimer = 0f;

            inactiveMusicSource.Stop();
            inactiveMusicSource.clip = nextTrack;
            inactiveMusicSource.loop = true;
            inactiveMusicSource.volume = 0f;
            inactiveMusicSource.Play();
        }

        private void UpdateCrossfade(float t)
        {
            if (activeMusicSource == null || inactiveMusicSource == null)
            {
                return;
            }

            float targetMusicVolume = GetEffectiveMusicVolume();
            activeMusicSource.volume = Mathf.Lerp(targetMusicVolume, 0f, t);
            inactiveMusicSource.volume = Mathf.Lerp(0f, targetMusicVolume, t);
        }

        private void CompleteCrossfade()
        {
            if (activeMusicSource == null || inactiveMusicSource == null)
            {
                isCrossfading = false;
                return;
            }

            activeMusicSource.Stop();
            activeMusicSource.volume = 0f;

            AudioSource previousActive = activeMusicSource;
            activeMusicSource = inactiveMusicSource;
            inactiveMusicSource = previousActive;

            isCrossfading = false;
            crossfadeTimer = 0f;
            ApplyVolumes();
        }

        private AudioClip SelectNextTrack(bool dayPhase)
        {
            AudioClip[] tracks = dayPhase ? dayTracks : nightTracks;
            if (tracks == null || tracks.Length == 0)
            {
                return null;
            }

            int index = dayPhase ? dayTrackIndex : nightTrackIndex;
            index = Mathf.Clamp(index, 0, tracks.Length - 1);

            AudioClip clip = tracks[index];
            if (clip == null)
            {
                clip = FindFirstValidClip(tracks);
                index = Array.IndexOf(tracks, clip);
                if (index < 0)
                {
                    return null;
                }
            }

            if (dayPhase)
            {
                dayTrackIndex = (index + 1) % tracks.Length;
            }
            else
            {
                nightTrackIndex = (index + 1) % tracks.Length;
            }

            return clip;
        }

        private static AudioClip FindFirstValidClip(AudioClip[] tracks)
        {
            if (tracks == null)
            {
                return null;
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] != null)
                {
                    return tracks[i];
                }
            }

            return null;
        }

        private void EnsureSources()
        {
            if (musicSourceA == null)
            {
                musicSourceA = gameObject.AddComponent<AudioSource>();
            }

            if (musicSourceB == null)
            {
                musicSourceB = gameObject.AddComponent<AudioSource>();
            }

            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }

            if (activeMusicSource == null)
            {
                activeMusicSource = musicSourceA;
                inactiveMusicSource = musicSourceB;
            }
            else if (inactiveMusicSource == null)
            {
                inactiveMusicSource = activeMusicSource == musicSourceA ? musicSourceB : musicSourceA;
            }
        }

        private void ConfigureSources()
        {
            ConfigureMusicSource(musicSourceA);
            ConfigureMusicSource(musicSourceB);

            if (sfxSource != null)
            {
                sfxSource.playOnAwake = false;
                sfxSource.loop = false;
                sfxSource.spatialBlend = 0f;
                sfxSource.priority = 128;
            }
        }

        private static void ConfigureMusicSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.priority = 128;
        }

        private void ApplyVolumes()
        {
            float musicVolumeFinal = GetEffectiveMusicVolume();
            float sfxVolumeFinal = GetEffectiveSfxVolume();

            if (musicSourceA != null && musicSourceA != inactiveMusicSource)
            {
                if (!isCrossfading || musicSourceA == activeMusicSource)
                {
                    musicSourceA.volume = musicVolumeFinal;
                }
            }

            if (musicSourceB != null && musicSourceB != inactiveMusicSource)
            {
                if (!isCrossfading || musicSourceB == activeMusicSource)
                {
                    musicSourceB.volume = musicVolumeFinal;
                }
            }

            if (!isCrossfading)
            {
                if (activeMusicSource != null)
                {
                    activeMusicSource.volume = musicVolumeFinal;
                }

                if (inactiveMusicSource != null)
                {
                    inactiveMusicSource.volume = 0f;
                }
            }

            if (sfxSource != null)
            {
                sfxSource.volume = sfxVolumeFinal;
            }
        }

        private float GetEffectiveMusicVolume()
        {
            return isMuted ? 0f : Mathf.Clamp01(cachedMusicVolume * volumeMultiplier);
        }

        private float GetEffectiveSfxVolume()
        {
            return isMuted ? 0f : Mathf.Clamp01(cachedSfxVolume * volumeMultiplier);
        }
    }
}
