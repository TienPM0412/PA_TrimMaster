using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RugMaster
{
    // Hạt vụn / sao / confetti / vệt sáng ô vừa cạo theo bộ emit/updateParts gốc.
    // Tối ưu cho web (Luna): không còn mỗi hạt một GameObject + RawImage (SetActive, đồng bộ transform, rebatch canvas
    // mỗi hạt); hạt được gom vào RugQuadBatchGraphic theo texture (+ material), mỗi frame dựng lại một mesh / texture.
    // partsRoot có Canvas con riêng (như ống bông) để hạt chuyển động không làm dựng lại batch của thảm / HUD.
    [DefaultExecutionOrder(300)]
    public sealed class RugTrimEffects : MonoBehaviour
    {
        private const int MaxParticles = 320;
        private const string AtlasKey = "fiber-atlas";
        private RugOriginalView view;
        private RugSimulation simulation;
        private OriginalLayout layout;
        private float previousTime;
        private readonly float[] lastPuff={-100,-100,-100};
        private readonly System.Random random=new System.Random(193471);
        private readonly List<Particle> particles=new List<Particle>();
        private readonly List<Sheen> sheens=new List<Sheen>();
        private readonly List<PackBurst> packs=new List<PackBurst>();
        // Mỗi texture (+ material) một batch trong partsRoot; vệt sáng một batch trong sheenRoot (toạ độ thảm).
        private readonly Dictionary<string,RugQuadBatchGraphic> batches=new Dictionary<string,RugQuadBatchGraphic>();
        private readonly List<RugQuadBatchGraphic> batchList=new List<RugQuadBatchGraphic>();
        private RugQuadBatchGraphic sheenBatch;
        private readonly Dictionary<string,Rect> atlasUv=new Dictionary<string,Rect>();
        private float lastPack=-100;
        private int combo;
        private bool dirty;
        private sealed class PackBurst{public int column,combo;public string color;public float at;}
        public int ActiveParticleCount{get;private set;}
        public float ExternalTime=-1;
        private sealed class Particle
        {
            public RugQuadBatchGraphic batch;
            public Rect uv;
            public bool active,twinkle;
            public Vector2 position,velocity,size;
            public float age,life,gravity,drag,spin,rotation;
            public Color tint;
            public float grow,sway,phase,alpha0=1;
            public bool flip;
        }
        private sealed class Sheen{public bool active;public float age;public Vector2 center,size;}
        private float R(float min,float max)=>min+(float)random.NextDouble()*(max-min);
        public void Initialize(RugOriginalView owner,RugSimulation model){view=owner;Bind(model);}
        public void Bind(RugSimulation model)
        {
            if(simulation!=null){simulation.OnTrim-=OnTrim;simulation.OnFiberLanded-=OnLanded;simulation.OnBasketCompleted-=OnPacked;}
            simulation=model;previousTime=model.Elapsed;
            ExternalTime=-1;
            layout=RugOriginalAssets.Layout(model.Definition.cols,model.Definition.rows);
            simulation.OnTrim+=OnTrim;simulation.OnFiberLanded+=OnLanded;
            simulation.OnBasketCompleted+=OnPacked;packs.Clear();lastPack=-100;combo=0;
            foreach(var p in particles)p.active=false;
            foreach(var s in sheens)s.active=false;
            foreach(var b in batchList)if(b!=null)b.Clear();
            if(sheenBatch!=null)sheenBatch.Clear();
        }

        // ---- Batch theo texture ----
        private RugQuadBatchGraphic Batch(string key,Texture texture,Material material)
        {
            if(batches.TryGetValue(key,out var batch)&&batch!=null)return batch;
            var root=view.partsRoot;
            var layerCanvas=root.GetComponent<Canvas>();
            if(layerCanvas==null){layerCanvas=root.gameObject.AddComponent<Canvas>();layerCanvas.overrideSorting=false;}
            batch=RugOriginalView.Rect(root,"Particles "+key,0,0,1080,1920).gameObject.AddComponent<RugQuadBatchGraphic>();
            batch.texture=texture;batch.raycastTarget=false;batch.maskable=false;
            if(material!=null)batch.material=material;
            batches[key]=batch;batchList.Add(batch);
            return batch;
        }
        private RugQuadBatchGraphic SheenBatch()
        {
            if(sheenBatch!=null)return sheenBatch;
            sheenBatch=RugOriginalView.Rect(view.sheenRoot,"Freshly shorn pile glints",0,0,1080,1920).gameObject.AddComponent<RugQuadBatchGraphic>();
            sheenBatch.texture=RugOriginalAssets.Texture("soft");sheenBatch.raycastTarget=false;
            return sheenBatch;
        }
        private Rect AtlasUv(string frame)
        {
            if(atlasUv.TryGetValue(frame,out var uv))return uv;
            var r=RugOriginalAssets.Frame(frame).rect;
            uv=new Rect(r[0]/4096,1-(r[1]+r[3])/4096,r[2]/4096,r[3]/4096);
            atlasUv[frame]=uv;return uv;
        }

        private void OnTrim(Cell cell)
        {
            var geometry=layout.cells[cell.Index];float cw=892f/simulation.Definition.cols,ch=732f/simulation.Definition.rows;
            for(int i=0;i<2;i++)
            {
                var piece=geometry.pieces[random.Next(4)];var q=piece.vertices;
                var position=new Vector2((q[0]+q[2]+q[4]+q[6])/4,(q[1]+q[3]+q[5]+q[7])/4);
                // Hạt vụn vẽ ở partsRoot (toạ độ màn hình): đổi từ toạ độ thảm qua RugFit.
                Spawn("pile-"+cell.Color+"-"+piece.variant,RugFit.ToScreen(position),R(0.7f,1)*Mathf.Max(cw,ch)*0.7f*RugFit.Scale,
                    R(0.3f,0.5f),R(120,280),2.4f,1000,1,10,false);
            }
            // center ở toạ độ thảm: sheenRoot nằm trong nhóm thảm đã thu nhỏ.
            var center=new Vector2(94+(cell.X+0.5f)*cw,230+(cell.Y+0.5f)*ch);
            if(R(0,1)<0.05f)Spawn("star",RugFit.ToScreen(center),R(20,34),R(0.25f,0.4f),R(0,40),Mathf.PI*2,0,3,3,true);
            Sheen sheen=null;
            for(int i=0;i<sheens.Count;i++)if(!sheens[i].active){sheen=sheens[i];break;}
            if(sheen==null){sheen=new Sheen();sheens.Add(sheen);}
            sheen.active=true;sheen.age=0;sheen.center=center;sheen.size=new Vector2(cw,ch)*2.2f;
            dirty=true;
        }
        private void OnLanded(Fiber fiber)
        {
            if(simulation.Elapsed-lastPuff[fiber.Target.Column]<0.08f)return;
            lastPuff[fiber.Target.Column]=simulation.Elapsed;
            for(int i=0;i<2;i++)Spawn("fiber-"+fiber.Color+"-"+random.Next(4),fiber.CaptureTo-new Vector2(0,10),
                R(10,16),R(0.3f,0.45f),R(80,180),1.6f,700,1.5f,8,false);
        }
        private void OnPacked(Basket basket)
        {
            if(simulation.Front(basket.Column)!=basket)return;
            combo=simulation.Elapsed-lastPack<2.6f?combo+1:0;lastPack=simulation.Elapsed;
            packs.Add(new PackBurst{column=basket.Column,color=basket.Color,combo=combo,
                at=(ExternalTime>=0?ExternalTime:simulation.Elapsed)+0.24f});
        }
        private void EmitPack(PackBurst pack)
        {
            var palette=RugPresentationAssets.Level(simulation.Definition.index);int index=System.Array.IndexOf(palette.keys,pack.color);
            var fill=RugRollCapsGraphic.Hex(palette.colors[Mathf.Max(0,index)]);
            var colors=new[]{fill,RugRollCapsGraphic.Hex(15909198),Color.white};
            for(int i=0;i<24+pack.combo*5;i++)Burst("conf",1,new Vector2(RugSimulation.BasketX[pack.column],1421),
                new Vector2(10,16),new Vector2(0.8f,1.2f),new Vector2(350,750),-Mathf.PI/2,1.8f,1100,1.4f,12,colors[random.Next(3)],0,0,30,true);
            Burst("star",5+pack.combo,new Vector2(RugSimulation.BasketX[pack.column],1471),new Vector2(28,54),
                new Vector2(0.45f,0.8f),new Vector2(60,260),-Mathf.PI/2,Mathf.PI*2,0,3,3,Color.white,200,120,0,false,0,1,true);
        }
        private Particle Spawn(string frame,Vector2 position,float size,float life,float speed,float spread,float gravity,float drag,float spin,bool twinkle)
        {
            Particle p=null;
            for(int i=0;i<particles.Count;i++)if(!particles[i].active){p=particles[i];break;}
            if(p==null){if(particles.Count>=MaxParticles)return null;p=new Particle();particles.Add(p);}
            p.active=true;p.age=0;p.life=life;p.position=position;
            float angle=-Mathf.PI/2+R(-0.5f,0.5f)*spread;
            p.velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed;p.gravity=gravity;p.drag=drag;
            p.rotation=R(0,6);p.spin=R(-0.5f,0.5f)*spin;p.twinkle=twinkle;p.tint=Color.white;
            p.grow=0;p.sway=0;p.phase=R(0,6);p.flip=false;p.alpha0=1;
            if(!frame.StartsWith("pile-")&&!frame.StartsWith("fiber-"))
            {
                var texture=RugPresentationAssets.Texture(frame);
                bool star=frame=="star";
                p.batch=Batch(frame,texture,star?RugPresentationAssets.Additive:null);
                p.uv=new Rect(0,0,1,1);
                p.size=new Vector2(size,size*texture.height/texture.width);
                if(star)p.tint=random.Next(2)==0?(Color)new Color32(255,244,194,255):Color.white;
            }
            else
            {
                p.batch=Batch(AtlasKey,RugOriginalAssets.Texture(AtlasKey),null);
                p.uv=AtlasUv(frame);
                p.size=new Vector2(size,size*p.uv.height/p.uv.width);
            }
            dirty=true;
            return p;
        }
        public void Burst(string frame,int count,Vector2 center,Vector2 sizes,Vector2 life,Vector2 speed,float angle,float spread,
            float gravity,float drag,float spin,Color tint,float jx=0,float jy=0,float sway=0,bool flip=false,
            float grow=0,float alpha=1,bool twinkle=false)
        {
            for(int i=0;i<count;i++)
            {
                var p=Spawn(frame,center+new Vector2(R(-0.5f,0.5f)*jx,R(-0.5f,0.5f)*jy),R(sizes.x,sizes.y),R(life.x,life.y),
                    R(speed.x,speed.y),spread,gravity,drag,spin,twinkle);
                if(p==null)return;
                float a=angle+R(-0.5f,0.5f)*spread;p.velocity=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*R(speed.x,speed.y);
                p.tint=tint;p.grow=grow;p.sway=sway;p.flip=flip;p.alpha0=alpha;
            }
        }
        // Giống RawImage cũ: sizeDelta theo twinkle/grow, localScale.x lật, xoay -rotation, alpha mờ dần 30% cuối đời.
        private static void Draw(Particle p)
        {
            float k=p.age/p.life;
            var size=p.size*(p.twinkle?Mathf.Sin(Mathf.PI*k):1+p.grow*k);
            var c=p.tint;c.a=p.alpha0*(k<0.7f?1:Mathf.Max(0,(1-k)/0.3f));
            p.batch.Add(p.position,size,-p.rotation,p.flip?Mathf.Cos(p.age*9+p.phase):1,p.uv,c);
        }
        private void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(simulation==null)return;
            float now=ExternalTime>=0?ExternalTime:simulation.Elapsed,dt=Mathf.Max(0,now-previousTime);previousTime=now;
            for(int i=packs.Count-1;i>=0;i--)if(now>=packs[i].at){EmitPack(packs[i]);packs.RemoveAt(i);}
            // Không có hạt / vệt sáng nào (đa số thời gian lúc không cạo): khỏi dựng mesh.
            if(!dirty)return;
            for(int i=0;i<batchList.Count;i++)if(batchList[i]!=null)batchList[i].BeginFrame();
            ActiveParticleCount=0;
            for(int i=0;i<particles.Count;i++)
            {
                var p=particles[i];
                if(!p.active)continue;p.age+=dt;
                if(p.age>=p.life){p.active=false;continue;}
                float drag=Mathf.Max(0,1-p.drag*dt);p.velocity.x*=drag;p.velocity.y=p.velocity.y*drag+p.gravity*dt;
                p.position+=p.velocity*dt+new Vector2(p.sway==0?0:Mathf.Sin(p.age*6+p.phase)*p.sway*dt,0);
                p.rotation+=p.spin*dt;Draw(p);ActiveParticleCount++;
            }
            int activeSheens=0;
            if(sheens.Count>0)
            {
                var batch=SheenBatch();batch.BeginFrame();
                for(int i=0;i<sheens.Count;i++)
                {
                    var s=sheens[i];
                    if(!s.active)continue;s.age+=dt;
                    if(s.age>=0.45f){s.active=false;continue;}
                    batch.Add(s.center,s.size,0,1,new Rect(0,0,1,1),new Color(1,1,1,0.22f*Mathf.Pow(1-s.age/0.45f,2)));
                    activeSheens++;
                }
                batch.EndFrame();
            }
            for(int i=0;i<batchList.Count;i++)if(batchList[i]!=null)batchList[i].EndFrame();
            // Frame này vừa xoá hạt cuối cùng (EndFrame đã dựng mesh rỗng) -> từ frame sau nghỉ.
            dirty=ActiveParticleCount>0||activeSheens>0||packs.Count>0;
        }
        private void OnDestroy(){if(simulation!=null){simulation.OnTrim-=OnTrim;simulation.OnFiberLanded-=OnLanded;simulation.OnBasketCompleted-=OnPacked;}}
    }
}
