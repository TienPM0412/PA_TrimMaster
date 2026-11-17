using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RugMaster
{
    [Serializable] public sealed class OriginalSprite
    {
        public string name, kind;
        public int width, height, level, index, count;
        public float[] rect,cut;
        // Frame trong atlas (fiber/flight): rect = toạ độ atlas gốc (4096/2048) - quyết định cỡ hiển thị;
        // src = vị trí của chính frame đó trong ảnh atlas hiện tại (art mới đóng gói lại/thu nhỏ). Không có src thì dùng rect.
        public float[] src;
    }
    [Serializable] public sealed class OriginalPiece { public int variant; public float[] vertices; }
    [Serializable] public sealed class OriginalCell { public float[] origin,shadow; public OriginalPiece[] pieces; }
    [Serializable] public sealed class OriginalToolHead {public string name;public float[] rect,blade;public float pitch;}
    [Serializable] public sealed class OriginalLayout { public int cols, rows; public OriginalCell[] cells; }
    [Serializable] public sealed class OriginalManifest
    {
        public int width, height;
        public OriginalSprite[] sprites, atlas, flightAtlas;
        public OriginalToolHead[] toolHeads;
        public OriginalLayout[] layouts;
        public float[] atlasSize, flightAtlasSize;
    }
    [Serializable] public sealed class OriginalHit {public float[] rect;}
    [Serializable] public sealed class OriginalJam {public float[] play,retry;}
    [Serializable] public sealed class OriginalDom
    {
        public OriginalHit[] heads,settings;
        public OriginalJam[] jam;
    }

    public static class RugOriginalAssets
    {
        private static OriginalManifest manifest;
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private static Dictionary<string, OriginalSprite> records, frames;
        private static Dictionary<string, OriginalSprite> flightFrames;
        private static OriginalDom dom;
        public static OriginalDom Dom => dom??(dom=RugPlayableAssets.Current.originalDom);
        public static OriginalManifest Manifest
        {
            get
            {
                if (manifest == null)
                {
                    var source = RugPlayableAssets.Current.originalManifest;
                    if (source == null) throw new InvalidOperationException("Original UI assets have not been exported");
                    manifest = source;
                    records = manifest.sprites.ToDictionary(s => s.name);
                    frames = manifest.atlas.ToDictionary(s => s.name);
                }
                return manifest;
            }
        }
        /// <summary>Xoá cache static (manifest/dom/texture). Editor không reload domain khi thoát Play mode nên cache cũ
        /// có thể còn sót; bake phải gọi hàm này sau khi gán manifest mới, nếu không sẽ tính UV theo manifest cũ.</summary>
        public static void ResetCache()
        {
            manifest = null; records = null; frames = null; flightFrames = null; dom = null;
            textures.Clear();
        }
        public static OriginalSprite Record(string key) { _ = Manifest; return records.TryGetValue(key,out var r) ? r : null; }
        public static OriginalSprite Frame(string key) { _ = Manifest; return frames[key]; }
        /// <summary>UV (gốc dưới-trái như RawImage.uvRect) của frame trong fiber-atlas / flight-atlas. Chia theo cỡ ảnh nguồn
        /// ghi trong manifest (atlasSize/flightAtlasSize), không theo texture.width vì import có thể thu nhỏ ảnh.</summary>
        public static Rect AtlasUV(OriginalSprite frame,bool flight=false)
        {
            var size=flight?Manifest.flightAtlasSize:Manifest.atlasSize;
            float w=size!=null&&size.Length==2?size[0]:flight?2048:4096,h=size!=null&&size.Length==2?size[1]:flight?2048:4096;
            var r=frame.src!=null&&frame.src.Length==4?frame.src:frame.rect;
            return new Rect(r[0]/w,1-(r[1]+r[3])/h,r[2]/w,r[3]/h);
        }
        public static OriginalSprite FlightFrame(string key)
        {
            if(flightFrames==null)flightFrames=Manifest.flightAtlas.ToDictionary(f=>f.name);
            return flightFrames[key];
        }
        public static Texture2D Texture(string key)
        {
            var t = TryTexture(key);
            if (t == null) throw new InvalidOperationException("Missing original UI texture: " + key);
            return t;
        }
        public static Texture2D TryTexture(string key)
        {
            if (textures.TryGetValue(key,out var t) && t != null) return t;
            string folder = key.StartsWith("base-") ? "Bases/" : key.StartsWith("header-") ? "Headers/" : "";
            t = RugPlayableAssets.Load<Texture2D>("RugMaster/OriginalUI/" + folder + key);
            if (t != null) textures[key] = t;
            return t;
        }

        private static readonly Dictionary<(int,int),OriginalLayout> generatedLayouts = new Dictionary<(int,int),OriginalLayout>();
        /// <summary>Layout búi len theo cỡ lưới. Game gốc chỉ có 5 cỡ; cỡ khác được suy ra từ layout gần nhất.</summary>
        public static OriginalLayout Layout(int cols,int rows)
        {
            var exact = Manifest.layouts.FirstOrDefault(l => l.cols==cols && l.rows==rows);
            if (exact != null) return exact;
            if (!generatedLayouts.TryGetValue((cols,rows), out var layout))
                generatedLayouts[(cols,rows)] = layout = DeriveLayout(cols, rows);
            return layout;
        }

        // Lấy hình dạng 4 mảnh len của ô cùng vị trí (lặp lại theo lưới nguồn), co giãn theo kích thước ô mới.
        private static OriginalLayout DeriveLayout(int cols,int rows)
        {
            var r = Constants.RugRect;
            float cw = r.width / cols, ch = r.height / rows;
            var source = Manifest.layouts.OrderBy(l => Mathf.Abs(r.width / l.cols - cw) + Mathf.Abs(r.height / l.rows - ch)).First();
            float sw = r.width / source.cols, sh = r.height / source.rows, kx = cw / sw, ky = ch / sh;
            var cells = new OriginalCell[cols * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int sx = x % source.cols, sy = y % source.rows;
                    var s = source.cells[sy * source.cols + sx];
                    float fromX = r.x + (sx + 0.5f) * sw, fromY = r.y + (sy + 0.5f) * sh;
                    float toX = r.x + (x + 0.5f) * cw, toY = r.y + (y + 0.5f) * ch;
                    float[] Map(float[] p)
                    {
                        if (p == null) return null;
                        var q = new float[p.Length];
                        for (int i = 0; i < p.Length; i += 2)
                        { q[i] = toX + (p[i] - fromX) * kx; q[i + 1] = toY + (p[i + 1] - fromY) * ky; }
                        return q;
                    }
                    cells[y * cols + x] = new OriginalCell { origin = Map(s.origin), shadow = Map(s.shadow),
                        pieces = s.pieces.Select(p => new OriginalPiece { variant = p.variant, vertices = Map(p.vertices) }).ToArray() };
                }
            return new OriginalLayout { cols = cols, rows = rows, cells = cells };
        }
    }
}
