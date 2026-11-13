using UnityEngine;

namespace RugMaster
{
    // Equations and phase boundaries from updateItems/loopPos, payload-script-6.js.
    public static class RugFiberMotion
    {
        // Miệng hút: ống nhánh thòng xuống từ đoạn ống phía trên (mép dưới có kẹp kim loại), đo từ channel-back.png.
        public static readonly Vector2 Intake=new Vector2(RugSimulation.PipeImageLeft+782.5f,RugSimulation.PipeImageTop+186f);
        // Mép dưới vành 3 miệng xả ("Channel front lips" trong scene GamePlay).
        public const float LipBottom=1284f;
        public static void Advance(Fiber f,float dt,float time)
        {
            f.Rotation+=dt*2;
            if(f.Phase!=FiberPhase.Channel)f.Age+=dt;
            if(f.Phase==FiberPhase.Flight)
            {
                if(f.FlightStage==0)
                {
                    float k=Mathf.Min(1,f.Age/0.18f);
                    f.Position=new Vector2(Mathf.Lerp(f.Start.x,f.PopTo.x,k),Mathf.Lerp(f.Start.y,f.PopTo.y,1-(1-k)*(1-k)));
                    f.VisualScale=52f/300*(k<0.6f?k/0.6f*1.15f:1.15f-0.15f*(k-0.6f)/0.4f);
                    if(k>=1){f.FlightStage=1;f.Age=0;f.FlyFrom=f.Position;}
                }
                else if(f.FlightStage==1)
                {
                    float k=Mathf.Min(1,f.Age/f.FlyDuration),e=k*k;
                    // Điểm điều khiển nằm ngay dưới miệng hút để sợi bay vòng lên, chui thẳng vào miệng từ bên dưới.
                    var control=new Vector2(Intake.x,Mathf.Max(f.FlyFrom.y,Intake.y)+40);
                    f.Position=Bezier(f.FlyFrom,control,Intake,e);
                    f.VisualScale=52f/300*(1-0.15f*e);
                    if(k>=1){f.FlightStage=2;f.Age=0;}
                }
                else
                {
                    float k=Mathf.Min(1,f.Age/0.22f);
                    // Hút ngược lên theo ống nhánh vào làn của đoạn ống phía trên.
                    f.Position=new Vector2(Intake.x,Mathf.Lerp(Intake.y,RugSimulation.PipeTop-f.Dy,k*k));
                    // Co dần về cỡ cục bông trong ống.
                    f.VisualScale=52f/300*0.85f*Mathf.Lerp(1,ChannelSize,k);
                    if(k>=1){f.Phase=FiberPhase.Channel;f.Distance=RugSimulation.TopDistance(Intake.x);}
                }
            }
            else if(f.Phase==FiberPhase.Channel)
            {
                // Tốc độ theo vị trí trên vòng ống: phanh ở góc ngoặt, nhanh dần khi ra đoạn thẳng (RugPipeFlow).
                f.Distance+=RugSimulation.Speed*f.SpeedScale*RugPipeFlow.Flow(f.Distance)*dt;
                float tt=time*f.TwirlSpeed,ph=f.WanderPhase;
                f.Position=ChannelPosition(f.Distance,f.Dy,tt,ph,out f.ChannelDropY);
                f.Rotation+=dt*f.Spin*(0.6f+0.6f*Mathf.Sin(tt*6.1f+ph));
                f.VisualScale=ChannelScale(tt,ph);
            }
            else
            {
                float k=Mathf.Clamp01(f.Age/f.Duration);
                if(f.Sweep)
                {
                    float e=InOut(k);
                    f.Position=Bezier(f.CaptureFrom,new Vector2((f.CaptureFrom.x+f.CaptureTo.x)/2,
                        Mathf.Min(f.CaptureFrom.y,f.CaptureTo.y)-180),f.CaptureTo,e);
                    f.VisualScale=52f/300*ChannelSize*(0.85f+0.3f*Mathf.Sin(k*Mathf.PI));
                }
                else
                {
                    f.Position=OutletDrop(f.CaptureFrom,f.CaptureTo,k,f.CaptureFrom.x);
                    f.VisualScale=52f/300*ChannelSize*(0.85f-0.2f*k);
                }
            }
        }
        // Rơi qua miệng xả: rơi nhanh dần (y theo k²); còn trong ống nhánh (trên LipBottom) thì x bị giữ ở tâm miệng xả
        // (from.x trôi về outletX), ra khỏi mép miệng mới tạt ngang về chỗ trong giỏ. Không lòi ra ngoài thành ống nhánh.
        public static Vector2 OutletDrop(Vector2 from,Vector2 to,float k,float outletX)
        {
            float y=Mathf.Lerp(from.y,to.y,k*k);
            float span=to.y-LipBottom;
            float outside=span>1?Mathf.Clamp01((y-LipBottom)/span):1;
            float inStub=Mathf.Lerp(from.x,outletX,Mathf.Min(1,k*6));
            float e=1-(1-outside)*(1-outside);
            return new Vector2(Mathf.Lerp(inStub,to.x,e),y);
        }
        // Vị trí lắc lư trong ống tại quãng đường distance (tt = time*TwirlSpeed, ph = WanderPhase); laneY = y của làn (chưa lắc).
        // Ở góc ngoặt (RugPipeFlow.CornerIntensity): bị văng ra phía thành ngoài và lắc mạnh/nhanh hơn như va vào thành ống.
        public static Vector2 ChannelPosition(float distance,float dy,float tt,float ph,out float laneY)
        {
            float c=RugPipeFlow.CornerIntensity(distance);
            float along=(16*Mathf.Sin(tt*5.3f+ph)+7*Mathf.Sin(tt*11.7f+ph*1.3f))*ChannelSize;
            var q=RugSimulation.LoopPosition(distance+along,dy+CornerPush*c);
            laneY=RugSimulation.LoopPosition(distance+along,dy).y;
            float shake=ChannelSize*(1+1.4f*c),hit=c*c*5*ChannelSize;
            var p=new Vector2(q.x+shake*(9*Mathf.Sin(tt*7.1f+ph*2.1f)+4*Mathf.Sin(tt*15.3f+ph))+hit*Mathf.Sin(tt*31f+ph*2.7f),
                q.y+shake*(10*Mathf.Sin(tt*8.9f+ph*1.7f)+5*Mathf.Sin(tt*17.9f+ph*0.6f))+hit*Mathf.Sin(tt*27f+ph*1.9f));
            // Giữ bông trong lòng ống: độ lệch vuông góc với đường tâm ống bị nén mềm về tối đa MaxOffset
            // (dọc theo ống thì tự do). Lệch càng xa càng bị ép lại, như chạm thành ống rồi bật vào.
            var center=RugSimulation.LoopPosition(distance+along);
            var normal=RugSimulation.LoopPosition(distance+along,1)-center;
            float off=(p.x-center.x)*normal.x+(p.y-center.y)*normal.y;
            float soft=off/Mathf.Pow(1+(off/MaxOffset)*(off/MaxOffset),0.5f);
            return p+normal*(soft-off);
        }
        // Cục bông trong ống nhỏ hơn cục bông bay (ChannelSize); RugFiberTrail bù số lượng.
        // MaxOffset: độ lệch tối đa khỏi đường tâm ống (ống dày ~±56, cục bông bán kính ~28 ở ChannelSize 0.78).
        public const float ChannelSize=0.78f,CornerPush=8f,MaxOffset=22f;
        public static float ChannelScale(float tt,float ph)=>52f/300*ChannelSize*(0.85f*(1+0.07f*Mathf.Sin(tt*12.3f+ph)));
        public static Vector2 Bezier(Vector2 a,Vector2 b,Vector2 c,float t)=>a*((1-t)*(1-t))+b*(2*(1-t)*t)+c*(t*t);
        // Source E.inOut is quadratic, not Unity's cubic SmoothStep.
        public static float InOut(float t)=>t<0.5f?2*t*t:1-Mathf.Pow(-2*t+2,2)/2;
    }
}
