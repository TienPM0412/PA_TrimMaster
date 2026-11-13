using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    /// <summary>
    /// Vẽ obstacle trên thảm lúc chạy từ các ảnh kit (thay cho 663 ảnh Marks/mark-* dựng sẵn theo từng level gốc):
    /// - Kẹp (clip): khung nét đứt màu kem quanh các ô bị kẹp + kit-clip cắm trên mép trên khung + kit-counter có số kẹp còn lại.
    /// - Ruy băng (ribbon): khung nét đứt màu vàng + kit-tab ở giữa.
    /// Khung tính từ đúng các ô của simulation (Cell.Clip / Ribbon.Cells) nên khớp mọi cỡ lưới (kể cả level tranh mới).
    /// Thông số đo lại từ ảnh gốc (đúng cho cả 663 ảnh): xem các hằng bên dưới. Thay asset = thay file kit-*.png.
    /// Toạ độ: khung thiết kế 1080x1920, gốc trên-trái (giống marksRoot của RugOriginalView).
    /// </summary>
    public sealed class RugObstacleMarks
    {
        // Khung nét đứt: viền ngoài trùng hộp bao các ô; nét dày 7, đầu bo tròn, nét thấy được dài 28 / chu kỳ 34, bo góc 16.
        private const float StrokeWidth = 7f, DashPeriod = 34f, DashGap = 6f, CornerRadius = 16f;
        private static readonly Color32 ClipFrameColor = new Color32(251, 242, 220, 255);
        private static readonly Color32 RibbonFrameColor = new Color32(240, 188, 78, 255);
        // Vùng bị kẹp phủ tối nhẹ (lòng khung bo góc, dưới nét đứt); ruy băng không phủ.
        private static readonly Color32 ClipFillColor = new Color32(34, 34, 62, 45);

        // Kẹp: tỉ lệ 0.44 so với kit-clip, tâm ở w*(i+1)/(số kẹp+1), thấp hơn mép trên khung 6 (cắm qua mép); kẹp thứ lẻ xoay 30° (chiều kim đồng hồ).
        private const float PinScale = 0.44f, PinDrop = 6f, OddPinAngle = -30f;
        // Badge: kit-counter rộng 170 (tỉ lệ 170/297), tâm cách mép trên khung 130.3, căn giữa ngang.
        private const float BadgeWidth = 170f, BadgeCenterFromTop = 130.3f;
        // Số trên badge: Fredoka 700 cỡ 43 (chữ cao ~31), nâu đậm, ở giữa khoảng trống bên phải icon giỏ của kit-counter.
        // Đổi ảnh kit-counter có bố cục khác thì chỉnh CounterDigitCenter (tỉ lệ theo badge, gốc trên-trái).
        private static readonly Vector2 CounterDigitCenter = new Vector2(0.638f, 0.497f);
        private const int CounterFontSize = 43;
        private static readonly Color32 CounterDigitColor = new Color32(59, 42, 30, 255);
        // Ruy băng: kit-tab tỉ lệ 0.615 (89x110), chính giữa khung.
        private const float TabScale = 0.615f;

        private sealed class ClipGroup
        {
            public int index;
            public GameObject root;
            public readonly List<GameObject> pins = new List<GameObject>();
            public Text number;
            public int shown = -1;
        }

        private sealed class RibbonGroup
        {
            public int index;
            public GameObject root;
            public bool shown = true;
        }

        private readonly RectTransform root;
        private readonly List<ClipGroup> clips = new List<ClipGroup>();
        private readonly List<RibbonGroup> ribbons = new List<RibbonGroup>();
        private readonly List<Texture2D> frameTextures = new List<Texture2D>();
        private static Font counterFont;

        public RugObstacleMarks(RectTransform root) { this.root = root; }

        /// <summary>Dựng lại obstacle cho level đang chơi (gọi sau khi đã xoá con của root).</summary>
        public void Build(RugSimulation simulation)
        {
            Clear();
            if (simulation == null || root == null) return;
            var def = simulation.Definition;
            float cw = Constants.RugRect.width / def.cols, ch = Constants.RugRect.height / def.rows;

            for (int i = 0; i < simulation.Ribbons.Length; i++)
            {
                if (!CellBounds(simulation, simulation.Ribbons[i].Cells, cw, ch, out var frame)) continue;
                var g = Group("Ribbon " + i, frame);
                Frame(g, frame, RibbonFrameColor, null);
                var tab = Kit(g, "kit-tab", TabScale, new Vector2(frame.width * 0.5f, frame.height * 0.5f));
                tab.name = "Tab";
                ribbons.Add(new RibbonGroup { index = i, root = g.gameObject });
            }

            for (int i = 0; i < simulation.Clips.Length; i++)
            {
                var cells = new List<int>();
                foreach (var c in simulation.Cells) if (c.Clip == i) cells.Add(c.Index);
                if (!CellBounds(simulation, cells, cw, ch, out var frame)) continue;
                int max = def.clips != null && i < def.clips.Length ? Mathf.Max(1, def.clips[i].count) : Mathf.Max(1, simulation.Clips[i]);
                var g = Group("Clip " + i, frame);
                Frame(g, frame, ClipFrameColor, ClipFillColor);
                var group = new ClipGroup { index = i, root = g.gameObject };

                var badgeTex = RugOriginalAssets.TryTexture("kit-counter");
                float badgeScale = badgeTex != null ? BadgeWidth / badgeTex.width : 1f;
                var badge = Kit(g, "kit-counter", badgeScale, new Vector2(frame.width * 0.5f, BadgeCenterFromTop));
                badge.name = "Counter";
                group.number = Number(badge);

                // Kẹp vẽ sau khung + badge để đè lên mép trên khung.
                for (int p = 0; p < max; p++)
                {
                    var pin = Kit(g, "kit-clip", PinScale, new Vector2(frame.width * (p + 1) / (max + 1), PinDrop));
                    pin.name = "Pin " + p;
                    if (p % 2 == 1) pin.localEulerAngles = new Vector3(0, 0, OddPinAngle);
                    group.pins.Add(pin.gameObject);
                }
                clips.Add(group);
            }
            Refresh(simulation);
        }

        /// <summary>Cập nhật hiển thị theo trạng thái (gọi mỗi frame; chỉ đổi khi số kẹp / ruy băng thay đổi).</summary>
        public void Refresh(RugSimulation simulation)
        {
            if (simulation == null) return;
            foreach (var g in clips)
            {
                int count = g.index < simulation.Clips.Length ? simulation.Clips[g.index] : 0;
                if (count == g.shown) continue;
                g.shown = count;
                g.root.SetActive(count > 0);
                for (int p = 0; p < g.pins.Count; p++) g.pins[p].SetActive(p < count);
                if (g.number != null) g.number.text = count.ToString();
            }
            foreach (var g in ribbons)
            {
                bool show = g.index < simulation.Ribbons.Length && !simulation.Ribbons[g.index].Resolved;
                if (show == g.shown) continue;
                g.shown = show;
                g.root.SetActive(show);
            }
        }

        /// <summary>Huỷ texture khung đã sinh (con của root do RugOriginalView xoá).</summary>
        public void Clear()
        {
            clips.Clear();
            ribbons.Clear();
            foreach (var t in frameTextures) if (t != null) Object.Destroy(t);
            frameTextures.Clear();
        }

        // Hộp bao các ô (toạ độ thiết kế, gốc trên-trái).
        private static bool CellBounds(RugSimulation simulation, IList<int> cells, float cw, float ch, out Rect frame)
        {
            frame = default;
            if (cells == null || cells.Count == 0) return false;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (int index in cells)
            {
                var c = simulation.Cells[index];
                x0 = Mathf.Min(x0, c.X); x1 = Mathf.Max(x1, c.X);
                y0 = Mathf.Min(y0, c.Y); y1 = Mathf.Max(y1, c.Y);
            }
            var rug = Constants.RugRect;
            frame = Rect.MinMaxRect(rug.x + x0 * cw, rug.y + y0 * ch, rug.x + (x1 + 1) * cw, rug.y + (y1 + 1) * ch);
            return true;
        }

        private RectTransform Group(string name, Rect frame)
        {
            var g = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            g.SetParent(root, false);
            g.anchorMin = g.anchorMax = new Vector2(0, 1);
            g.pivot = new Vector2(0, 1);
            g.anchoredPosition = new Vector2(frame.x, -frame.y);
            g.sizeDelta = frame.size;
            return g;
        }

        // Ảnh kit đặt theo tâm (toạ độ trong nhóm, gốc trên-trái, y xuống).
        private static RectTransform Kit(RectTransform parent, string key, float scale, Vector2 center)
        {
            var r = new GameObject(key, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(center.x, -center.y);
            var image = r.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var tex = RugOriginalAssets.TryTexture(key);
            image.texture = tex;
            r.sizeDelta = tex != null ? new Vector2(tex.width, tex.height) * scale : Vector2.zero;
            return r;
        }

        private static Text Number(RectTransform badge)
        {
            var r = new GameObject("Count", typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(badge, false);
            r.anchorMin = r.anchorMax = new Vector2(CounterDigitCenter.x, 1 - CounterDigitCenter.y);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(80, 60);
            var label = r.gameObject.AddComponent<Text>();
            if (counterFont == null) counterFont = RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-700");
            label.font = counterFont != null ? counterFont : RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            label.fontSize = CounterFontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.color = CounterDigitColor;
            label.raycastTarget = false;
            return label;
        }

        private void Frame(RectTransform parent, Rect frame, Color32 color, Color32? fill)
        {
            var tex = DashedFrame(Mathf.CeilToInt(frame.width), Mathf.CeilToInt(frame.height), color, fill);
            frameTextures.Add(tex);
            var r = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(tex.width, tex.height);
            var image = r.gameObject.AddComponent<RawImage>();
            image.texture = tex;
            image.raycastTarget = false;
        }

        /// <summary>
        /// Raster khung chữ nhật bo góc nét đứt (khử răng cưa) vào texture w x h: tâm nét lùi vào StrokeWidth/2 so với viền,
        /// mỗi nét là một "viên thuốc" (đầu tròn). Chu kỳ được co giãn nhẹ để số nét chẵn khít quanh khung (không lệch chỗ nối).
        /// </summary>
        private static Texture2D DashedFrame(int w, int h, Color32 color, Color32? fill)
        {
            float half = StrokeWidth * 0.5f;
            float x0 = half, y0 = half, x1 = w - half, y1 = h - half;
            float r = Mathf.Max(0, Mathf.Min(CornerRadius - half, (x1 - x0) * 0.5f, (y1 - y0) * 0.5f));
            float top = x1 - x0 - 2 * r, side = y1 - y0 - 2 * r, arc = Mathf.PI * r * 0.5f;
            float length = 2 * (top + side) + 4 * arc;
            int dashes = Mathf.Max(1, Mathf.RoundToInt(length / DashPeriod));
            float period = length / dashes;
            float on = Mathf.Max(0, period - DashGap - StrokeWidth); // độ dài đường tâm của nét (chưa tính 2 đầu tròn)

            var pixels = new Color32[w * h];
            var clear = new Color32(color.r, color.g, color.b, 0);
            float hw = w * 0.5f, hh = h * 0.5f, outerR = Mathf.Min(CornerRadius, hw, hh);
            float ix0 = x0 + r, ix1 = x1 - r, iy0 = y0 + r, iy1 = y1 - r; // hình chữ nhật trong (tâm các cung góc)
            for (int py = 0; py < h; py++)
            {
                float y = py + 0.5f;          // py tính từ trên xuống
                int row = (h - 1 - py) * w;  // Texture2D: hàng 0 là hàng dưới cùng
                for (int px = 0; px < w; px++)
                {
                    float x = px + 0.5f;
                    // Điểm gần nhất trên đường tâm + vị trí dọc đường (s), bắt đầu từ đầu cạnh trên, đi theo chiều kim đồng hồ.
                    float cx = Mathf.Clamp(x, ix0, ix1), cy = Mathf.Clamp(y, iy0, iy1);
                    float dx = x - cx, dy = y - cy, dist, s;
                    if (dx == 0 && dy == 0)
                    {
                        // Trong lòng khung: lấy cạnh gần nhất.
                        float dt = y - y0, db = y1 - y, dl = x - x0, dr = x1 - x;
                        float m = Mathf.Min(Mathf.Min(dt, db), Mathf.Min(dl, dr));
                        dist = m;
                        if (m == dt) s = x - ix0;
                        else if (m == dr) s = top + arc + (y - iy0);
                        else if (m == db) s = 2 * top - (x - ix0) + side + 2 * arc;
                        else s = 2 * top + 3 * arc + side + (iy1 - y); // cạnh trái đi lên
                    }
                    else if (dx == 0)
                    {
                        dist = Mathf.Abs(Mathf.Abs(dy) - r);
                        s = dy < 0 ? x - ix0 : top + arc + side + arc + (ix1 - x);
                    }
                    else if (dy == 0)
                    {
                        dist = Mathf.Abs(Mathf.Abs(dx) - r);
                        s = dx > 0 ? top + arc + (y - iy0) : 2 * top + 3 * arc + side + (iy1 - y);
                    }
                    else
                    {
                        float len = Mathf.Sqrt(dx * dx + dy * dy);
                        dist = Mathf.Abs(len - r);
                        // Góc trong cung: 0 -> 1 theo chiều kim đồng hồ.
                        float a = Mathf.Atan2(dy, dx); // y xuống: phải = 0, dưới = +pi/2
                        float t;
                        if (dx > 0 && dy < 0) { t = (a + Mathf.PI * 0.5f) / (Mathf.PI * 0.5f); s = top + t * arc; }                         // trên-phải
                        else if (dx > 0) { t = a / (Mathf.PI * 0.5f); s = top + arc + side + t * arc; }                                    // dưới-phải
                        else if (dy > 0) { t = (a - Mathf.PI * 0.5f) / (Mathf.PI * 0.5f); s = 2 * top + 2 * arc + side + t * arc; }         // dưới-trái
                        else { t = (a + Mathf.PI) / (Mathf.PI * 0.5f); s = 2 * top + 3 * arc + 2 * side + t * arc; }                       // trên-trái
                    }
                    // Lớp phủ: lòng hình chữ nhật bo góc ngoài (khử răng cưa theo khoảng cách có dấu).
                    float fillA = 0;
                    if (fill.HasValue)
                    {
                        float qx = Mathf.Abs(x - hw) - (hw - outerR), qy = Mathf.Abs(y - hh) - (hh - outerR);
                        float sd = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - outerR;
                        fillA = Mathf.Clamp01(0.5f - sd) * fill.Value.a / 255f;
                    }
                    if (dist > half + 1f) { pixels[row + px] = Blend(color, 0, fill, fillA, clear); continue; }
                    // Khoảng cách tới "viên thuốc" của nét gần nhất (đoạn tâm [0, on] trong mỗi chu kỳ).
                    float phase = Mathf.Repeat(s, period);
                    float along = phase <= on ? 0 : Mathf.Min(phase - on, period - phase);
                    float d = Mathf.Sqrt(dist * dist + along * along);
                    float alpha = Mathf.Clamp01(half + 0.5f - d);
                    pixels[row + px] = Blend(color, alpha * color.a / 255f, fill, fillA, clear);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Dashed frame", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // Nét (alpha sa) đè lên lớp phủ (alpha fa), alpha thẳng.
        private static Color32 Blend(Color32 stroke, float sa, Color32? fill, float fa, Color32 clear)
        {
            if (!fill.HasValue || fa <= 0) return sa <= 0 ? clear : new Color32(stroke.r, stroke.g, stroke.b, (byte)Mathf.RoundToInt(sa * 255));
            var f = fill.Value;
            float a = sa + fa * (1 - sa);
            if (a <= 0) return clear;
            float k = fa * (1 - sa);
            return new Color32((byte)Mathf.RoundToInt((stroke.r * sa + f.r * k) / a), (byte)Mathf.RoundToInt((stroke.g * sa + f.g * k) / a),
                (byte)Mathf.RoundToInt((stroke.b * sa + f.b * k) / a), (byte)Mathf.RoundToInt(a * 255));
        }
    }
}
