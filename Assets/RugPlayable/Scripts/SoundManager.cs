using System.Collections.Generic;
using UnityEngine;

namespace RugMaster
{
    /// <summary>Tên clip trong Resources/RugMaster/Audio (bake vào scene qua RugPlayableAssets).</summary>
    public static class SoundFX
    {
        public const string MusicInGame = "BGM_INGAME";
        public const string UI_Button = "tap_button";
        public const string Win = "win";
        public const string Fail = "fail";
        // Các tiếng dưới đây game gốc tổng hợp lúc chạy (RugTrimAudio, AudioClip.Create) - Luna không hỗ trợ
        // AudioClip.Create/SetData nên đã render sẵn ra WAV với đúng công thức đó.
        public const string ShearPrefix = "trim_shear_";
        public const int ShearCount = 5;
        public const string Place = "trim_place";
        public const string Land = "fiber_land";
        public const string MotorLoop = "trimmer_motor_loop";
        public const string Packed = "basket_packed";
    }

    /// <summary>
    /// Âm thanh scene GamePlay theo SoundManager + RugTrimAudio của game gốc:
    /// - Nhạc nền BGM_INGAME loop (volume 0.6).
    /// - Cạo: tiếng kéo cắt (5 biến thể xoay vòng, cách nhau ≥ 34ms), đặt dao, bông rơi vào giỏ (≥ 45ms), đóng giỏ.
    /// - Motor dao loop: volume bám theo trạng thái giữ / đang cắt (LevelManager gọi Motor mỗi frame).
    /// - Thắng / kẹt ống / bấm nút: SFX như game gốc (mặc định âm lượng Minor 0.2).
    /// Playable không có nút bật/tắt tiếng nên luôn bật; không có rung (HapticManager) trên web.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoundManager : Singleton<SoundManager>
    {
        public enum SFXType { Important, Normal, Minor, Small }
        private const string MusicFolder = "RugMaster/Audio/Music/", SfxFolder = "RugMaster/Audio/SFX/";
        private const float MusicVolume = 0.6f, TrimVolume = 0.8f;
        private const float ShearInterval = .034f, LandInterval = .045f, SfxDuplicateDelay = .1f;
        // WAV motor lưu ở 1/2 biên độ (bản gốc vượt 1.0), nên volume nhân 2 để ra đúng độ lớn gốc.
        private const float MotorGain = 2f;
        private const int PoolSize = 6;

        private AudioSource musicSource, trimSource, motorSource;
        private readonly List<AudioSource> sfxPool = new List<AudioSource>();
        private int nextSfx, nextShear;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastSfxTime = new Dictionary<string, float>();
        private AudioClip[] shears;
        private float lastShear = -100, lastLand = -100;

        private void Awake()
        {
            // Scene GamePlay không có AudioListener (camera dựng bằng code chỉ có Camera) -> không nghe thấy gì.
            if (FindObjectOfType<AudioListener>() == null)
            {
                var cam = Camera.main;
                (cam != null ? cam.gameObject : gameObject).AddComponent<AudioListener>();
            }
            musicSource = Source(true); musicSource.volume = MusicVolume;
            trimSource = Source(false);
            motorSource = Source(true); motorSource.volume = 0;
            for (int i = 0; i < PoolSize; i++) sfxPool.Add(Source(false));
            shears = new AudioClip[SoundFX.ShearCount];
            for (int i = 0; i < shears.Length; i++) shears[i] = Clip(SfxFolder + SoundFX.ShearPrefix + i);
            motorSource.clip = Clip(SfxFolder + SoundFX.MotorLoop);
            if (motorSource.clip != null) motorSource.Play();
        }

        private void OnEnable()
        {
            EventManager.AddListener<OnTrimEvent>(OnTrim);
            EventManager.AddListener<OnFiberLandedEvent>(OnFiberLanded);
            EventManager.AddListener<OnBasketCompletedEvent>(OnBasketCompleted);
            EventManager.AddListener<OnLevelWonEvent>(OnLevelWon);
            EventManager.AddListener<OnLevelJammedEvent>(OnLevelJammed);
        }

        private void OnDisable()
        {
            EventManager.RemoveListener<OnTrimEvent>(OnTrim);
            EventManager.RemoveListener<OnFiberLandedEvent>(OnFiberLanded);
            EventManager.RemoveListener<OnBasketCompletedEvent>(OnBasketCompleted);
            EventManager.RemoveListener<OnLevelWonEvent>(OnLevelWon);
            EventManager.RemoveListener<OnLevelJammedEvent>(OnLevelJammed);
        }

        private void OnTrim(OnTrimEvent e)
        {
            if (Time.unscaledTime - lastShear < ShearInterval) return;
            lastShear = Time.unscaledTime;
            var clip = shears[nextShear++ % shears.Length];
            if (clip != null) trimSource.PlayOneShot(clip, TrimVolume);
        }

        private void OnFiberLanded(OnFiberLandedEvent e)
        {
            if (Time.unscaledTime - lastLand < LandInterval) return;
            lastLand = Time.unscaledTime;
            OneShot(SoundFX.Land, TrimVolume);
        }

        private void OnBasketCompleted(OnBasketCompletedEvent e) => OneShot(SoundFX.Packed, 1);
        private void OnLevelWon(OnLevelWonEvent e) => PlaySFX(SoundFX.Win);
        private void OnLevelJammed(OnLevelJammedEvent e) => PlaySFX(SoundFX.Fail);

        /// <summary>Dao chạm thảm (hết 0.12s đặt dao).</summary>
        public void PlayPlace() => OneShot(SoundFX.Place, TrimVolume);

        /// <summary>Motor dao: held = đang giữ, cutting = vừa cắt, allowed = gameplay không bị chặn. Gọi mỗi frame.</summary>
        public void Motor(bool held, bool cutting, bool allowed)
        {
            float target = allowed ? (cutting ? .05f : held ? .012f : 0) * TrimVolume * MotorGain : 0;
            motorSource.volume = Mathf.Lerp(motorSource.volume, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .05f));
        }

