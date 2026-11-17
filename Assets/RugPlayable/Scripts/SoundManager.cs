using System.Collections.Generic;
using UnityEngine;

namespace RugMaster
{
    /// <summary>Tên clip trong Resources/RugMaster/Audio (bake vào scene qua RugPlayableAssets). Giống SoundFX của game gốc.</summary>
    public static class SoundFX
    {
        public const string MusicInGame = "BGM_INGAME";
        public const string UI_Button = "tap_button";
        public const string Win = "win";
        public const string Fail = "fail";

        // ---- Theo doc Sound (GD/Soundfx) của game gốc ----
        /// <summary>#1 Một cục bông rơi xuống giỏ.</summary>
        public const string Place = "place";
        /// <summary>#2 Hiện ra bức tranh (thảm loé sáng lúc thắng).</summary>
        public const string Ribbon = "ribbon";
        /// <summary>#3-9 Hoàn thành giỏ: pack_combo0..6, giỏ sau xong trong 5s thì lên nấc tiếp (tối đa 6), quá 5s về 0.</summary>
        public const string PackCombo = "pack_combo";
        public const int PackComboMax = 6;
        public const float PackComboWindow = 5f;
        /// <summary>#10 Cuộn tranh lăn ra (intro level).</summary>
        public const string RollUp = "roll_up";
        /// <summary>#12 Cắt 1 cục bông trên tranh: pitch ngẫu nhiên 1 trong 3 mốc (trầm / base / cao).</summary>
        public const string Shear = "shear";
        public static readonly float[] ShearPitches = { .9f, 1f, 1.12f };
        /// <summary>#13 Cùng lúc hoàn thành giỏ (chạy cùng pack_combo).</summary>
        public const string Pop = "pop";
        /// <summary>#14 Cục bông được hút vào ống: mỗi cục 1 tiếng, cách nhau tối thiểu 0.1s.</summary>
        public const string Land = "land";
        public const float LandInterval = .1f;

        // Game gốc tổng hợp 2 tiếng này lúc chạy (RugTrimAudio, AudioClip.Create) - Luna không hỗ trợ
        // AudioClip.Create/SetData nên đã render sẵn ra WAV với đúng công thức đó.
        /// <summary>Dao chạm thảm (RugTrimAudio.Place).</summary>
        public const string TrimmerContact = "trim_place";
        /// <summary>Motor dao 96/192 Hz loop (RugTrimAudio.Motor).</summary>
        public const string MotorLoop = "trimmer_motor_loop";
    }

