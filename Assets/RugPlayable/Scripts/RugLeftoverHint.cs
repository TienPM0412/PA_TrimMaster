using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    /// <summary>
    /// Gợi ý cục len sót: thảm chỉ còn ≤ <see cref="MaxCells"/> ô chưa cắt (khó nhìn ra) và người chơi chưa cắt được ô nào
    /// trong <see cref="IdleSeconds"/> giây (tính theo thời gian simulation, nên popup / tạm dừng không tính) thì hiện vòng sáng
    /// nhấp nháy + gợn sóng lan ra quanh các ô đó. Cắt được ô nào thì ẩn và đếm lại.
    /// Chỉ chỉ ô cắt được ngay (không bị kẹp); ống đầy (không cắt được) thì không gợi ý.
    /// Vẽ trong marksRoot (toạ độ thảm, trên lớp len) nên co giãn cùng tấm thảm.
    /// Port nguyên từ game gốc (View/RugLeftoverHint.cs); texture sinh lúc chạy bằng SetPixels32 (Luna hỗ trợ).
    /// </summary>
    public sealed class RugLeftoverHint
    {
        public const int MaxCells = 2;
        public const float IdleSeconds = 4f;
        // Đường kính vòng so với cạnh lớn của ô (tối thiểu MinSize đơn vị thảm), chu kỳ nhấp nháy / gợn sóng (giây).
        private const float SizeFactor = 3f, MinSize = 130f, PulsePeriod = .9f, RipplePeriod = 1.4f;
        private static readonly Color RingColor = new Color(1f, .58f, .1f, 1f);

        private sealed class Marker { public RectTransform root; public RawImage glow, ring, ripple; }

        private readonly RectTransform parent;
        private readonly List<Marker> markers = new List<Marker>();
        private readonly List<Cell> targets = new List<Cell>();
        private RugSimulation simulation;
        private int lastTrimmed = -1;
        private float idleSince;
        private bool showing;
        private float shownAt;
        private static Texture2D glowTexture, ringTexture;

        public RugLeftoverHint(RectTransform parent) { this.parent = parent; }

        /// <param name="allowed">False khi đang có intro / tutorial / popup: ẩn và không đếm.</param>
        public void Update(RugSimulation model, bool allowed)
        {
            if (model != simulation) { simulation = model; lastTrimmed = -1; Hide(); }
            if (simulation == null) return;
            if (simulation.TrimmedCount != lastTrimmed) { lastTrimmed = simulation.TrimmedCount; idleSince = simulation.Elapsed; Hide(); }
            bool want = allowed && simulation.State == PlayState.Playing && !simulation.Full &&
                        simulation.Cells.Length - simulation.TrimmedCount <= MaxCells &&
                        simulation.Elapsed - idleSince >= IdleSeconds && CollectTargets();
            if (!want) { Hide(); return; }
            if (!showing) Show();
            Animate(Time.unscaledTime - shownAt);
        }

        private bool CollectTargets()
        {
            targets.Clear();
            foreach (var c in simulation.Cells)
                if (!c.Trimmed && !simulation.Blocked(c)) targets.Add(c);
            return targets.Count > 0;
        }

        private void Show()
        {
            showing = true; shownAt = Time.unscaledTime;
            var def = simulation.Definition;
            float cw = Constants.RugRect.width / def.cols, ch = Constants.RugRect.height / def.rows;
            float size = Mathf.Max(MinSize, Mathf.Max(cw, ch) * SizeFactor);
            for (int i = 0; i < targets.Count; i++)
            {
                var m = MarkerAt(i);
                if (m == null) return;
                var c = targets[i];
                var center = new Vector2(Constants.RugRect.x + (c.X + .5f) * cw, Constants.RugRect.y + (c.Y + .5f) * ch);
                m.root.anchoredPosition = new Vector2(center.x, -center.y);
                m.root.sizeDelta = new Vector2(size, size);
                m.root.SetAsLastSibling();
                m.root.gameObject.SetActive(true);
            }
            for (int i = targets.Count; i < markers.Count; i++)
                if (markers[i].root != null) markers[i].root.gameObject.SetActive(false);
        }

        private void Hide()
        {
            if (!showing) return;
            showing = false;
            foreach (var m in markers) if (m.root != null) m.root.gameObject.SetActive(false);
        }

        private void Animate(float t)
        {
            float pulse = .5f + .5f * Mathf.Sin(t / PulsePeriod * Mathf.PI * 2);
            float ripple = Mathf.Repeat(t, RipplePeriod) / RipplePeriod;
            // Hiện dần trong 0.3s đầu.
            float fadeIn = Mathf.Clamp01(t / .3f);
            foreach (var m in markers)
            {
                if (m.root == null || !m.root.gameObject.activeSelf) continue;
                m.glow.color = new Color(RingColor.r, RingColor.g, RingColor.b, (.35f + .35f * pulse) * fadeIn);
                m.ring.color = new Color(RingColor.r, RingColor.g, RingColor.b, (.65f + .35f * pulse) * fadeIn);
                m.ring.rectTransform.localScale = Vector3.one * (.85f + .12f * pulse);
                m.ripple.color = new Color(RingColor.r, RingColor.g, RingColor.b, (1 - ripple) * .8f * fadeIn);
                m.ripple.rectTransform.localScale = Vector3.one * (.8f + .7f * ripple);
            }
        }

        private Marker MarkerAt(int i)
        {
            while (markers.Count <= i) markers.Add(new Marker());
            var m = markers[i];
            // marksRoot bị xoá con mỗi lần đổi level: dựng lại khi cần.
            if (m.root != null) return m;
            if (parent == null) return null;
            m.root = RugOriginalView.Rect(parent, "Leftover pile hint", 0, 0, 1, 1);
            m.root.pivot = new Vector2(.5f, .5f);
            m.glow = Layer(m.root, "Glow", GlowTexture());
            m.ripple = Layer(m.root, "Ripple", RingTexture());
            m.ring = Layer(m.root, "Ring", RingTexture());
            return m;
        }

        private static RawImage Layer(RectTransform root, string name, Texture texture)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(root, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var image = r.gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false;
            return image;
        }

        // Quầng sáng mềm (đậm ở giữa, mờ dần ra mép) và vòng tròn viền mềm, sinh một lần.
        private static Texture2D GlowTexture()
        {
            if (glowTexture != null) return glowTexture;
            return glowTexture = Radial("Leftover hint glow", d => Mathf.Pow(Mathf.Clamp01(1 - d), 2f));
        }

        private static Texture2D RingTexture()
        {
            if (ringTexture != null) return ringTexture;
            return ringTexture = Radial("Leftover hint ring", d => Mathf.Clamp01(1 - Mathf.Abs(d - .8f) / .13f));
        }

        private static Texture2D Radial(string name, System.Func<float, float> alpha)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                    float a = alpha(Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }
    }
}