        public void PlayButton() => PlaySFX(SoundFX.UI_Button);

        #region MUSIC
        public void PlayMusic(string clipName, bool loop = true)
        {
            var clip = Clip(MusicFolder + clipName);
            if (clip == null || (musicSource.clip == clip && musicSource.isPlaying)) return;
            musicSource.clip = clip; musicSource.loop = loop; musicSource.Play();
        }
        public void StopMusic() => musicSource.Stop();
        #endregion

        #region SFX
        /// <summary>Phát chồng qua pool AudioSource; cùng clip cách nhau &lt; 0.1s thì bỏ qua. Mặc định Minor như game gốc.</summary>
        public void PlaySFX(string clipName, SFXType type = SFXType.Minor, float pitch = 1)
        {
            float now = Time.unscaledTime;
            if (lastSfxTime.TryGetValue(clipName, out float last) && now - last < SfxDuplicateDelay) return;
            var clip = Clip(SfxFolder + clipName);
            if (clip == null) return;
            lastSfxTime[clipName] = now;
            var s = sfxPool[nextSfx++ % sfxPool.Count];
            s.Stop(); s.clip = clip; s.volume = Volume(type); s.pitch = pitch; s.Play();
        }

        private static float Volume(SFXType type)
        {
            switch (type)
            {
                case SFXType.Important: return 1f;
                case SFXType.Normal: return .7f;
                case SFXType.Minor: return .2f;
                default: return .1f;
            }
        }
        #endregion

        private void OneShot(string clipName, float volume)
        {
            var clip = Clip(SfxFolder + clipName);
            if (clip != null) trimSource.PlayOneShot(clip, volume);
        }

        private AudioClip Clip(string path)
        {
            if (clips.TryGetValue(path, out var clip)) return clip;
            clip = RugPlayableAssets.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning("[Sound] Missing clip " + path + " (chạy lại Rug Playable/Bake Assets Into Current Scene)");
            clips[path] = clip;
            return clip;
        }

        private AudioSource Source(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0;
            return s;
        }
    }
}
