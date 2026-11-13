using System;
using System.Linq;
using UnityEngine;

namespace RugMaster
{
    [Serializable] public sealed class RugPresentationLevel{public int level;public string[] keys;public int[] colors;}
    [Serializable] public sealed class RugPresentationManifest{public RugPresentationLevel[] levels;}
    public static class RugPresentationAssets
    {
        private static RugPresentationManifest manifest;
        private static Material additive;
        public static RugPresentationLevel Level(int n)
        {
            if(manifest==null)manifest=RugPlayableAssets.Current.presentationManifest;
            return manifest.levels.First(x=>x.level==Constants.SourceIndex(n));
        }
        public static Texture2D Texture(string name)=>RugPlayableAssets.Load<Texture2D>("RugMaster/Presentation/"+name);
        public static Material Additive=>additive!=null?additive:(additive=RugPlayableAssets.Load<Material>("RugMaster/Presentation/Additive"));
    }
}
