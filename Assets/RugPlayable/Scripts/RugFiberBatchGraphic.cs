using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // One UI mesh per depth layer, rather than one CanvasRenderer/Transform per puff.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugFiberBatchGraphic : MaskableGraphic
    {
        public Texture2D atlas;
        private struct Quad
        {
            public Vector2 position, halfSize;
            public Rect uv;
            public float cos, sin;
        }
        private readonly List<Quad> quads = new List<Quad>(192);
        private int previousCount;
        public int QuadCount { get { return quads.Count; } }
        public override Texture mainTexture { get { return atlas != null ? atlas : Texture2D.whiteTexture; } }
        public void BeginFrame() { previousCount = quads.Count; quads.Clear(); }
        public void Add(Vector2 position, Vector2 size, Rect uv, float radians)
        {
            quads.Add(new Quad { position = new Vector2(position.x, -position.y), halfSize = size * 0.5f,
                uv = uv, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians) });
        }
        public void EndFrame() { if (previousCount > 0 || quads.Count > 0) SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < quads.Count; i++)
            {
                var q = quads[i];
                int start = vh.currentVertCount;
                AddVertex(vh, q, -q.halfSize.x, -q.halfSize.y, q.uv.xMin, q.uv.yMin);
                AddVertex(vh, q, -q.halfSize.x, q.halfSize.y, q.uv.xMin, q.uv.yMax);
                AddVertex(vh, q, q.halfSize.x, q.halfSize.y, q.uv.xMax, q.uv.yMax);
                AddVertex(vh, q, q.halfSize.x, -q.halfSize.y, q.uv.xMax, q.uv.yMin);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
        private void AddVertex(VertexHelper vh, Quad q, float x, float y, float u, float v)
        {
            // Match the old clockwise RectTransform rotation, in top-left gameplay coordinates.
            vh.AddVert(new Vector3(q.position.x + x * q.cos + y * q.sin,
                q.position.y - x * q.sin + y * q.cos, 0), color, new Vector2(u, v));
        }
    }
}
