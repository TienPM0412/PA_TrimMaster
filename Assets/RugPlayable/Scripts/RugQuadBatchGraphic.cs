using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    /// <summary>
    /// Một mesh uGUI chứa nhiều quad cùng texture/material (hạt vụn khi cạo, vệt sáng ô vừa cạo...), thay cho mỗi hạt
    /// một GameObject + RawImage. Trên Luna (web) mỗi phần tử UI riêng tốn CPU (đồng bộ transform, SetActive, rebatch),
    /// nên gom lại một draw call / một lần dựng mesh mỗi frame.
    /// Toạ độ quad: gốc ở góc trên-trái của RectTransform (pivot (0,1)), y hướng xuống như anchoredPosition (x,-y).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugQuadBatchGraphic : MaskableGraphic
    {
        public Texture texture;
        private struct Quad
        {
            public Vector2 center, half;
            public float cos, sin, scaleX;
            public Rect uv;
            public Color32 color;
        }
        private readonly List<Quad> quads = new List<Quad>(128);
        private int previousCount;
        public int QuadCount { get { return quads.Count; } }
        public override Texture mainTexture { get { return texture != null ? texture : Texture2D.whiteTexture; } }

        public void BeginFrame() { previousCount = quads.Count; quads.Clear(); }

        /// <param name="position">Tâm quad (x phải, y xuống).</param>
        /// <param name="angle">Góc quay (radian, ngược chiều kim đồng hồ trên màn hình như localEulerAngles.z).</param>
        /// <param name="scaleX">Hệ số co ngang trước khi quay (âm = lật), như localScale.x.</param>
        public void Add(Vector2 position, Vector2 size, float angle, float scaleX, Rect uv, Color color)
        {
            if (color.a <= 0 || size.x == 0 || size.y == 0) return;
            quads.Add(new Quad { center = new Vector2(position.x, -position.y), half = size * 0.5f,
                cos = Mathf.Cos(angle), sin = Mathf.Sin(angle), scaleX = scaleX, uv = uv, color = color });
        }

        public void EndFrame() { if (previousCount > 0 || quads.Count > 0) SetVerticesDirty(); }

        public void Clear() { if (quads.Count > 0) { quads.Clear(); SetVerticesDirty(); } previousCount = 0; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < quads.Count; i++)
            {
                var q = quads[i];
                int start = vh.currentVertCount;
                Corner(vh, q, -q.half.x, -q.half.y, q.uv.xMin, q.uv.yMin);
                Corner(vh, q, -q.half.x, q.half.y, q.uv.xMin, q.uv.yMax);
                Corner(vh, q, q.half.x, q.half.y, q.uv.xMax, q.uv.yMax);
                Corner(vh, q, q.half.x, -q.half.y, q.uv.xMax, q.uv.yMin);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }

        private static void Corner(VertexHelper vh, Quad q, float x, float y, float u, float v)
        {
            x *= q.scaleX;
            vh.AddVert(new Vector3(q.center.x + x * q.cos - y * q.sin, q.center.y + x * q.sin + y * q.cos, 0), q.color, new Vector2(u, v));
        }
    }
}
