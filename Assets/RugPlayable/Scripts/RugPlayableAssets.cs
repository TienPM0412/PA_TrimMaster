using System;
using UnityEngine;
namespace RugMaster
{
    [Serializable]
    public sealed class RugPlayableAssetEntry
    {
        public string path;
        public Texture2D texture;
        public Sprite sprite;
        public Font font;
        public Material material;
        public AudioClip audio;
        [TextArea] public string json;
    }
    public sealed class RugPlayableAssets : MonoBehaviour
    {
        public RugPlayableAssetEntry[] entries = new RugPlayableAssetEntry[0];
        // Dữ liệu level đã parse lúc Bake (index 0 = level 1), lấy từ PAManager.levelFiles.
        public LevelDefinition[] levels = new LevelDefinition[0];
        public LevelDefinition Level(int level)
        {
            if (levels == null || levels.Length == 0) throw new InvalidOperationException("Scene has no baked levels");
            return levels[Mathf.Clamp(level, 1, levels.Length) - 1];
        }
        public OriginalManifest originalManifest;
        public OriginalDom originalDom;
        public RugPresentationManifest presentationManifest;
        private static RugPlayableAssets active;
        public void Bind() { active = this; }
        public static RugPlayableAssets Current
        {
            get
            {
                if (active == null) active = UnityEngine.Object.FindObjectOfType<RugPlayableAssets>();
                if (active == null) throw new InvalidOperationException("Scene has no baked gameplay assets");
                return active;
            }
        }
        private static RugPlayableAssetEntry Entry(string path)
        {
            if (active == null) active = UnityEngine.Object.FindObjectOfType<RugPlayableAssets>();
            if (active == null) throw new InvalidOperationException("Scene has no baked gameplay assets");
            for (int i = 0; i < active.entries.Length; i++)
                if (active.entries[i].path == path) return active.entries[i];
            return null;
        }
        public static T Load<T>(string path) where T : UnityEngine.Object
        {
            var entry = Entry(path);
            if (entry == null) return null;
            if (typeof(T) == typeof(Texture2D)) return entry.texture as T;
            if (typeof(T) == typeof(Sprite)) return entry.sprite as T;
            if (typeof(T) == typeof(Font)) return entry.font as T;
            if (typeof(T) == typeof(Material)) return entry.material as T;
            if (typeof(T) == typeof(AudioClip)) return entry.audio as T;
            throw new InvalidOperationException("Unsupported baked asset type: " + typeof(T).Name);
        }
        public static string Json(string path)
        {
            var entry = Entry(path);
            if (entry == null || string.IsNullOrEmpty(entry.json)) throw new InvalidOperationException("Missing scene data: " + path);
            return entry.json;
        }
    }
}
