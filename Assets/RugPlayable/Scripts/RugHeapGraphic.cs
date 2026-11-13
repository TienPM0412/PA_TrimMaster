using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugHeapGraphic : MaskableGraphic
    {
        private Basket basket;
        private int lastCollected=-1;
        [SerializeField] private Texture2D atlasTexture;
        public override Texture mainTexture => atlasTexture!=null?atlasTexture:Texture2D.whiteTexture;
        public void Bind(Basket model)
        {
            basket=model;
            if(atlasTexture==null)atlasTexture=RugOriginalAssets.Texture("fiber-atlas");
        }
        private void Update()
        {if(basket!=null&&lastCollected!=basket.Collected){lastCollected=basket.Collected;SetVerticesDirty();}}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(basket==null)return;
            int visible=Mathf.FloorToInt(24f*basket.Collected/basket.Capacity+0.5f),index=0;
            float hk=(304*0.997f/1108)/(279.2f/1108);
            int[] nums={6,6,5,4,3};float[] spans={190,196,170,130,90};
            for(int row=0;row<5;row++)for(int i=0;i<nums[row];i++,index++)
            {
                if(index>=visible)return;
                int variant=new[]{2,3,1}[index%3];
                var f=RugOriginalAssets.Frame("fiber-"+basket.Color+"-"+variant);
                float x=-spans[row]/2+spans[row]*(i+0.5f)/nums[row]+(Hash(i,row)-0.5f)*10;
                float y=-146-row*18+(Hash(row,i)-0.5f)*8;
                float w=(76-row*3)*hk*1.25f,h=w*f.rect[3]/f.rect[2];
                RugPileGraphic.Add(vh,RugPileGraphic.Quad(x*hk,y*hk,w,h,(Hash(index,3)-0.5f)*0.8f),f,Color.white);
            }
        }
        private static float Hash(int x,int y)
        {unchecked{uint n=(uint)(x*374761393+y*668265263);n=(n^(n>>13))*1274126177;return(n^(n>>16))/(float)uint.MaxValue;}}
    }
}
