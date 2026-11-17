using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // Standalone presentation port. The production host's separate result UI is a different flow.
    [DefaultExecutionOrder(200)]
    public sealed class RugPresentation : MonoBehaviour
    {
        private enum Stage{Playing,Intro,Finishing}
        private Stage stage;
        private RugOriginalView view;
        private RugTrimEffects effects;
        private RugSimulation simulation;
        private RugPresentationLevel level;
        private RectTransform world,rug,reveal,inner,roll,sweepMask;
        private RawImage rollBody,rollShadow,flash,sweep;
        private RugRollCapsGraphic caps;
        private float clock,totalTime,previousClock,lastDust;
        private bool celebration,secondCurl,tutorialStarted;
        // Diễn xong màn thắng chỉ báo RugGameFlow một lần (popup Win / sang level kế).
        private bool winHandled;
        private Vector2 flingFrom;
        private float flingAngle;
        private readonly List<Track> channels=new List<Track>();
        private Mask revealMask;
        private bool revealClipOn;
        private readonly System.Random random=new System.Random(77219);
        private sealed class Track{public RectTransform rect;public Vector2 position;public CanvasGroup group;}
        public bool BlocksInput=>stage==Stage.Intro&&clock<1.41f||stage==Stage.Finishing;
        public bool IsFinishing=>stage==Stage.Finishing;
        public bool ManualClock;
        public float TimeInStage=>clock;
        private float Random(float a,float b)=>a+(float)random.NextDouble()*(b-a);

        private static ControllerManager Input=>ControllerManager.Ins;
        // SFX màn diễn như game gốc: roll_up lúc thảm cuộn lăn ra (intro), ribbon lúc thảm loé sáng (thắng).
        private void Sfx(string clip){if(!ManualClock&&SoundManager.HasInstance)SoundManager.Ins.PlaySFX(clip,SoundManager.SFXType.Important);}
        public void Initialize(RugOriginalView owner,RugTrimEffects particles)
        {
            view=owner;effects=particles;EnsureHierarchy();Bind(LevelManager.Ins.Simulation,true);
        }
        private static CanvasGroup Group(RectTransform rect)
        {var g=rect.GetComponent<CanvasGroup>();if(g==null)g=rect.gameObject.AddComponent<CanvasGroup>();return g;}
        private static RawImage Image(Transform parent,string name,Texture texture)
        {var r=RugOriginalView.Rect(parent,name,0,0,1,1).gameObject.AddComponent<RawImage>();r.texture=texture;r.raycastTarget=false;return r;}
        private void Move(Transform node,Transform parent){if(node!=null)node.SetParent(parent,false);}
        // Intro: dao chờ ngay trên mép trên màn hình rồi rơi xuống. Màn hình dài hơn 9:16 thì nền lộ ra phía trên
        // khung 1080x1920, nên tính theo mép trên thật của màn hình (toạ độ thảm, cả thân dao ~300 nằm khuất).
        private static float IntroToolStartY()
        {
            float scale=Mathf.Min(Screen.width/1080f,Screen.height/1920f);
            float screenTop=scale>0?-(Screen.height/scale-1920)*0.5f:0;
            return Mathf.Min(-590,RugFit.ToRug(new Vector2(0,screenTop)).y-320);
        }
        private void EnsureHierarchy()
        {
            world=RugOriginalView.Rect(view.content,"Native gameplay world",0,0,1080,1920);
            world.SetSiblingIndex(view.GameplayBaseIndex);
            // Thảm và dao cắt đã nằm sẵn trong container RugFit của scene (RugOriginalView.EnsureRugFit):
            // chỉ chuyển cả container vào world, giữ nguyên vị trí/tỉ lệ đã lưu trong scene.
            view.EnsureRugFit();
            var rugFit=view.rugFit;Move(rugFit,world);
            var shadow=rugFit.Find("Rug shadow");var binding=rugFit.Find("Original braided binding");
            rug=RugOriginalView.Rect(rugFit,"Rug group",0,0,1080,1920);
            // Mask stencil, không dùng RectMask2D: Luna (web) chỉ cull cả graphic theo RectMask2D chứ không cắt
            // (MaskableGraphic.SetClipRect để TODO), còn Mask stencil được hỗ trợ đầy đủ. Chỉ bật lúc trải thảm, xem RevealClip.
            reveal=RugOriginalView.Rect(rug,"Roll reveal mask",0,0,1080,1920);
            revealMask=StencilMask(reveal);
            inner=RugOriginalView.Rect(reveal,"Rug inner - squash around center",0,0,1080,1920);
            var clipping=view.tuftMesh.transform.parent;
            Move(shadow,inner);Move(view.basePile.transform,inner);Move(view.shade.transform,inner);Move(clipping,inner);
            Move(view.sheenRoot,inner);Move(binding,inner);Move(view.marksRoot,inner);
            inner.pivot=new Vector2(0.5f,1-596f/1920);inner.anchoredPosition=new Vector2(540,-596);
            view.tuftMesh.maskable=view.shadowMesh.maskable=true;
            roll=RugOriginalView.Rect(rug,"Original procedural rug roll",0,0,1080,1920);
            rollShadow=Image(roll,"Roll contact shadow",RugOriginalAssets.Texture("soft"));rollShadow.color=new Color(58/255f,34/255f,16/255f,0.5f);
            rollBody=Image(roll,"Woven roll body",null);
            caps=RugOriginalView.Rect(roll,"Spiral end caps",0,0,1080,1920).gameObject.AddComponent<RugRollCapsGraphic>();caps.raycastTarget=false;
            // "Intake front lip" có thể đã được đặt làm con của channel trong scene: khi đó nó đi theo channel,
            // không tách ra world và không cần track riêng (Find chỉ tìm con trực tiếp của content).
            var intake=view.content.Find("Intake front lip");
            foreach(var node in new Transform[]{view.channel.transform,view.fibersRoot,view.dropsRoot,view.lips.transform,
                view.basketsRoot,view.flyersRoot,view.meter.transform,intake,view.toolFit,view.partsRoot})Move(node,world);
            view.PutFibersBehindPipe();
            foreach(var node in new Transform[]{view.channel.transform,view.fibersRoot,view.dropsRoot,view.lips.transform,view.meter.transform,intake})
            {if(node==null)continue;var r=(RectTransform)node;channels.Add(new Track{rect=r,position=r.anchoredPosition,group=Group(r)});}
            flash=Image(inner,"Completion flash",RugOriginalAssets.Texture("soft"));
            RugOriginalView.Position(flash.rectTransform,new Rect(-180,-4,1440,1200));flash.material=RugPresentationAssets.Additive;
            sweepMask=RugOriginalView.Rect(rug,"Completion sheen clipping",60,196,960,800);
            sweep=Image(sweepMask,"Final light sweep",RugPresentationAssets.Texture("sweep"));sweep.material=RugPresentationAssets.Additive;
            SetStencilMask(StencilMask(sweepMask),true);
            sweep.rectTransform.pivot=new Vector2(0.5f,0.5f);sweep.rectTransform.sizeDelta=new Vector2(520,1300);
            sweep.rectTransform.localEulerAngles=new Vector3(0,0,-0.26f*Mathf.Rad2Deg);
        }
        public void Bind(RugSimulation model,bool intro)
        {
            simulation=model;level=RugPresentationAssets.Level(LevelManager.Ins.CurrentLevel);clock=totalTime=previousClock=0;
            stage=intro?Stage.Intro:Stage.Playing;celebration=secondCurl=tutorialStarted=winHandled=false;lastDust=-1;
            effects.ExternalTime=-1;rollBody.texture=RugPresentationAssets.Texture($"roll-{level.level:000}");
            rug.anchoredPosition=world.anchoredPosition=Vector2.zero;inner.localScale=Vector3.one;
            SetReveal(new Rect(0,0,1080,1920));roll.gameObject.SetActive(intro);
            flash.gameObject.SetActive(false);sweepMask.gameObject.SetActive(false);ApplyChannels(1,0);SetFooter(!intro,0);
            view.SetPresentationTutorialVisible(!intro);
            Apply();
        }
        public void BeginIntro(){stage=Stage.Intro;clock=0;previousClock=0;lastDust=-1;tutorialStarted=false;Apply();}
        public void BeginFinish()
        {
            if(stage==Stage.Finishing)return;
            stage=Stage.Finishing;clock=0;previousClock=0;celebration=secondCurl=winHandled=false;
            flingFrom=Input.HasPlacedTool?Input.ToolPosition:new Vector2(880,560);flingAngle=Input.ToolAngle;Input.EndTrim();
            SetReveal(new Rect(0,0,1080,1920));SetFooter(false,0);
        }
        private void LateUpdate()
        {
            if(view==null||simulation==null)return;
            if(simulation.State==PlayState.Won&&stage!=Stage.Finishing)BeginFinish();
            if(!ManualClock&&!GameManager.Ins.IsModalPaused)Advance(Mathf.Min(0.05f,Time.unscaledDeltaTime));
            Apply();
        }
        public void Advance(float dt)
        {
            previousClock=clock;clock+=dt;totalTime+=dt;
            effects.ExternalTime=totalTime;
            if(stage==Stage.Intro&&previousClock<=0&&clock>0)Sfx(SoundFX.RollUp);
            if(stage==Stage.Intro)
            {
                if(clock<0.55f&&clock-lastDust>=0.06f)
                {var s=RugRollState.Intro(clock);Dust(new Vector2(s.x+460,236),2);lastDust=clock;}
                if(clock>=0.69f&&clock<1.38f&&clock-lastDust>=0.07f)
                {float y=RugRollState.Intro(clock).edge;Dust(new Vector2(90,y),2);Dust(new Vector2(990,y),2);lastDust=clock;}
                if(previousClock<1.41f&&clock>=1.41f)for(int i=0;i<6;i++)Dust(new Vector2(140+i*160,1006),3);
                if(clock>=1.76f&&!tutorialStarted){tutorialStarted=true;view.SetPresentationTutorialVisible(true);view.ResumeTutorialAfterIntro();}
                if(clock>=2){stage=Stage.Playing;roll.gameObject.SetActive(false);SetReveal(new Rect(0,0,1080,1920));ApplyChannels(1,0);SetFooter(true,0);}
            }
            if(stage==Stage.Finishing)
            {
                if(clock>=0.8f&&!celebration){celebration=true;Sfx(SoundFX.Ribbon);Celebrate();}
                if(clock>=2.1f&&!secondCurl){secondCurl=true;Curl(18,new Vector2(540,116+184),Mathf.PI/2,1.2f,new Vector2(60,200),380,1.8f,new Vector2(2.2f,3),new Vector2(60,86),960,110);}
                if(clock>=2.75f&&!ManualClock&&!winHandled){winHandled=true;RugGameFlow.OnWinPresentationFinished();}
            }
        }
        private void SetReveal(Rect clip,bool clipping=false)
        {
            RugOriginalView.Position(reveal,clip);
            inner.anchoredPosition=new Vector2(540-clip.x,-(596-clip.y));
            RevealClip(clipping);
        }
        // Chỉ bật mask khi thảm đang trải (intro); xong thì tắt để thảm vẽ/batch như bình thường khi chơi.
        // Mask tự cắt theo rect của reveal (SetReveal đổi rect mỗi frame), không cần cập nhật gì thêm.
        private void RevealClip(bool on)
        {
            if(on==revealClipOn)return;
            revealClipOn=on;SetStencilMask(revealMask,on);
        }
        // Graphic trắng cùng object làm vùng stencil, không hiện ra (showMaskGraphic=false).
        private static Mask StencilMask(RectTransform rect)
        {
            var shape=rect.gameObject.AddComponent<RawImage>();shape.texture=Texture2D.whiteTexture;shape.raycastTarget=false;
            var mask=rect.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
            SetStencilMask(mask,false);return mask;
        }
        // Tắt cả Mask lẫn graphic của nó: Mask tắt mà graphic còn bật thì sẽ vẽ ra một hình chữ nhật trắng.
        private static void SetStencilMask(Mask mask,bool on)
        {mask.enabled=on;mask.graphic.enabled=on;}
        private void ApplyChannels(float alpha,float dy)
        {foreach(var t in channels){t.rect.anchoredPosition=t.position+new Vector2(0,-dy);t.group.alpha=alpha;}}
        private void SetFooter(bool show,float dy)
        {
            foreach(var r in new[]{view.selector.rectTransform,view.boosters[0].rectTransform,view.boosters[1].rectTransform})
            {r.gameObject.SetActive(show);r.anchoredPosition=new Vector2(r.anchoredPosition.x,-1615-dy);}
            foreach(int h in new[]{1,2,4}){var t=view.FindUI("Head hit target "+h);if(t!=null)t.gameObject.SetActive(show);}
        }
        public void Apply()
        {
            if(view==null)return;
            if(stage==Stage.Playing){world.anchoredPosition=Vector2.zero;return;}
            string head=Input.HeadWidth==1?"narrow":Input.HeadWidth==4?"wide":"normal";
            if(stage==Stage.Intro)
            {
                var s=RugRollState.Intro(clock);var g=RugRollShape.Evaluate(s);
                bool rolling=clock<1.41f;roll.gameObject.SetActive(rolling&&g.visible);
                if(rolling)
                {
                    SetReveal(g.clip,true);RugOriginalView.Position(rollBody.rectTransform,g.body);RugOriginalView.Position(rollShadow.rectTransform,g.shadow);
                    var c=rollShadow.color;c.a=g.shadowAlpha;rollShadow.color=c;caps.Set(s,g,RugRollCapsGraphic.Hex(level.colors[0]));
                }
                else SetReveal(new Rect(0,0,1080,1920));
                float k=Mathf.Clamp01((clock-0.2f)/0.35f);ApplyChannels(1-(1-k)*(1-k),0);
                foreach(RectTransform b in view.basketsRoot)
                {
                    int column=Mathf.Abs(b.anchoredPosition.x-226.44f)<1?0:Mathf.Abs(b.anchoredPosition.x-540)<1?1:2;
                    float t=Mathf.Clamp01((clock-0.3f-column*0.08f)/0.5f),e=RugPilePose.OutBack(t,1.6f);
                    b.anchoredPosition+=new Vector2(0,-280*(1-e));Group(b).alpha=e;
                }
                float top=IntroToolStartY(),y=top,angle=-0.14f;
                if(clock>=1.3f)y=Mathf.LerpUnclamped(top,560,RugPilePose.OutBack(Mathf.Clamp01((clock-1.3f)/0.55f),1.3f));
                if(clock>=1.41f)
                {
                    float start=Mathf.LerpUnclamped(top,560,RugPilePose.OutBack(0.11f/0.55f,1.3f));
                    float t=Mathf.Clamp01((clock-1.41f)/0.18f),e=1-(1-t)*(1-t);y=Mathf.Lerp(start,490,e);angle=Mathf.Lerp(-0.14f,0.1f,e);
                    if(clock>=1.59f){e=RugPilePose.OutBack(Mathf.Clamp01((clock-1.59f)/0.4f),2.6f);y=Mathf.LerpUnclamped(490,560,e);angle=Mathf.LerpUnclamped(0.1f,-0.14f,e);}
                }
                view.nativeTool.gameObject.SetActive(true);view.nativeTool.Show(head,new Vector2(880,y),angle,true,false,false,totalTime);
                SetFooter(clock>=1.41f,clock<1.41f?0:162.4f*(1-FooterEase(Mathf.Clamp01((clock-1.41f)/0.48f))));
            }
            else
            {
                float t=Mathf.Clamp01(clock/0.3f),e=t*t;
                view.nativeTool.gameObject.SetActive(clock<0.3f);
                if(clock<0.3f)view.nativeTool.Show(head,Vector2.Lerp(flingFrom,new Vector2(1260,flingFrom.y-200),e),Mathf.Lerp(flingAngle,-0.6f,e),true,false,false,simulation.Elapsed+clock);
                float move=RugFiberMotion.InOut(Mathf.Clamp01((clock-0.3f)/0.45f));rug.anchoredPosition=new Vector2(0,-184*move);
                t=Mathf.Clamp01((clock-0.3f)/0.36f);ApplyChannels(1-t*t,140*t*t);
                int index=0;
                foreach(RectTransform b in view.basketsRoot)
                {
                    float p=Mathf.Clamp01((clock-0.3f-index*0.03f)/0.36f),back=2.7f*p*p*p-1.7f*p*p;
                    b.anchoredPosition+=new Vector2(0,-420*back);Group(b).alpha=1-back;index++;
                }
                float age=clock-0.8f;
                flash.gameObject.SetActive(age>=0&&age<0.8f);flash.color=new Color(1,1,1,0.8f*Mathf.Pow(1-Mathf.Clamp01(age/0.8f),2));
                if(age>=0&&age<0.55f){float scale=1+0.05f*(1-RugPilePose.OutBack(age/0.55f,3.2f));inner.localScale=Vector3.one*scale;}else inner.localScale=Vector3.one;
                world.anchoredPosition=age>=0&&age<0.34f?new Vector2(Mathf.Sin(age*70)*12*(1-age/0.34f),0):Vector2.zero;
                sweepMask.gameObject.SetActive(clock>=1.15f);
                if(clock>=1.15f)
                {float p=RugFiberMotion.InOut(Mathf.Clamp01((clock-1.15f)/1.26f));sweep.rectTransform.anchoredPosition=new Vector2(Mathf.Lerp(-320,1280,p),-400);sweep.color=new Color(1,1,1,0.32f);}
                SetFooter(false,0);
            }
        }
        // Dust/Pom/Curl/Star nhận toạ độ thảm, hạt bụi vẽ ở partsRoot (toạ độ màn hình) nên đổi qua RugFit.
        private static Vector2 RugToScreen(Vector2 rug)=>RugFit.ToScreen(rug);
        private void Dust(Vector2 p,int n)
        {effects.Burst("dot",n,RugToScreen(p),new Vector2(16,30),new Vector2(0.35f,0.6f),new Vector2(70,200),-Mathf.PI/2,2.8f,-30,4,6,RugRollCapsGraphic.Hex(16048830),120,0,0,false,1.3f,0.65f);}
        private static float FooterEase(float time)
        {
            // Original footer WebAnimation cubic-bezier(0.2,1.5,0.35,1), with measured height 232.
            float u=time;
            for(int i=0;i<8;i++)
            {
                float v=1-u,x=3*v*v*u*0.2f+3*v*u*u*0.35f+u*u*u;
                float dx=3*v*v*0.2f+6*v*u*(0.35f-0.2f)+3*u*u*(1-0.35f);
                if(dx<0.0001f)break;u=Mathf.Clamp01(u-(x-time)/dx);
            }
            return 3*(1-u)*(1-u)*u*1.5f+3*(1-u)*u*u+u*u*u;
        }
        private Color RandomColor()=>RugRollCapsGraphic.Hex(level.colors[random.Next(level.colors.Length)]);
        private void Pom(int count,Vector2 p,float angle,float spread,Vector2 speed,float gravity,float drag,Vector2 life,Vector2 size,float jx=0)
        {
            for(int i=0;i<count;i++)effects.Burst("fiber-"+level.keys[random.Next(level.keys.Length)]+"-"+random.Next(3),1,RugToScreen(p),size,life,speed,angle,spread,gravity,drag,3,Color.white,jx*RugFit.Scale);
        }
        private void Curl(int count,Vector2 p,float angle,float spread,Vector2 speed,float gravity,float drag,Vector2 life,Vector2 size,float jx,float sway)
        {for(int i=0;i<count;i++)effects.Burst("curl",1,RugToScreen(p),size,life,speed,angle,spread,gravity,drag,5,RandomColor(),jx*RugFit.Scale,0,sway,true);}
        private void Celebrate()
        {
            const float oy=184;
            for(int i=0;i<=10;i++)Dust(new Vector2(60+i*96,996+oy),3);
            for(int j=0;j<=6;j++){Dust(new Vector2(60,196+j*800f/6+oy),2);Dust(new Vector2(1020,196+j*800f/6+oy),2);}
            for(int i=0;i<12;i++)Pom(1,new Vector2(Random(100,980),996+oy),Mathf.PI/2+Random(-0.5f,0.5f)*1.4f,0.2f,new Vector2(380,720),500,2.6f,new Vector2(0.9f,1.3f),new Vector2(56,86));
            foreach(int side in new[]{-1,1})for(int i=0;i<6;i++)Pom(1,new Vector2(side<0?60:1020,196+oy+Random(0,800)),(side<0?Mathf.PI:0)+Random(-0.5f,0.5f)*0.7f,0.2f,new Vector2(420,760),600,2.4f,new Vector2(0.9f,1.3f),new Vector2(56,86));
            Curl(18,new Vector2(540,196+oy),-Mathf.PI/2,1.7f,new Vector2(700,1250),1300,1.1f,new Vector2(1.4f,2),new Vector2(62,90),768,50);
            Pom(10,new Vector2(540,196+oy),-Mathf.PI/2,1.4f,new Vector2(700,1150),1400,1.1f,new Vector2(1.2f,1.7f),new Vector2(60,92),672);
            for(int i=0;i<14;i++)effects.Burst("star",1,RugToScreen(new Vector2(540,596+oy)),new Vector2(36,70),new Vector2(0.6f,1.1f),new Vector2(20,120),-Mathf.PI/2,Mathf.PI*2,0,3,3,Color.white,816*RugFit.Scale,680*RugFit.Scale,0,false,0,1,true);
        }
    }
}
