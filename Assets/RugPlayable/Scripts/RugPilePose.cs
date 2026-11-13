using UnityEngine;

namespace RugMaster
{
    public struct RugCellPose
    {
        public Vector2 scale;
        public float rotation,alpha,shadowAlpha;
    }
    public static class RugPilePose
    {
        public static float OutBack(float t,float strength)=>1+(strength+1)*Mathf.Pow(t-1,3)+strength*(t-1)*(t-1);
        public static RugCellPose Evaluate(Cell cell,float time)
        {
            var p=new RugCellPose{scale=Vector2.one,alpha=1,shadowAlpha=1};
            float wobble=time-cell.WobbleAt;
            if(wobble>=0&&wobble<0.38f)
            {
                float remain=1-OutBack(wobble/0.38f,3);
                p.rotation=cell.WobbleRotation*remain;
                p.scale=new Vector2(1+0.08f*cell.WobbleStrength*remain,1-0.08f*cell.WobbleStrength*remain);
            }
            if(!cell.Trimmed)return p;
            float t=Mathf.Max(0,time-cell.TrimmedAt),s;
            if(t<0.05f){float k=t/0.05f;s=1+0.15f*(1-(1-k)*(1-k));}
            else{float k=Mathf.Clamp01((t-0.05f)/0.12f);s=1.15f*(1-k*k);p.alpha=1-k*k;}
            p.scale*=s;p.shadowAlpha=Mathf.Clamp01(1-t/0.15f);
            return p;
        }
        public static float[] Transform(float[] vertices,Vector2 origin,RugCellPose pose)
        {
            var output=new float[8];
            Transform(vertices,origin,pose,output);
            return output;
        }
        public static void Transform(float[] vertices,Vector2 origin,RugCellPose pose,float[] output)
        {
            float c=Mathf.Cos(pose.rotation),s=Mathf.Sin(pose.rotation);
            for(int i=0;i<8;i+=2)
            {
                float x=(vertices[i]-origin.x)*pose.scale.x,y=(vertices[i+1]-origin.y)*pose.scale.y;
                output[i]=origin.x+c*x-s*y;output[i+1]=origin.y+s*x+c*y;
            }
        }
    }
}
