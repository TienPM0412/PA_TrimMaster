using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // Chỉ là hiển thị: ống bao quanh thảm dài gấp đôi ống gốc nên mỗi sợi trong ống kéo theo vài cục bông "phụ"
    // nối đuôi, để độ đầy nhìn thấy của ống khớp thanh Collection load (Fibers.Count / Capacity).
    // Cục phụ chui ra từ miệng hút sau sợi chính, chạy cùng làn và rơi theo sợi chính vào cùng giỏ. Không đổi luật chơi.
    public sealed class RugFiberTrail
    {
        // Số cục bông nhìn thấy khi ống đầy 100%, và khoảng cách (đơn vị thiết kế) giữa các cục nối đuôi.
        // GapTau = TauLength/FullPuffs để khi đầy, cục phụ của một sợi vừa lấp khoảng trống tới sợi phía sau (sợi đã giãn đều).
        public const float FullPuffs=150f;
        private static readonly float Gap=RugSimulation.LoopLength/FullPuffs;
        // Cục phụ nối đuôi theo Tau (thời gian chạy, RugPipeFlow) nên cũng dồn lại ở góc ngoặt như sợi chính.
        private static readonly float GapTau=RugPipeFlow.TauLength/FullPuffs;

        private sealed class Puff
        {
            public RawImage image;
            public int k,variant;
            public float dist,rotation,spin,phase,twirl,dy,age,delay,duration,scale,outletX;
            public bool capturing,sweep,dead;
            public Vector2 pos,from,to;
        }
        private sealed class Group{public readonly List<Puff> puffs=new List<Puff>();public bool alive;public string color;}

        private readonly Dictionary<int,Group> groups=new Dictionary<int,Group>();
        private readonly List<int> removeIds=new List<int>();
        private readonly Stack<RawImage> pool=new Stack<RawImage>();
        private readonly RectTransform fibersRoot,dropsRoot,flyersRoot;
        private readonly Func<string,int,RugOriginalView.FlightSprite> sprite;
        private RugSimulation simulation;
        private float lastTime;
        private static float Entry=>RugSimulation.TopDistance(RugFiberMotion.Intake.x);

        public RugFiberTrail(RectTransform fibers,RectTransform drops,RectTransform flyers,Func<string,int,RugOriginalView.FlightSprite> spriteLookup)
        {fibersRoot=fibers;dropsRoot=drops;flyersRoot=flyers;sprite=spriteLookup;}

        public void Clear()
        {
            foreach(var g in groups.Values)foreach(var p in g.puffs)Release(p);
            groups.Clear();simulation=null;
        }

        // Số cục phụ của một sợi: tổng (1 + phụ) trên Capacity sợi ~ FullPuffs; phần lẻ chia theo Id cho đều.
        private static int ExtraCount(int id,int capacity)
        {
            float extra=Mathf.Max(0,FullPuffs/Mathf.Max(1,capacity)-1);
            int whole=(int)extra;float frac=extra-whole;
            float h=id*0.6180339f;h-=Mathf.Floor(h);
            return whole+(h<frac?1:0);
        }

        public void Update(RugSimulation model)
        {
            if(model!=simulation){Clear();simulation=model;lastTime=model.Elapsed;}
            float time=model.Elapsed,dt=Mathf.Max(0,time-lastTime);lastTime=time;
            foreach(var g in groups.Values)g.alive=false;
            foreach(var f in model.Fibers)
            {
                if(!groups.TryGetValue(f.Id,out var g))
                {
                    g=new Group{color=f.Color};groups.Add(f.Id,g);
                    int n=ExtraCount(f.Id,model.Capacity);
                    for(int k=1;k<=n;k++)
                    {
                        float seed=(f.Id*7.31f+k*3.17f)%1f;
                        g.puffs.Add(new Puff{k=k,variant=(f.Variant+k*2)%6,phase=f.WanderPhase+k*1.9f,twirl=f.TwirlSpeed*(0.9f+0.2f*seed),
                            dy=Mathf.Clamp(f.Dy+(seed-0.5f)*26,-14,14),rotation=f.Rotation+k*1.3f,spin=-f.Spin*(0.8f+0.4f*seed),dist=float.MinValue});
                    }
                }
                g.alive=true;
                foreach(var p in g.puffs)
                {
                    if(p.capturing||p.dead)continue;
                    if(f.Phase==FiberPhase.Channel)
                    {
                        p.dist=RugPipeFlow.DistanceAt(RugPipeFlow.Tau(f.Distance)-p.k*GapTau);
                        if(p.dist<Entry)continue;
                        Follow(p,time,dt);
                        if(p.image==null)p.image=Acquire();
                    }
                    else if(f.Phase==FiberPhase.Capture)
                    {
                        // Cục phụ chưa chui ra khỏi miệng hút thì bỏ luôn.
                        if(p.image==null){p.dead=true;continue;}
                        p.capturing=true;p.sweep=f.Sweep;p.age=0;p.duration=f.Duration;p.from=p.pos;p.outletX=f.CaptureFrom.x;
                        p.to=f.CaptureTo+new Vector2(((p.k*0.37f)%1f-0.5f)*50,0);
                        p.delay=f.Sweep?p.k*0.05f:p.k*GapTau/RugSimulation.Speed;
                    }
                }
            }
            foreach(var pair in groups)
            {
                var g=pair.Value;bool any=false;
                foreach(var p in g.puffs)
                {
                    if(p.capturing&&!p.dead)AdvanceCapture(p,time,dt);
                    if(!g.alive&&!p.capturing)p.dead=true;
                    if(p.dead){Release(p);continue;}
                    any=true;
                    if(p.image!=null)Draw(p,g.color);
                }
                if(!g.alive&&!any)removeIds.Add(pair.Key);
            }
            foreach(int id in removeIds)groups.Remove(id);
            removeIds.Clear();
        }

        private void Follow(Puff p,float time,float dt)
        {
            float tt=time*p.twirl;
            p.pos=RugFiberMotion.ChannelPosition(p.dist,p.dy,tt,p.phase,out _);
            p.rotation+=dt*p.spin*(0.6f+0.6f*Mathf.Sin(tt*6.1f+p.phase))+dt*2;
            // Vừa chui ra miệng hút thì phồng dần lên.
            p.scale=RugFiberMotion.ChannelScale(tt,p.phase)*Mathf.Clamp01(0.35f+(p.dist-Entry)/Gap);
        }

        private void AdvanceCapture(Puff p,float time,float dt)
        {
            p.age+=dt;
            if(p.age<p.delay)
            {
                // Chưa tới lượt: vẫn chạy tiếp theo ống tới đúng miệng xả mà sợi chính đã rơi xuống.
                if(!p.sweep){p.dist+=RugSimulation.Speed*RugPipeFlow.Flow(p.dist)*dt;Follow(p,time,dt);p.from=p.pos;}
                return;
            }
            float k=Mathf.Clamp01((p.age-p.delay)/p.duration);
            if(p.sweep)
            {
                float e=RugFiberMotion.InOut(k);
                p.pos=RugFiberMotion.Bezier(p.from,new Vector2((p.from.x+p.to.x)/2,Mathf.Min(p.from.y,p.to.y)-180),p.to,e);
                p.scale=52f/300*RugFiberMotion.ChannelSize*(0.85f+0.3f*Mathf.Sin(k*Mathf.PI))*(1-0.6f*k*k);
            }
            else
            {
                p.pos=RugFiberMotion.OutletDrop(p.from,p.to,k,p.outletX);
                p.scale=52f/300*RugFiberMotion.ChannelSize*(0.85f-0.35f*k);
            }
            p.rotation+=dt*2;
            if(k>=1)p.dead=true;
        }

        private void Draw(Puff p,string color)
        {
            var parent=!p.capturing||p.age<p.delay?fibersRoot:!p.sweep&&p.pos.y-18<=RugFiberMotion.LipBottom?dropsRoot:flyersRoot;
            if(p.image.transform.parent!=parent)p.image.transform.SetParent(parent,false);
            if(!p.image.gameObject.activeSelf)p.image.gameObject.SetActive(true);
            var s=sprite(color,p.variant);
            p.image.uvRect=s.uv;
            var rect=p.image.rectTransform;
            rect.anchoredPosition=new Vector2(p.pos.x,-p.pos.y);rect.sizeDelta=s.size*(p.scale/0.28f);
            rect.localEulerAngles=new Vector3(0,0,-p.rotation*Mathf.Rad2Deg);
        }

        private RawImage Acquire()
        {
            if(pool.Count>0)return pool.Pop();
            var image=RugOriginalView.Rect(fibersRoot,"Fiber trail puff",0,0,0,0).gameObject.AddComponent<RawImage>();
            image.texture=RugOriginalAssets.Texture("flight-atlas");image.raycastTarget=false;
            image.rectTransform.pivot=new Vector2(0.5f,0.5f);
            return image;
        }
        private void Release(Puff p)
        {
            if(p.image==null)return;
            p.image.gameObject.SetActive(false);pool.Push(p.image);p.image=null;
        }
    }
}
