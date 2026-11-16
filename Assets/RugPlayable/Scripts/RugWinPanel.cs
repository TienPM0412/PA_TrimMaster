using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // Panel thắng của playable, nằm sẵn trong scene (tắt), lúc thắng chỉ việc Show():
    // nền tối hơi trong suốt + logo "Trim Master" là nút, bấm vào thì mở store (PlayableAdHelper.InstallFullGame).
    // Cả panel fade in qua CanvasGroup, logo bật lên rồi nhịp nhẹ để gợi ý bấm.
    // Dựng/cập nhật trong scene bằng menu Rug Playable/Create Win Panel In Scene (hoặc Bake Assets).
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RugWinPanel : MonoBehaviour
    {
        public const string PanelName = "Win panel";
        public const string TitleKey = "RugMaster/PA/title 2";

        public const string PlayButtonName = "Play now button";
        public const string ButtonFrameKey = "RugMaster/PA/btn_frame_gold", ButtonFillKey = "RugMaster/PA/btn_green";

        public CanvasGroup group;
        public RawImage background;
        public RectTransform title;
        public Button installButton;
        // Nút "PLAY NOW" dưới logo, cũng mở store.
        public RectTransform playRoot;
        public Button playButton;
        [Tooltip("Nút Play now bật lên trễ hơn logo bao nhiêu giây.")] public float playButtonDelay = 0.18f;
        // Phiên bản layout nút do code dựng: tăng PlayButtonLayout khi đổi thiết kế để Bake dựng lại nút trong scene.
        public const int PlayButtonLayout = 3;
        [HideInInspector] public int playButtonLayout;
        [Tooltip("Thời gian fade in/out cả panel (giây).")] public float fadeDuration = 0.4f;
        [Tooltip("Thời gian logo bật lên (giây), bắt đầu cùng lúc fade.")] public float popDuration = 0.45f;
        [Tooltip("Biên độ nhịp của logo sau khi bật lên.")] public float pulseAmount = 0.045f;
        public float pulsePeriod = 1.1f;

        private float shownAt;
        private bool hiding, endedReported;

        public bool IsShown => gameObject.activeSelf && !hiding;

        private void Awake()
        {
            // Nền dùng texture trắng mặc định lúc chạy (không lưu built-in texture vào scene).
            if (background != null && background.texture == null) background.texture = Texture2D.whiteTexture;
            if (installButton != null) installButton.onClick.AddListener(OnInstallClicked);
            if (playButton != null) playButton.onClick.AddListener(OnInstallClicked);
        }

        public void Show()
        {
            hiding = false;
            shownAt = Time.unscaledTime;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            group.alpha = 0; group.interactable = group.blocksRaycasts = true;
            if (title != null) title.localScale = Vector3.zero;
            if (playRoot != null) playRoot.localScale = Vector3.zero;
            if (!endedReported) { endedReported = true; PlayableAdHelper.GameEnded(); }
        }

        public void Hide(bool instant = false)
        {
            if (instant || !gameObject.activeSelf) { hiding = false; gameObject.SetActive(false); return; }
            if (hiding) return;
            hiding = true; shownAt = Time.unscaledTime;
            group.interactable = group.blocksRaycasts = false;
        }

        private void OnInstallClicked()
        {
            if (SoundManager.HasInstance) SoundManager.Ins.PlayButton();
            PlayableAdHelper.InstallFullGame();
        }

        private void Update()
        {
            float t = Time.unscaledTime - shownAt;
            float k = fadeDuration > 0 ? Mathf.Clamp01(t / fadeDuration) : 1;
            if (hiding)
            {
                group.alpha = 1 - k * k;
                if (k >= 1) { hiding = false; gameObject.SetActive(false); }
                return;
            }
            group.alpha = 1 - (1 - k) * (1 - k);
            if (title != null) title.localScale = Vector3.one * PopPulse(t);
            if (playRoot != null) playRoot.localScale = Vector3.one * PopPulse(t - playButtonDelay);
        }
        // Bật lên (OutBack) rồi nhịp nhẹ để gợi ý bấm. t < 0: chưa hiện.
        private float PopPulse(float t)
        {
            if (t <= 0) return 0;
            if (t < popDuration) return RugPilePose.OutBack(t / popDuration, 1.7f);
            return 1 + pulseAmount * Mathf.Sin((t - popDuration) * Mathf.PI * 2 / pulsePeriod);
        }

        // Dựng hierarchy panel (chạy được trong Editor để lưu vào scene). Gọi lại thì dựng mới từ đầu.
        public static RugWinPanel Build(Transform parent, Texture2D titleTexture)
        {
            var old = parent.Find(PanelName);
            if (old != null) DestroyImmediate(old.gameObject);
            var root = RugOriginalView.Rect(parent, PanelName, 0, 0, 1080, 1920);
            var panel = root.gameObject.AddComponent<RugWinPanel>();
            panel.group = root.GetComponent<CanvasGroup>();

            // Nền tối hơi trong suốt, phủ kín cả phần ngoài khung 1080x1920; chặn chạm xuống gameplay.
            panel.background = Image(root, "Background (dim)", new Rect(-1080, -1920, 3240, 5760), null);
            panel.background.color = new Color(0.08f, 0.05f, 0.03f, 0.72f);
            panel.background.raycastTarget = true;

            const float width = 860;
            var size = titleTexture != null ? new Vector2(width, width * titleTexture.height / titleTexture.width) : new Vector2(width, 510);
            var image = Image(root, "Title (install button)", new Rect(0, 0, size.x, size.y), titleTexture);
            panel.title = image.rectTransform;
            panel.title.pivot = new Vector2(0.5f, 0.5f);
            panel.title.anchoredPosition = new Vector2(540, -880);
            image.raycastTarget = true;
            panel.installButton = image.gameObject.AddComponent<Button>();
            panel.installButton.targetGraphic = image;
            panel.installButton.transition = Selectable.Transition.None;

            BuildPlayButton(panel);
            root.gameObject.SetActive(false);
            return panel;
        }

        // Nút "PLAY NOW": khung vàng (9-slice) + lõi xanh (9-slice) + chữ trắng có bóng, căn giữa ngay dưới logo.
        // Panel cũ trong scene chưa có nút thì RugOriginalView.EnsureWinPanel gọi riêng hàm này (giữ nguyên chỉnh sửa khác).
        public static void BuildPlayButton(RugWinPanel panel)
        {
            var old = panel.transform.Find(PlayButtonName);
            if (old != null) DestroyImmediate(old.gameObject);
            // Nút ngắn (hẹp ngang) và cao hơn.
            const float width = 520, height = 230, inset = 16;
            panel.playButtonLayout = PlayButtonLayout;
            float top = 880 + (panel.title != null ? panel.title.sizeDelta.y * 0.5f : 255) + 40;
            var root = RugOriginalView.Rect(panel.transform, PlayButtonName, 0, 0, width, height);
            root.pivot = new Vector2(0.5f, 0.5f); root.anchoredPosition = new Vector2(540, -(top + height * 0.5f));
            panel.playRoot = root;

            var frame = Sliced(root, "Frame", new Rect(0, 0, width, height), RugPlayableAssets.Load<Sprite>(ButtonFrameKey));
            frame.raycastTarget = true;
            Sliced(root, "Fill", new Rect(inset, inset, width - inset * 2, height - inset * 2), RugPlayableAssets.Load<Sprite>(ButtonFillKey));
            var font = RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-700");
            // Bóng chữ vẽ bằng một Text lệch xuống (Shadow/Outline của uGUI không chạy trên web).
            Label(root, "Label shadow", new Rect(0, 7, width, height - 4), font, new Color32(18, 92, 12, 255));
            Label(root, "Label", new Rect(0, 0, width, height - 4), font, Color.white);

            panel.playButton = root.gameObject.AddComponent<Button>();
            panel.playButton.targetGraphic = frame;
            panel.playButton.transition = Selectable.Transition.None;
            root.SetAsLastSibling();
        }

        private static UnityEngine.UI.Image Sliced(Transform parent, string name, Rect r, Sprite sprite)
        {
            var image = RugOriginalView.Rect(parent, name, r.x, r.y, r.width, r.height).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.type = UnityEngine.UI.Image.Type.Sliced; image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1; image.raycastTarget = false;
            return image;
        }

        private static void Label(Transform parent, string name, Rect r, Font font, Color color)
        {
            var text = RugOriginalView.Rect(parent, name, r.x, r.y, r.width, r.height).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = 88; text.text = "PLAY NOW"; text.color = color;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static RawImage Image(Transform parent, string name, Rect r, Texture texture)
        {
            var image = RugOriginalView.Rect(parent, name, r.x, r.y, r.width, r.height).gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false; return image;
        }
    }
}
