using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    public sealed class RugToolView : MonoBehaviour
    {
        public RawImage body,shadow,teeth;
        [Header("Cutting vibration")]
        [SerializeField, Min(0)] private float vibrationDistance=4.5f;
        [SerializeField, Min(0)] private float vibrationAngle=1.2f;
        [SerializeField, Min(0)] private float vibrationFrequency=27f;
        private string previousHead;
        private OriginalToolHead currentHead;
        private float raise=1,raiseFrom=1,raiseTo=1,raiseAt=-1,headAt=-100;
        private float vibrationStrength,lastShownAt=-1;

        private void OnDisable()
        {
            vibrationStrength=0;
            lastShownAt=-1;
        }
        public void Build()
        {
            if(body!=null)return;
            shadow=Create("Contact shadow");body=Create("Original trimmer body");teeth=Create("Moving blade teeth");
        }
        private RawImage Create(string name)
        {
            var image=RugOriginalView.Rect(transform,name,0,0,1,1).gameObject.AddComponent<RawImage>();
            image.raycastTarget=false;return image;
        }
        public void Show(string head,Vector2 position,float angle,bool raised,bool cutting,bool blocked,float time)
        {
            Build();
            bool first=previousHead==null;
            if(previousHead!=head)
            {
                if(!first)headAt=time;
                previousHead=head;
                currentHead=RugOriginalAssets.Manifest.toolHeads.First(x=>x.name==head);
                body.texture=shadow.texture=RugOriginalAssets.Texture("tool-body-"+head);
                teeth.texture=RugOriginalAssets.Texture("blade-"+head);
            }
            var h=currentHead;
            float target=raised?1:0;
            if(target!=raiseTo){raiseFrom=raise;raiseTo=target;raiseAt=time;}
            float t=Mathf.Clamp01((time-raiseAt)/0.12f);raise=Mathf.Lerp(raiseFrom,raiseTo,1-Mathf.Pow(1-t,3));
            float scale=Mathf.Lerp(1,1.05f,raise),off=Mathf.Lerp(12,30,raise);
            float dt=lastShownAt<0||time<lastShownAt?0:time-lastShownAt;
            if(time<lastShownAt)vibrationStrength=0;
            lastShownAt=time;
            bool vibrating=cutting&&!raised&&!blocked;
            vibrationStrength=Mathf.MoveTowards(vibrationStrength,vibrating?1:0,dt/(vibrating?0.045f:0.08f));
            float phaseAngle=time*vibrationFrequency*Mathf.PI*2;
            var jitter=new Vector2(Mathf.Sin(phaseAngle),Mathf.Sin(phaseAngle*1.37f+1)*0.65f)*vibrationDistance*vibrationStrength;
            float rock=Mathf.Sin(phaseAngle*0.73f)*vibrationAngle*vibrationStrength;
            var parent=(RectTransform)transform;parent.anchoredPosition=new Vector2(position.x,-position.y);
            parent.localEulerAngles=new Vector3(0,0,-angle*Mathf.Rad2Deg+rock);
            float hx=1,hy=1,ht=time-headAt;
            if(ht>=0&&ht<0.38f)
            {float k=1-RugPilePose.OutBack(ht/0.38f,2.4f);hx+=((head=="wide"?1.12f:head=="narrow"?0.9f:1.06f)-1)*k;hy+=((head=="wide"?0.92f:1.08f)-1)*k;}
            Set(body,h.rect,new Vector2(scale*hx,scale*hy),jitter);
            Set(shadow,h.rect,Vector2.one,new Vector2(off*0.3f,off));
            shadow.color=new Color(58/255f,34/255f,16/255f,Mathf.Lerp(0.32f,0.22f,raise));
            body.color=blocked?(Color)new Color32(255,194,122,255):Color.white;
            teeth.gameObject.SetActive(cutting);
            if(cutting)
            {
                int phase=Mathf.FloorToInt(time/0.026f)&1;
                Set(teeth,h.blade,new Vector2(scale*hx,scale*hy),jitter+new Vector2(h.pitch*(phase==1?0.5f:-0.25f)*scale*hx,0));
                teeth.color=phase==1?Color.white:(Color)new Color32(201,211,222,255);
            }
        }
        private static void Set(RawImage image,float[] rect,Vector2 scale,Vector2 offset)
        {RugOriginalView.Position(image.rectTransform,new Rect(rect[0]*scale.x+offset.x,rect[1]*scale.y+offset.y,rect[2]*scale.x,rect[3]*scale.y));}
    }
}
