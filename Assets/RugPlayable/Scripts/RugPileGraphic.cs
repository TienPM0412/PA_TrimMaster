using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace RugMaster
{
    // One native uGUI mesh batches the exact four original tuft pieces for every uncut cell.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RugPileGraphic : MaskableGraphic
    {
        public int previewLevel = 1;
        public bool shadows;
        private RugSimulation simulation;
        private OriginalLayout layout;
        private int lastTrimmed = -1;
        private float lastMeshTime = float.NaN;
        private readonly float[] transformedPoints = new float[8];
        private readonly Dictionary<float[], CachedGeometry> geometryCache = new Dictionary<float[], CachedGeometry>();
        private readonly Dictionary<string, Dictionary<int, OriginalSprite>> cellFrames = new Dictionary<string, Dictionary<int, OriginalSprite>>();
        private sealed class CachedGeometry
        {
            public ClipVertex[] vertices;
        }
        public int RenderedCellCount {get;private set;}
        public int CollapsingCellCount {get;private set;}
        [SerializeField] private Texture2D atlasTexture;
        public override Texture mainTexture => atlasTexture!=null?atlasTexture:Texture2D.whiteTexture;

        public void Bind(RugSimulation model)
        {
            simulation = model;
            maskable=true;
            if(atlasTexture==null)atlasTexture=RugOriginalAssets.Texture("fiber-atlas");
            layout = RugOriginalAssets.Layout(model.Definition.cols, model.Definition.rows);
            lastTrimmed = -1;
            lastMeshTime = float.NaN;
            geometryCache.Clear();
            cellFrames.Clear();
            SetAllDirty();
        }

        private void Update()
        {
            if (simulation == null) return;
            // Render one final settled frame even if a slow frame skips past the animation window.
            if (simulation.TrimmedCount == lastTrimmed && simulation.Elapsed>simulation.TrimVisualUntil &&
                lastMeshTime>simulation.TrimVisualUntil) return;
            if (lastTrimmed == simulation.TrimmedCount && lastMeshTime == simulation.Elapsed) return;
            lastTrimmed = simulation.TrimmedCount;
            lastMeshTime = simulation.Elapsed;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            RenderedCellCount=0;
            CollapsingCellCount=0;
            if (simulation == null)
            {
                var d = RugPlayableAssets.Current.level;
                if (d == null) return;
                simulation=new RugSimulation(d);
                maskable=true;
                layout=RugOriginalAssets.Layout(simulation.Definition.cols,simulation.Definition.rows);
                if(atlasTexture==null)atlasTexture=RugOriginalAssets.Texture("fiber-atlas");
            }
            foreach (var cell in simulation.Cells)
            {
                var pose=RugPilePose.Evaluate(cell,simulation.Elapsed);
                if(cell.Trimmed&&(shadows?pose.shadowAlpha:pose.alpha)<=0)continue;
                RenderedCellCount++;
                if(cell.Trimmed)CollapsingCellCount++;
                var geo = layout.cells[cell.Index];
                var tint=color;tint.a*=shadows?pose.shadowAlpha:pose.alpha;
                if (shadows) AddCached(vh, geo.shadow, RugOriginalAssets.Frame("tshadow"), tint);
                else foreach (var p in geo.pieces)
                {
                    bool transformed=pose.scale!=Vector2.one||pose.rotation!=0;
                    var origin=geo.origin!=null?new Vector2(geo.origin[0],geo.origin[1]):Vector2.zero;
                    if(!cellFrames.TryGetValue(cell.Color,out var frames))
                    {
                        frames=new Dictionary<int, OriginalSprite>();
                        cellFrames.Add(cell.Color,frames);
                    }
                    if(!frames.TryGetValue(p.variant,out var frame))
                        frames[p.variant]=frame=RugOriginalAssets.Frame("pile-"+cell.Color+"-"+p.variant);
                    if(transformed)
                    {
                        RugPilePose.Transform(p.vertices,origin,pose,transformedPoints);
                        Add(vh,transformedPoints,frame,tint,true);
                    }
                    else AddCached(vh,p.vertices,frame,tint);
                }
            }
        }

        private struct ClipVertex {public Vector2 point,uv;public ClipVertex(Vector2 p,Vector2 u){point=p;uv=u;}}
        private static readonly Vector2[] boundary=RoundedBoundary();
        // uGUI populates meshes sequentially on the main thread.
        private static readonly List<ClipVertex> clipInput=new List<ClipVertex>(64);
        private static readonly List<ClipVertex> clipOutput=new List<ClipVertex>(64);
        private void AddCached(VertexHelper vh,float[] points,OriginalSprite frame,Color tint)
        {
            if(!geometryCache.TryGetValue(points,out var cached))
            {
                cached=new CachedGeometry{vertices=Polygon(points,frame,true).ToArray()};
                geometryCache.Add(points,cached);
            }
            Emit(vh,cached.vertices,tint);
        }
        public static void Add(VertexHelper vh,float[] points,OriginalSprite frame,Color color,bool clip=false)
        {
            if(!clip || (Inside(points[0],points[1])&&Inside(points[2],points[3])&&
                Inside(points[4],points[5])&&Inside(points[6],points[7])))
            {
                int start=vh.currentVertCount;var r=frame.rect;
                float l=r[0]/4096f,rr=(r[0]+r[2])/4096f,t=1-r[1]/4096f,b=1-(r[1]+r[3])/4096f;
                vh.AddVert(new Vector3(points[0],-points[1]),color,new Vector2(l,t));
                vh.AddVert(new Vector3(points[2],-points[3]),color,new Vector2(rr,t));
                vh.AddVert(new Vector3(points[4],-points[5]),color,new Vector2(rr,b));
                vh.AddVert(new Vector3(points[6],-points[7]),color,new Vector2(l,b));
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start+2,start+3,start);
                return;
            }
            Emit(vh,Polygon(points,frame,clip),color);
        }
        private static void Emit(VertexHelper vh,IList<ClipVertex> polygon,Color color)
        {
            int i=vh.currentVertCount;
            for(int n=0;n<polygon.Count;n++)
            {var v=polygon[n];vh.AddVert(new Vector3(v.point.x,-v.point.y),color,v.uv);}
            for(int n=1;n<polygon.Count-1;n++)vh.AddTriangle(i,i+n,i+n+1);
        }
        private static List<ClipVertex> Polygon(float[] points,OriginalSprite frame,bool clip)
        {
            var r=frame.rect;
            float l=r[0]/4096f, rr=(r[0]+r[2])/4096f, t=1-r[1]/4096f, b=1-(r[1]+r[3])/4096f;
            var polygon=clipInput;var output=clipOutput;
            polygon.Clear();output.Clear();
            polygon.Add(new ClipVertex(new Vector2(points[0],points[1]),new Vector2(l,t)));
            polygon.Add(new ClipVertex(new Vector2(points[2],points[3]),new Vector2(rr,t)));
            polygon.Add(new ClipVertex(new Vector2(points[4],points[5]),new Vector2(rr,b)));
            polygon.Add(new ClipVertex(new Vector2(points[6],points[7]),new Vector2(l,b)));
            if(clip&&(!Inside(points[0],points[1])||!Inside(points[2],points[3])||!Inside(points[4],points[5])||!Inside(points[6],points[7])))
            {
                for(int edge=0;edge<boundary.Length&&polygon.Count>0;edge++)
                {
                    Vector2 a=boundary[edge],z=boundary[(edge+1)%boundary.Length];
                    output.Clear();var previous=polygon[polygon.Count-1];
                    float pd=Cross(z-a,previous.point-a);
                    foreach(var current in polygon)
                    {
                        float cd=Cross(z-a,current.point-a);
                        if((pd>=0)!=(cd>=0))
                        {
                            float f=pd/(pd-cd);
                            output.Add(new ClipVertex(Vector2.Lerp(previous.point,current.point,f),Vector2.Lerp(previous.uv,current.uv,f)));
                        }
                        if(cd>=0)output.Add(current);
                        previous=current;pd=cd;
                    }
                    var swap=polygon;polygon=output;output=swap;
                }
            }
            return polygon;
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static bool Inside(float x,float y)
        {
            if(x<90||x>990||y<226||y>966)return false;
            float dx=Mathf.Max(Mathf.Max(132-x,0),x-948),dy=Mathf.Max(Mathf.Max(268-y,0),y-924);
            return dx*dx+dy*dy<=42*42;
        }
        private static Vector2[] RoundedBoundary()
        {
            var result=new List<Vector2>();
            Vector2[] centers={new Vector2(948,268),new Vector2(948,924),new Vector2(132,924),new Vector2(132,268)};
            for(int c=0;c<4;c++)for(int s=0;s<=12;s++)
            {float a=(-90+c*90+s*7.5f)*Mathf.Deg2Rad;result.Add(centers[c]+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*42);}
            return result.ToArray();
        }

        public static float[] Quad(float cx,float cy,float width,float height,float rotation)
        {
            float c=Mathf.Cos(rotation),s=Mathf.Sin(rotation);
            float[] q={-width/2,-height/2,width/2,-height/2,width/2,height/2,-width/2,height/2};
            for(int i=0;i<q.Length;i+=2)
            {float x=q[i],y=q[i+1];q[i]=cx+c*x-s*y;q[i+1]=cy+s*x+c*y;}
            return q;
        }
    }

}
