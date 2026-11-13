using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugRollCapsGraphic : MaskableGraphic
    {
        private RugRollState state;
        private RugRollShape shape;
        private Color dominant=Color.white;
        public void Set(RugRollState value,RugRollShape geometry,Color ink)
        {state=value;shape=geometry;dominant=ink;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();float d=shape.diameter,rx=shape.capRadius,dh=d*state.squash,cy=shape.centerY;
            foreach(int sign in new[]{-1,1})
            {
                float ex=state.x+sign*(shape.halfWidth-rx/2);
                Ellipse(vh,ex,cy,rx,dh/2,Hex(14205594),0);
                Ellipse(vh,ex,cy,rx,dh/2,Hex(9336144),2);
                int n=Mathf.Max(2,Mathf.FloorToInt(d/13));
                for(int i=1;i<=n;i++)
                {float f=1-i/(float)(n+1);Ellipse(vh,ex+sign*rx*(1-f)*0.2f,cy,rx*f,dh*0.5f*f,i%2==1?dominant:Hex(13150840),Mathf.Max(2,d/22));}
                Ellipse(vh,ex+sign*rx*0.18f+Mathf.Cos(state.spin)*rx*0.08f,cy+Mathf.Sin(state.spin)*dh*0.12f,
                    rx*0.14f,dh*0.06f,Hex(7228974),0);
            }
        }
        public static Color Hex(int n)=>new Color32((byte)(n>>16),(byte)(n>>8),(byte)n,255);
        private static void Ellipse(VertexHelper vh,float x,float y,float rx,float ry,Color color,float stroke)
        {
            const int segments=64;int start=vh.currentVertCount;
            if(stroke==0)
            {
                vh.AddVert(new Vector3(x,-y),color,Vector2.zero);
                for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2/segments;vh.AddVert(new Vector3(x+Mathf.Cos(a)*rx,-y-Mathf.Sin(a)*ry),color,Vector2.zero);}
                for(int i=0;i<segments;i++)vh.AddTriangle(start,start+i+1,start+i+2);
            }
            else
            {
                for(int i=0;i<=segments;i++)
                {float a=i*Mathf.PI*2/segments,c=Mathf.Cos(a),s=Mathf.Sin(a);
                    vh.AddVert(new Vector3(x+c*(rx+stroke/2),-y-s*(ry+stroke/2)),color,Vector2.zero);
                    vh.AddVert(new Vector3(x+c*Mathf.Max(0,rx-stroke/2),-y-s*Mathf.Max(0,ry-stroke/2)),color,Vector2.zero);}
                for(int i=0;i<segments;i++){int k=start+i*2;vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k+1,k+3,k+2);}
            }
        }
    }
}