    /// <summary>
    /// Âm thanh scene GamePlay, đồng bộ với SoundManager + RugTrimAudio của game gốc:
    /// - Nhạc nền BGM_INGAME loop (volume 0.6), dừng khi thắng.
    /// - Cắt (shear, pitch ngẫu nhiên 3 mốc, 1 tiếng/frame), bông vào ống (land, xếp hàng ≥ 0.1s, tối đa 8 chờ),
    ///   bông rơi vào giỏ (place, gộp trong 0.1s), xong giỏ (pack_combo0..6 + pop), intro (roll_up), thắng (ribbon + win).
    /// - Dao chạm thảm + motor dao loop (bản WAV của synth RugTrimAudio).
    /// Âm lượng theo SFXType (PA to hơn game gốc, xem MusicVolume): Important 1 / Normal 1 / Minor 0.6 / Small 0.3; nhạc nền 0.35.
    /// Playable không có nút bật/tắt tiếng nên luôn bật; không có rung (HapticManager) trên web.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoundManager : Singleton<SoundManager>
    {
        public enum SFXType { Important, Normal, Minor, Small }
        private const string MusicFolder = "RugMaster/Audio/Music/", SfxFolder = "RugMaster/Audio/SFX/";
        // Âm lượng PA to hơn game gốc (gốc: nhạc 0.6, dao 0.8, SFX Important 1 / Normal 0.7 / Minor 0.2 / Small 0.1):
        // Luna (PlayCanvas) kẹp volume mỗi tiếng ở tối đa 1 nên không thể "vượt 1" để to hơn; thay vào đó đẩy SFX lên gần mức
        // tối đa và hạ nhạc nền để tiếng hiệu ứng nổi rõ trên loa điện thoại.
        private const float MusicVolume = 0.35f, TrimVolume = 1f;
        private const float SfxDuplicateDelay = .1f;
        // WAV motor lưu ở 1/2 biên độ (bản gốc vượt 1.0), nên volume nhân 2 để ra đúng độ lớn gốc.
        private const float MotorGain = 2f;
        // Bông vào ống dồn dập (cắt nhanh) thì chỉ giữ tối đa chừng này tiếng chờ.
        private const int MaxPendingLand = 8;
        private const int PoolSize = 8, MaxPoolSize = 16;

        private AudioSource musicSource, trimSource, motorSource;
        private readonly List<AudioSource> sfxPool = new List<AudioSource>();
        private readonly List<float> sfxStartedAt = new List<float>();
        private readonly List<bool> sfxImportant = new List<bool>();
        private int nextSfx;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastCoalescedTime = new Dictionary<string, float>();

        private int lastShearFrame = -1, lastShearPitch = -1;
        private int pendingLand;
        private float lastLandTime = -100f;
        private int packCombo = -1;
        private float lastPackTime = -100f;

        private void Awake()
        {
            // Scene GamePlay không có AudioListener (camera dựng bằng code chỉ có Camera) -> trong Editor không nghe thấy gì.
            // Luna (web) không hỗ trợ AddComponent<AudioListener> (ném NotImplemented) và cũng không cần listener để phát tiếng:
            // nếu không bắt lỗi, Awake dừng giữa chừng -> musicSource null -> GameManager.Start lỗi, không load level, không cắt được.
            if (FindObjectOfType<AudioListener>() == null)
            {
                try
                {
                    var cam = Camera.main;
                    (cam != null ? cam.gameObject : gameObject).AddComponent<AudioListener>();
                }
                catch (System.Exception e) { Debug.LogWarning("[Sound] AudioListener: " + e.Message); }
            }
            musicSource = Source(true); musicSource.volume = MusicVolume;
            trimSource = Source(false);
            motorSource = Source(true); motorSource.volume = 0;
            for (int i = 0; i < PoolSize; i++) { sfxPool.Add(Source(false)); sfxStartedAt.Add(0); sfxImportant.Add(false); }
            motorSource.clip = Clip(SoundFX.MotorLoop, false);
            if (motorSource.clip != null) motorSource.Play();
        }

        private void OnEnable()
        {
            EventManager.AddListener<OnTrimEvent>(OnTrim);
            EventManager.AddListener<OnFiberLandedEvent>(OnFiberLanded);
            EventManager.AddListener<OnFiberEnterChannelEvent>(OnFiberEnterChannel);
            EventManager.AddListener<OnLevelLoadedEvent>(OnLevelLoaded);
            EventManager.AddListener<OnBasketCompletedEvent>(OnBasketCompleted);
            EventManager.AddListener<OnLevelWonEvent>(OnLevelWon);
            EventManager.AddListener<OnLevelJammedEvent>(OnLevelJammed);
        }

        private void OnDisable()
        {
            EventManager.RemoveListener<OnTrimEvent>(OnTrim);
            EventManager.RemoveListener<OnFiberLandedEvent>(OnFiberLanded);
            EventManager.RemoveListener<OnFiberEnterChannelEvent>(OnFiberEnterChannel);
            EventManager.RemoveListener<OnLevelLoadedEvent>(OnLevelLoaded);
            EventManager.RemoveListener<OnBasketCompletedEvent>(OnBasketCompleted);
            EventManager.RemoveListener<OnLevelWonEvent>(OnLevelWon);
            EventManager.RemoveListener<OnLevelJammedEvent>(OnLevelJammed);
        }

        private void OnLevelLoaded(OnLevelLoadedEvent e)
        {
            PlayMusic(SoundFX.MusicInGame);
            pendingLand = 0;
            packCombo = -1;
            lastPackTime = -100f;
        }

        // #12 shear: pitch ngẫu nhiên trong 3 mốc (không lặp mốc liền trước);
        // một nét cắt cắt nhiều ô cùng frame thì chỉ phát 1 tiếng để không bị cộng dồn to/rè.
        private void OnTrim(OnTrimEvent e)
        {
            if (Time.frameCount == lastShearFrame) return;
            lastShearFrame = Time.frameCount;
            int count = SoundFX.ShearPitches.Length;
            int pick = Random.Range(0, count);
            if (pick == lastShearPitch) pick = (pick + 1 + Random.Range(0, count - 1)) % count;
            lastShearPitch = pick;
            PlaySFX(SoundFX.Shear, SFXType.Normal, SoundFX.ShearPitches[pick]);
        }

        // #1 place: mỗi cục bông rơi xuống giỏ một tiếng; gộp các lượt trong 0.1s để tránh rè.
        private void OnFiberLanded(OnFiberLandedEvent e) => PlaySFXOnce(SoundFX.Place, SFXType.Normal);

        // #14 land: cục bông được hút vào ống; mỗi cục 1 tiếng, xếp hàng cách nhau >= 0.1s (Update).
        private void OnFiberEnterChannel(OnFiberEnterChannelEvent e)
        {
            if (pendingLand < MaxPendingLand) pendingLand++;
        }

        private void Update()
        {
            UpdateMusicDuck();
            if (pendingLand <= 0) return;
            float now = Time.unscaledTime;
            if (now - lastLandTime < SoundFX.LandInterval) return;
            lastLandTime = now;
            pendingLand--;
            PlaySFX(SoundFX.Land, SFXType.Normal);
        }

        // #3-9 pack_combo + #13 pop: giỏ xong trong 5s kể từ giỏ trước thì lên nấc combo (tối đa 6), quá 5s về 0.
        // Tiếng xong giỏ to nhất có thể: Luna kẹp volume mỗi nguồn ở 1.0, nên phát chồng BasketLayers bản cùng lúc
        // (các nguồn cộng biên độ -> ~+6dB với 2 bản) và tạm hạ nhạc nền (ducking) trong BasketDuckSeconds.
        private const int BasketLayers = 2;
        private const float BasketDuckSeconds = 0.9f, DuckedMusicVolume = 0.12f;
        private float duckUntil = -100f;
        private void OnBasketCompleted(OnBasketCompletedEvent e)
        {
            float now = Time.unscaledTime;
            packCombo = packCombo >= 0 && now - lastPackTime <= SoundFX.PackComboWindow
                ? Mathf.Min(packCombo + 1, SoundFX.PackComboMax)
                : 0;
            lastPackTime = now;
            for (int i = 0; i < BasketLayers; i++)
            {
                PlaySFX(SoundFX.PackCombo + packCombo, SFXType.Important);
                PlaySFX(SoundFX.Pop, SFXType.Important);
            }
            duckUntil = now + BasketDuckSeconds;
        }

        // Nhạc nền về lại mức thường sau khi hết ducking (nhanh xuống, lên dần cho êm).
        private void UpdateMusicDuck()
        {
            float target = Time.unscaledTime < duckUntil ? DuckedMusicVolume : MusicVolume;
            float speed = target < musicSource.volume ? 25f : 3f;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, speed * Time.unscaledDeltaTime);
        }

