using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // Clip fill geometry on the CPU: independent of RectMask2D support in the web engine.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugMeterFillGraphic : MaskableGraphic
    {
        public Texture2D fillTexture;
        public float amount;
        public float sourceWidth = 302, sourceHeight = 82;
        public override Texture mainTexture { get { return fillTexture != null ? fillTexture : Texture2D.whiteTexture; } }
        public float VisibleWidth { get { return rectTransform.rect.width * Mathf.Clamp01(amount); } }
        public void SetFill(float value, Texture2D texture)
        {
            amount = Mathf.Clamp01(value);
            if (fillTexture != texture) { fillTexture = texture; SetMaterialDirty(); }
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float cap = Mathf.Min(r.width * 0.5f, r.height * 0.5f);
            float uvCap = sourceHeight * 0.5f / sourceWidth;
            float right = r.xMin + r.width * Mathf.Clamp01(amount);
            Segment(vh, r, r.xMin, r.xMin + cap, 0, uvCap, right);
            Segment(vh, r, r.xMin + cap, r.xMax - cap, uvCap, 1 - uvCap, right);
            Segment(vh, r, r.xMax - cap, r.xMax, 1 - uvCap, 1, right);
        }
        private void Segment(VertexHelper vh, Rect r, float left, float end, float u0, float u1, float right)
        {
            if (right <= left || end <= left) return;
            float clipped = Mathf.Min(end, right);
            float uvRight = Mathf.Lerp(u0, u1, (clipped - left) / (end - left));
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(left, r.yMin, 0), color, new Vector2(u0, 0));
            vh.AddVert(new Vector3(left, r.yMax, 0), color, new Vector2(u0, 1));
            vh.AddVert(new Vector3(clipped, r.yMax, 0), color, new Vector2(uvRight, 1));
            vh.AddVert(new Vector3(clipped, r.yMin, 0), color, new Vector2(uvRight, 0));
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
