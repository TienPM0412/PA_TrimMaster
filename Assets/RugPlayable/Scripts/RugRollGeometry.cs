using UnityEngine;

namespace RugMaster
{
    public struct RugRollState
    {
        public float edge,x,hop,squash,spin;
        public static RugRollState Intro(float time)
        {
            float x=Mathf.LerpUnclamped(1790,540,RugPilePose.OutBack(Mathf.Clamp01(time/0.55f),0.9f));
            float squash=time<0.55f?1:Mathf.LerpUnclamped(0.84f,1,RugPilePose.OutBack(Mathf.Clamp01((time-0.55f)/0.24f),3));
            float u=Mathf.Clamp01((time-0.69f)/0.72f);
            return new RugRollState{edge=Mathf.Lerp(196,1000,1-Mathf.Pow(1-u,3)),x=x,hop=0,squash=squash,
                spin=-14*RugPilePose.OutBack(Mathf.Clamp01(time/0.55f),0.9f)};
        }
    }
    public struct RugRollShape
    {
        public Rect body,shadow,clip;
        public float shadowAlpha,diameter,capRadius,halfWidth,centerY;
        public bool visible;
        public static RugRollShape Evaluate(RugRollState s)
        {
            float d=2*Mathf.Sqrt(Mathf.Max(0,996-Mathf.Min(s.edge,996))*22/Mathf.PI+18*18);
            float cy=s.edge+s.hop-d*(s.squash-1)*0.5f,dh=d*s.squash,dw=d/Mathf.Sqrt(s.squash);
            float half=480+(dw-d)*0.6f,rx=Mathf.Max(6,d*0.2f),lead=Mathf.Clamp01((s.edge-196)/80);
            return new RugRollShape{body=new Rect(s.x-(half*2-rx)/2,cy-dh/2,half*2-rx,dh),
                shadow=new Rect(s.x-(half*2+40)/2,s.edge+d*0.45f-d*0.7f*(1-Mathf.Min(0.5f,-s.hop/160))/2,
                    half*2+40,d*0.7f*(1-Mathf.Min(0.5f,-s.hop/160))),
                shadowAlpha=0.5f*(1-Mathf.Min(0.6f,-s.hop/140)),diameter=d,capRadius=rx,halfWidth=half,centerY=cy,
                clip=new Rect(-30,196-120*lead,1140,Mathf.Max(0,s.edge-196+120*lead)),visible=s.edge<994};
        }
    }
}