        // Game gốc phát win ở mức Minor (0.2) vì ngay sau đó còn popup Win + pháo hoa có tiếng riêng.
        // Playable không có popup/pháo hoa đó nên win là tiếng thắng duy nhất -> phát Important (1.0) cho nghe rõ.
        private void OnLevelWon(OnLevelWonEvent e)
        {
            StopMusic();
            PlaySFX(SoundFX.Win, SFXType.Important);
        }

        // Game gốc dừng nhạc vì mở popup Out of Space; playable tự hồi sinh và chơi tiếp nên giữ nhạc.
        private void OnLevelJammed(OnLevelJammedEvent e) => PlaySFX(SoundFX.Fail);

        /// <summary>Dao chạm thảm (hết 0.12s đặt dao), như RugTrimAudio.Place.</summary>
        public void PlayPlace()
        {
            var clip = Clip(SoundFX.TrimmerContact, false);
            if (clip != null) trimSource.PlayOneShot(clip, TrimVolume);
        }

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
            var clip = Clip(clipName, true);
            if (clip == null || (musicSource.clip == clip && musicSource.isPlaying)) return;
            musicSource.clip = clip; musicSource.loop = loop; musicSource.Play();
        }
        public void StopMusic() => musicSource.Stop();
        #endregion

        #region SFX
        /// <summary>Phát ngay qua pool AudioSource (chồng lên các tiếng đang phát). Mặc định Minor như game gốc.</summary>
        public void PlaySFX(string clipName, SFXType type = SFXType.Minor, float pitch = 1)
        {
            var clip = Clip(clipName, false);
            if (clip == null) return;
            int index = PickSource(type);
            var s = sfxPool[index];
            s.Stop(); s.clip = clip; s.volume = Volume(type); s.pitch = pitch; s.Play();
            sfxStartedAt[index] = Time.unscaledTime;
            sfxImportant[index] = type == SFXType.Important;
        }

        // Như pool của game gốc: lấy nguồn đang rảnh, hết thì thêm nguồn mới (tới MaxPoolSize).
        // Trước đây pool xoay vòng 8 nguồn: lúc đang cạo, tiếng shear phát mỗi frame nên chỉ ~8 frame (~0.13s) sau
        // nguồn đang phát pack_combo / pop đã bị Stop() để phát shear -> tiếng xong giỏ bị cắt cụt, nghe rất bé.
        // Pool đầy thì cướp nguồn cũ nhất không phải Important; tiếng Important chỉ bị cướp khi tất cả đều Important.
        private int PickSource(SFXType type)
        {
            for (int i = 0; i < sfxPool.Count; i++)
            {
                int k = (nextSfx + i) % sfxPool.Count;
                if (!sfxPool[k].isPlaying) { nextSfx = k + 1; return k; }
            }
            if (sfxPool.Count < MaxPoolSize)
            {
                sfxPool.Add(Source(false)); sfxStartedAt.Add(0); sfxImportant.Add(false);
                return sfxPool.Count - 1;
            }
            int oldest = -1;
            for (int i = 0; i < sfxPool.Count; i++)
                if (!sfxImportant[i] && (oldest < 0 || sfxStartedAt[i] < sfxStartedAt[oldest])) oldest = i;
            if (oldest >= 0) return oldest;
            // Tất cả đều đang phát tiếng Important: đành dùng nguồn cũ nhất.
            for (int i = 0; i < sfxPool.Count; i++)
                if (oldest < 0 || sfxStartedAt[i] < sfxStartedAt[oldest]) oldest = i;
            return oldest;
        }

        /// <summary>Gộp các lần gọi cùng clip trong 0.1s thành 1 tiếng.</summary>
        public void PlaySFXOnce(string clipName, SFXType type = SFXType.Normal)
        {
            float now = Time.unscaledTime;
            if (lastCoalescedTime.TryGetValue(clipName, out float last) && now - last < SfxDuplicateDelay) return;
            lastCoalescedTime[clipName] = now;
            PlaySFX(clipName, type);
        }

        private static float Volume(SFXType type)
        {
            switch (type)
            {
                case SFXType.Important: return 1f;
                case SFXType.Normal: return 1f;
                case SFXType.Minor: return .6f;
                default: return .3f;
            }
        }
        #endregion

        private AudioClip Clip(string name, bool music)
        {
            string path = (music ? MusicFolder : SfxFolder) + name;
            if (clips.TryGetValue(path, out var clip)) return clip;
            clip = RugPlayableAssets.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning("[Sound] Missing clip " + path + " (chạy lại Rug Playable/Bake Assets Into Current Scene)");
            clips[path] = clip;
            return clip;
        }

        private AudioSource Source(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop;
            return s;
        }
    }
}
