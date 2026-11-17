using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RugMaster
{
    [DisallowMultipleComponent]
    public sealed class RugOriginalView : MonoBehaviour
    {
        public Canvas canvas;
        public RectTransform content, marksRoot, basketsRoot, fibersRoot;
        public RectTransform flyersRoot,dropsRoot,partsRoot,sheenRoot;
        public RugToolView nativeTool;
        // Container thu nhỏ/dời thảm và dao cắt vào trong ống bao quanh (RugFit), lưu sẵn trong scene GamePlay.
        public RectTransform rugFit, toolFit;
        // Bố cục theo màn hình (EnsureScreenLayout): nền gỗ phủ kín màn hình, HUD trên/dưới bám mép vùng an toàn
        // (Crystal.SafeArea trên safeArea), modal/tutorial nằm trên cùng.
        public RectTransform safeArea, topBar, bottomBar, overlayRoot;
        // Nút + popup Setting copy từ scene Home (menu Rug Master/Copy Home Setting Into GamePlay); RateUs đổi thành Quit về Home.
        
        
        private RugTrimEffects trimEffects;
        private RugPresentation presentation;
        public bool PresentationBlocksInput=>presentation!=null&&presentation.BlocksInput;
        public RugPresentation Presentation=>presentation;
        public RawImage wood, basePile, shade, channel, lips, header, selector, tool, meter;
        
        [Header("Basket numbers")]
        [SerializeField] private Font basketNumberFont;
        [SerializeField] private Color basketNumberLightColor=new Color32(255,255,255,255);
        [SerializeField] private Color basketNumberDarkColor=new Color32(59,42,30,255);
        [SerializeField] private int basketNumberFontSize=72;
        public RawImage[] boosters = new RawImage[2];
        public RugPileGraphic tuftMesh, shadowMesh;
        // Ảnh thảm nguyên lông bake sẵn (Rug Playable/Bake Assets): ô đã cạo bị khoét qua mask = shadeTexture.
        public RawImage pileImage;
        private Material pileMaterial;
        public const string PileImageName="Uncut pile image";
        public static string PileImageKey(int sourceLevel)=>$"pile-full-{sourceLevel:000}";
        public const string PileMaskMaterial="RugMaster/OriginalUI/PileMask";
        public RectTransform modalRoot;
        [SerializeField] private Font fredoka;
        // false khi dựng/bind trong Editor (Rebuild Original UI Scene): không được chạm tới các manager.
        private bool initialized;
        private RugSimulation simulation;
        private Texture2D shadeTexture;
        private Color32[] shadePixels;
        private readonly HashSet<int> liveBasketIds=new HashSet<int>();
        private readonly Dictionary<int,BasketVisual> basketViews=new Dictionary<int,BasketVisual>();
        private RugObstacleMarks obstacleMarks;
        private RugLeftoverHint leftoverHint;
        private readonly List<FiberVisual> fiberViews=new List<FiberVisual>();
        private readonly Dictionary<(string,int),FlightSprite> flightSprites=new Dictionary<(string,int),FlightSprite>();
        public sealed class FlightSprite {public Rect uv;public Vector2 size;}
        private RugFiberTrail fiberTrail;
        public RugFiberBatchGraphic pipeFiberBatch, dropFiberBatch, flyingFiberBatch;
        private sealed class FiberVisual {public int id=-1;public FlightSprite sprite;}
        private int lastRearranges=-1,lastSweeps=-1;
        private int lastTrimmed=-1,lastMeter=-1,lastHead=-1,sourceLevel=1;
        private bool settingsOpen;
        private float wonAt=-1;
        // Kẹt đã mở popup Out of Space / thắng đã báo RugGameFlow (không mở lại mỗi frame).
        private bool jamPopupShown,winHandled;
        // Tutorial level 1 (spotlight theo từng bước), xem RugTutorial.
        
        public bool TutorialBlocksInput=>false;
        private sealed class BasketVisual
        {
            public RectTransform root;
            public RawImage back,front,queue,queueShadow,queueTag,bow,seal;
            public Text number;
            public RugHeapGraphic heap;
            public int count=-1,position=-1,tied;
            // Anim tiến lên hàng trên: vị trí/scale lúc bắt đầu, thời điểm bắt đầu (simulation.Elapsed, -1 = không chạy),
            // và y/scale đang hiển thị (để anim mới nối tiếp anim đang chạy dở).
            public float moveFromX,moveFromY,moveFromScale=1,moveStart=-1,shownX,shownY,shownScale=1;
            public int column=-1;
            public bool revealed,completing,lastInQueue;
        }
        private static readonly Vector2 FullSize=new Vector2(1080,1920);
        public static readonly Vector2 MeterAnchor=new Vector2(412,369);
        // Ảnh rổ màu 360x350: thân rổ x 60.0.299, đáy y 260 (pivot = giữa đáy thân).
        private const float QueueBasketScale=610f*(165*0.997f/1108/0.55f)/240f;
        private static readonly Vector2 QueueBasketPivot=new Vector2(179.5f/360,260f/350);
        // Rổ con -> rổ hàng đầu: thân rổ con rộng 240*QueueBasketScale, rổ hàng đầu 303 (cùng gốc ở giữa đáy thân rổ).
        private const float QueueToFrontScale=240f*QueueBasketScale/303f;
        // Nhỏ hơn thời gian cột bận sau khi rổ trước xong (0.42s) để rổ vào chỗ trước khi nhận bông.
        private const float BasketAdvanceDuration=0.38f;

        public void Initialize()
        {
            initialized=true;
            BuildHierarchy();
            EnsureRugFit();
            EnsureScreenLayout();
            ClearChildren(basketsRoot);
            ClearChildren(marksRoot);
            if (FindObjectOfType<EventSystem>()==null)
                new GameObject("UI Event System",typeof(EventSystem),typeof(StandaloneInputModule));
            var levels=LevelManager.Ins;
            Bind(levels.Simulation,levels.CurrentLevel);
            BindControls();
            trimEffects=GetComponent<RugTrimEffects>();
            if(trimEffects==null)trimEffects=gameObject.AddComponent<RugTrimEffects>();
            trimEffects.Initialize(this,levels.Simulation);
            presentation=GetComponent<RugPresentation>();
            if(presentation==null)presentation=gameObject.AddComponent<RugPresentation>();
            presentation.Initialize(this,trimEffects);

            EventManager.AddListener<OnLevelLoadedEvent>(OnLevelLoaded);
            EventManager.AddListener<OnBoosterUnavailableEvent>(OnBoosterUnavailable);
            RugGameFlow.Completed+=ShowWinPanel;
        }
        private void OnLevelLoaded(OnLevelLoadedEvent e)=>Bind(e.simulation,e.level);
        // Màn diễn thắng xong -> bật panel thắng có sẵn trong scene (logo bấm vào mở store).
        public RugWinPanel winPanel;
        private void ShowWinPanel(){EnsureWinPanel(false);if(winPanel!=null)winPanel.Show();}
        // Panel nằm trong overlayRoot (trên HUD, ngoài modalRoot vì ClearModal xoá hết con của modalRoot).
        // Scene đã có panel thì dùng lại; rebuild=true (menu Editor) thì dựng lại từ đầu.
        public void EnsureWinPanel(bool rebuild)
        {
            var parent=overlayRoot!=null?overlayRoot:content;
            if(parent==null)return;
            if(!rebuild&&winPanel==null){var found=parent.Find(RugWinPanel.PanelName);if(found!=null)winPanel=found.GetComponent<RugWinPanel>();}
            if(rebuild||winPanel==null)winPanel=RugWinPanel.Build(parent,RugPlayableAssets.Load<Texture2D>(RugWinPanel.TitleKey));
            else if(winPanel.playButton==null||winPanel.playButtonLayout<RugWinPanel.PlayButtonLayout)RugWinPanel.BuildPlayButton(winPanel);
            winPanel.transform.SetAsLastSibling();
        }

        public void BuildHierarchy()
        {
            _=RugOriginalAssets.Manifest;
            if(canvas!=null)return;
            var root=new GameObject("Original UI - Canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false);
            canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=10;
            canvas.pixelPerfect=false;
            content=Rect(root.transform,"Reference frame 1080 x 1920",0,0,1080,1920);
            content.anchorMin=content.anchorMax=new Vector2(0.5f,0.5f);content.pivot=new Vector2(0.5f,0.5f);content.anchoredPosition=Vector2.zero;
            wood=Image(content,"Wood table",new Rect(0,0,1080,1920),RugOriginalAssets.Texture("wood"));
            Image(content,"Rug shadow",new Rect(0,136,1080,940),RugOriginalAssets.Texture("rugShadow"));
            basePile=Image(content,"Permanent shorn pile",new Rect(60,196,960,800));
            shade=Image(content,"Uncut ambient occlusion",Constants.RugRect);
            shade.color=new Color(1,1,1,0.36f);
            var maskRect=Rect(content,"Rounded pile clipping",90,226,900,740);
            // Tuft quads are clipped to the original rounded rectangle on the CPU.
            shadowMesh=Pile(maskRect,"Uncut tuft shadows",true);
            tuftMesh=Pile(maskRect,"Uncut four-piece pom-pom mesh",false);
            sheenRoot=Rect(content,"Fresh trim sheen",0,0,1080,1920);
            Image(content,"Original braided binding",new Rect(60,196,960,800),RugOriginalAssets.Texture("binding"));
            marksRoot=Rect(content,"Ribbon tabs and clip groups",0,0,1080,1920);
            channel=Image(content,"Collection channel",new Rect(-48.93f,837.62f,0,0));
            SetLocalSprite(channel,"channel-back",Vector2.zero);
            // Original source draws a soft rounded shadow behind the physical tube.
            var channelShadow=Image(content,"Channel table shadow",new Rect(12.71f,1062,1003.66f,191.62f));
            channelShadow.color=new Color(0.35f,0.22f,0.08f,0.14f);
            channelShadow.texture=Texture2D.whiteTexture;channelShadow.gameObject.SetActive(false);
            fibersRoot=Rect(content,"Fibers inside collection channel",0,0,1080,1920);
            dropsRoot=Rect(content,"Fibers behind outlet lips",0,0,1080,1920);
            lips=Image(content,"Channel front lips",new Rect(0,0,0,0));
            SetLocalSprite(lips,"channel-outlet-lips",Vector2.zero);
            basketsRoot=Rect(content,"Basket columns",0,0,1080,1920);
            flyersRoot=Rect(content,"Flying and landing fiber clusters",0,0,1080,1920);
            meter=Image(content,"Collection load meter",new Rect(0,0,500,72));
            var intake=Image(content,"Intake front lip",new Rect());SetLocalSprite(intake,"channel-intake-lip",Vector2.zero);
            nativeTool=Rect(content,"Dynamic trimmer assembly",0,0,1080,1920).gameObject.AddComponent<RugToolView>();
            nativeTool.Build();tool=nativeTool.body;
            partsRoot=Rect(content,"Trim offcuts and sparkles",0,0,1080,1920);
            header=Image(content,"Level pill",new Rect(300,10,480,160));


            selector=Image(content,"Three trimmer heads",new Rect(0,1615,700,305));
            for(int i=0;i<3;i++)
            {
                int width=new[]{1,2,4}[i];
                var hr=RugOriginalAssets.Dom.heads[i].rect;
                var hit=Image(content,"Head hit target "+width,ArrayRect(hr));
                hit.color=Color.clear;AddButton(hit.rectTransform,()=>ControllerManager.Ins.SetHead(width));
            }
            boosters[0]=Image(content,"Rearrange booster",new Rect(674,1615,220,305));
            boosters[1]=Image(content,"Sweep assist booster",new Rect(860,1615,220,305));
            AddButton(boosters[0].rectTransform,()=>BoosterManager.Ins.UseBooster(false));
            AddButton(boosters[1].rectTransform,()=>BoosterManager.Ins.UseBooster(true));
            modalRoot=Rect(content,"Modal overlays",0,0,1080,1920);
            fredoka=RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            EnsureRugFit();
            EnsureScreenLayout();
        }

        // Chỗ đặt các lớp gameplay trong content (ngay sau nền gỗ nếu nền còn nằm trong content).
        public int GameplayBaseIndex=>wood!=null&&wood.transform.parent==content?wood.transform.GetSiblingIndex()+1:0;

        // Tìm nút/HUD theo tên trong content và hai thanh HUD trên/dưới.
        public Transform FindUI(string name)
        {
            foreach(var parent in new Transform[]{content,topBar,bottomBar})
            {if(parent==null)continue;var t=parent.Find(name);if(t!=null)return t;}
            return null;
        }

        // Khung gameplay (content 1080x1920) vẫn căn giữa và vừa màn hình như cũ; phần còn lại:
        //  - Nền gỗ: con trực tiếp của canvas, giãn kín màn hình (uv kiểu cover).
        //  - "Safe area HUD" (Crystal.SafeArea) giãn theo vùng an toàn; trong đó "Top HUD" bám mép trên
        //    (Level, Settings) và "Bottom HUD" bám mép dưới (chọn đầu dao, booster). Hai thanh này là khung 1080x1920
        //    neo mép trên/dưới nên các nút giữ nguyên toạ độ thiết kế cũ (y 10.0.170 và 1615.0.1920).
        //  - Modal/tutorial ở lớp trên cùng để phủ cả HUD.
        // Gọi lại nhiều lần không sao; menu Rug Master/Fit Rug Inside Pipe lưu kết quả vào scene.
        public void EnsureScreenLayout()
        {
            if(canvas==null||content==null)return;
            var root=(RectTransform)canvas.transform;
            var scaler=canvas.GetComponent<CanvasScaler>();
            if(scaler==null)scaler=canvas.gameObject.AddComponent<CanvasScaler>();
            // Expand = min(w/1080, h/1920): khung 1080x1920 luôn nằm trọn trong màn hình (giống ControllerManager.UpdateLayout).
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=FullSize;
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            if(wood!=null)
            {
                if(wood.transform.parent!=root)wood.transform.SetParent(root,false);
                Stretch(wood.rectTransform);
            }
            // Không được giãn lại safeArea sau khi SafeArea đã chạy: nó chỉ đặt anchor khi vùng an toàn/màn hình đổi,
            // giãn đè lên thì HUD sẽ nằm dưới tai thỏ cho tới khi xoay màn hình.
            if(safeArea==null)
            {
                safeArea=Rect(root,"Safe area HUD",0,0,0,0);Stretch(safeArea);
                safeArea.gameObject.AddComponent<Crystal.SafeArea>();
            }
            else if(safeArea.GetComponent<Crystal.SafeArea>()==null)
            {Stretch(safeArea);safeArea.gameObject.AddComponent<Crystal.SafeArea>();}
            if(topBar==null)topBar=Rect(safeArea,"Top HUD (pinned to top)",0,0,1080,1920);
            Pin(topBar,1);
            if(bottomBar==null)bottomBar=Rect(safeArea,"Bottom HUD (pinned to bottom)",0,0,1080,1920);
            Pin(bottomBar,0);
            foreach(var node in new[]{header!=null?header.transform:null,content.Find("Settings button")})
                if(node!=null&&node.parent!=topBar)node.SetParent(topBar,false);
            var bottom=new List<Transform>{selector!=null?selector.transform:null};
            foreach(int h in new[]{1,2,4})bottom.Add(content.Find("Head hit target "+h));
            foreach(var b in boosters)bottom.Add(b!=null?b.transform:null);
            foreach(var node in bottom)if(node!=null&&node.parent!=bottomBar)node.SetParent(bottomBar,false);
            if(overlayRoot==null)overlayRoot=Rect(root,"Modal and tutorial overlays",0,0,1080,1920);
            overlayRoot.anchorMin=overlayRoot.anchorMax=overlayRoot.pivot=new Vector2(0.5f,0.5f);
            overlayRoot.anchoredPosition=Vector2.zero;overlayRoot.sizeDelta=FullSize;
            if(modalRoot!=null&&modalRoot.parent!=overlayRoot)modalRoot.SetParent(overlayRoot,false);
            // Thứ tự vẽ: nền -> gameplay -> HUD -> modal/tutorial.
            if(wood!=null)wood.transform.SetSiblingIndex(0);
            content.SetSiblingIndex(wood!=null?1:0);
            safeArea.SetAsLastSibling();overlayRoot.SetAsLastSibling();
            // Popup Setting (copy từ Home) luôn trên cùng: nền tối của nó phủ và chặn bấm cả HUD (setting, đầu dao, booster).
            FitWood();
        }
        private static void Stretch(RectTransform r)
        {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(0.5f,0.5f);r.offsetMin=r.offsetMax=Vector2.zero;r.localScale=Vector3.one;}
        // Khung 1080x1920 neo mép trên (edgeY=1) hoặc mép dưới (edgeY=0) của cha, căn giữa ngang.
        private static void Pin(RectTransform r,float edgeY)
        {r.anchorMin=r.anchorMax=r.pivot=new Vector2(0.5f,edgeY);r.anchoredPosition=Vector2.zero;r.sizeDelta=FullSize;r.localScale=Vector3.one;}
        // CSS background-size: cover cho texture gỗ vuông theo tỉ lệ thật của nền.
        private void FitWood()
        {
            if(wood==null)return;
            var size=wood.rectTransform.rect.size;
            float aspect=size.x>0&&size.y>0?size.x/size.y:FullSize.x/FullSize.y;
            wood.uvRect=aspect<=1?new Rect((1-aspect)*0.5f,0,aspect,1):new Rect(0,(1-1/aspect)*0.5f,1,1/aspect);
        }

        // Đưa thảm vào trong ống bao quanh ngay trong hierarchy (chạy được cả trong Editor, menu
        // Rug Master/Fit Rug Inside Pipe lưu kết quả vào scene): các phần của thảm nằm trong "Rug fit inside pipe",
        // dao cắt trong "Trimmer fit inside pipe" (thu nhỏ RugFit.Scale, dời RugFit.Origin), thanh load ở MeterAnchor.
        // Gọi lại nhiều lần không sao: chỉ dời những gì chưa đúng chỗ và cập nhật vị trí/tỉ lệ theo RugFit.
        public void EnsureRugFit()
        {
            if(content==null)return;
            if(rugFit==null)
            {
                rugFit=Rect(content,"Rug fit inside pipe",0,0,1080,1920);
                rugFit.SetSiblingIndex(GameplayBaseIndex);
            }
            var rugShadow=content.Find("Rug shadow");var binding=content.Find("Original braided binding");
            var clipping=tuftMesh!=null?tuftMesh.transform.parent:null;
            // Thứ tự vẽ giữ như cũ: bóng -> lông đã cắt -> AO -> lông chưa cắt -> vệt sáng -> viền -> ruy băng/kẹp.
            foreach(var node in new Transform[]{rugShadow,basePile!=null?basePile.transform:null,shade!=null?shade.transform:null,
                clipping,sheenRoot,binding,marksRoot})
                if(node!=null&&node.parent!=rugFit)node.SetParent(rugFit,false);
            if(toolFit==null&&nativeTool!=null)
            {
                int index=nativeTool.transform.GetSiblingIndex();
                toolFit=Rect(nativeTool.transform.parent,"Trimmer fit inside pipe",0,0,1080,1920);
                toolFit.SetSiblingIndex(index);
            }
            if(nativeTool!=null&&toolFit!=null&&nativeTool.transform.parent!=toolFit)nativeTool.transform.SetParent(toolFit,false);
            foreach(var fit in new[]{rugFit,toolFit})
            {
                if(fit==null)continue;
                Position(fit,new Rect(RugFit.Origin.x,RugFit.Origin.y,1080,1920));
                fit.localScale=new Vector3(RugFit.Scale,RugFit.Scale,1);
            }
            if(meter!=null)SetMeter(Mathf.Max(0,lastMeter));
            // Ống bao quanh: đúng kích thước ảnh, góc trên-trái tại (PipeImageLeft, PipeImageTop) như simulation tính đường đi của sợi.
            if(channel!=null&&channel.texture!=null)
                Position(channel.rectTransform,new Rect(RugSimulation.PipeImageLeft,RugSimulation.PipeImageTop,1043f,1071f));
            PutFibersBehindPipe();
        }

        // Bông chạy trong ống và bông đang rơi qua miệng xả (dropsRoot) nằm dưới ảnh ống: ống thuỷ tinh trong mờ ở giữa,
        // đặc ở thành nên bông nhìn như đang ở bên trong. Ra khỏi mép miệng xả (LipBottom) bông mới lên flyersRoot phía trên.
        public void PutFibersBehindPipe()
        {
            foreach(var layer in new[]{fibersRoot,dropsRoot})
            {
                if(channel==null||layer==null||layer.parent!=channel.transform.parent)continue;
                int pipe=channel.transform.GetSiblingIndex();
                if(layer.GetSiblingIndex()>pipe)layer.SetSiblingIndex(pipe);
            }
        }

        private void BindControls()
        {
            for(int i=0;i<3;i++){int width=new[]{1,2,4}[i];BindButton(FindUI("Head hit target "+width),()=>{SoundManager.Ins.PlayButton();ControllerManager.Ins.SetHead(width);});}
            BindButton(boosters[0].transform,()=>PressBooster(0));
            BindButton(boosters[1].transform,()=>PressBooster(1));
        }
        // ---- Booster: nảy nút khi bấm, có tác dụng thì nảy mạnh hơn; không tác dụng thì lắc nhẹ + thông báo lý do ----
        private readonly float[] boosterPressAt={-100,-100};
        private readonly bool[] boosterPressUsed=new bool[2];
        private RectTransform toast;
        private CanvasGroup toastGroup;
        private Text toastText;
        private float toastShownAt=-100;
        private const float ToastDuration=1.8f;
        /// <summary>Bấm booster (0 = Rearrange, 1 = Sweep). Hết lượt thì BoosterManager mở popup Buy Booster; mua xong popup gọi lại hàm này.</summary>
        public void PressBooster(int index)
        {
            bool sweep=index==1;
            boosterPressAt[index]=Time.unscaledTime;
            // Hết lượt miễn phí: bấm = mở store, đổi lại +1 lượt cho booster này (bấm lần nữa mới dùng).
            if(BoosterManager.Ins.Count(sweep)<=0)
            {
                boosterPressUsed[index]=true;SoundManager.Ins.PlayButton();
                BoosterManager.Ins.GetMoreViaInstall(sweep);
                return;
            }
            bool used=BoosterManager.Ins.UseBooster(sweep);
            boosterPressUsed[index]=used;
            if(used)SoundManager.Ins.PlayButton();
        }
        private void OnBoosterUnavailable(OnBoosterUnavailableEvent e)=>ShowToast(e.message);

        // ---- Nút booster kiểu Pixeland (prefab DevVinh/Prefab/Booster/Booster_Launcher): ô vuông bo góc viền vàng (BtnOn),
        // mặt xanh (Core) + icon (Ref), góc dưới phải là huy hiệu xanh lá đếm lượt (Info/Amount) hoặc dấu + vàng khi hết (AddIng).
        // boosters[i] chỉ còn là vùng bấm trong suốt 220x305 (giữ toạ độ thiết kế cũ); hình vẽ nằm trong con "Pixeland skin".
        // Ảnh ở Resources/RugMaster/Art/Booster (copy từ Pixeland0626 _UI/Ingame + _UI/NewShop).
        private const string BoosterSkinName="Pixeland skin";
        // Hai ô bấm (x 674 và 860, rộng 220) chồng nhau 34px: tâm cách nhau 186px. Cỡ 0.85 thì khung 178px
        // + huy hiệu (lệch phải 80) của Rearrange không đè lên Sweep; Rearrange lùi trái 12px: hai khung cách nhau ~19.5, bằng khoảng thẻ chọn đầu dao - Rearrange.
        private const float BoosterSkinScale=0.85f;
        private static readonly Vector2[] BoosterSkinOffset={new Vector2(-12,14),new Vector2(0,14)};
        private readonly Text[] boosterCounts=new Text[2];
        private readonly GameObject[] boosterInfos=new GameObject[2],boosterAdds=new GameObject[2];
        // Gọi lại nhiều lần không sao; menu Rug Master/Apply Booster Skin lưu kết quả vào scene GamePlay.
        public void EnsureBoosterSkins()
        {
            for(int i=0;i<boosters.Length;i++)
            {
                var b=boosters[i];if(b==null)continue;
                b.texture=null;b.color=Color.clear;
                var old=b.transform.Find(BoosterSkinName);
                if(old!=null)
                {
                    var existing=old.Find("BtnOn/Info/Amount");
                    if(existing!=null)
                    {
                        boosterCounts[i]=existing.GetComponent<Text>();
                        boosterInfos[i]=old.Find("BtnOn/Info").gameObject;
                        var add=old.Find("BtnOn/AddIng");boosterAdds[i]=add!=null?add.gameObject:null;
                        LayoutBoosterSkin((RectTransform)old,i);
                        EnsureBoosterLockSkin((RectTransform)old,i);
                        continue;
                    }
                    DestroyImmediate(old.gameObject);
                }
                var skin=new GameObject(BoosterSkinName,typeof(RectTransform)).GetComponent<RectTransform>();
                skin.SetParent(b.transform,false);
                LayoutBoosterSkin(skin,i);
                var on=BoosterPart(skin,"BtnOn","booster-frame",Vector2.zero);
                var core=BoosterPart(on.rectTransform,"Core","booster-core",new Vector2(0,5));
                var icon=BoosterPart(core.rectTransform,"Ref",i==0?"booster-icon-rearrange":"booster-icon-sweep",Vector2.zero);
                // Icon vừa khung Ref 128x125 của Pixeland, giữ tỉ lệ.
                var iconSize=icon.rectTransform.sizeDelta;float fit=Mathf.Min(128/iconSize.x,125/iconSize.y);
                icon.rectTransform.sizeDelta=iconSize*fit;
                var info=BoosterPart(on.rectTransform,"Info","booster-count",new Vector2(80,-80));
                var count=new GameObject("Amount",typeof(RectTransform)).AddComponent<Text>();
                count.transform.SetParent(info.transform,false);Stretch(count.rectTransform);
                count.rectTransform.offsetMin=new Vector2(0,4);count.rectTransform.offsetMax=new Vector2(0,0);
                count.font=fredoka!=null?fredoka:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
                count.text=Constants.BoostersPerLevel.ToString();
                count.fontSize=44;count.alignment=TextAnchor.MiddleCenter;count.color=Color.white;count.raycastTarget=false;
                count.horizontalOverflow=HorizontalWrapMode.Overflow;count.verticalOverflow=VerticalWrapMode.Overflow;
                var outline=count.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0.05f,0.32f,0.05f,0.9f);outline.effectDistance=new Vector2(2,-2);
                var plus=BoosterPart(on.rectTransform,"AddIng","booster-plus",new Vector2(80,-80));plus.gameObject.SetActive(false);
                boosterCounts[i]=count;boosterInfos[i]=info.gameObject;boosterAdds[i]=plus.gameObject;
                EnsureBoosterLockSkin(skin,i);
            }
        }
        // ---- Booster chưa mở khoá (BoosterUnlock): ẩn BtnOn, hiện BtnOff = ô xám ổ khoá (btn_disable của Pixeland)
        // với chữ "Unlock at Lv.N" trên dải dưới (như StateOff của UIBooster_InGame).
        private readonly GameObject[] boosterOns=new GameObject[2],boosterOffs=new GameObject[2];
        private readonly Text[] boosterLockTexts=new Text[2];
        private readonly int[] lastBoosterLocked={-1,-1};
        private bool? pendingUnlockPopup;
        private void EnsureBoosterLockSkin(RectTransform skin,int index)
        {
            var on=skin.Find("BtnOn");boosterOns[index]=on!=null?on.gameObject:null;
            var off=skin.Find("BtnOff");
            if(off==null)
            {
                var image=BoosterPart(skin,"BtnOff","booster-locked",new Vector2(0,-12));
                off=image.transform;
                var label=new GameObject("Des",typeof(RectTransform)).AddComponent<Text>();
                label.transform.SetParent(off,false);
                var r=label.rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0.5f,0.5f);
                r.anchoredPosition=new Vector2(0,-75);r.sizeDelta=new Vector2(150,56);
                label.font=fredoka!=null?fredoka:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
                label.fontSize=24;label.alignment=TextAnchor.MiddleCenter;label.color=Color.white;label.raycastTarget=false;
                label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Overflow;
                var outline=label.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0.2f,0.22f,0.35f,0.9f);outline.effectDistance=new Vector2(1.5f,-1.5f);
                label.text=string.Empty;
                off.gameObject.SetActive(false);
            }
            // Cỡ cố định như BtnOff của Pixeland (210x235, bằng BtnOn 210x211 + dải chữ), không theo cỡ texture đã import
            // (Max Size Android nhỏ hơn ảnh thì texture.width nhỏ lại làm ô khoá bé hơn ô booster).
            ((RectTransform)off).sizeDelta=new Vector2(210,235);
            boosterOffs[index]=off.gameObject;
            boosterLockTexts[index]=off.GetComponentInChildren<Text>(true);
        }
        private void SetBoosterLocked(int index,bool locked)
        {
            if(boosterOffs[index]==null)EnsureBoosterSkins();
            if(boosterOns[index]!=null)boosterOns[index].SetActive(!locked);
            if(boosterOffs[index]!=null)boosterOffs[index].SetActive(locked);
            if(locked&&boosterLockTexts[index]!=null)boosterLockTexts[index].text=string.Empty;
        }
        private static void LayoutBoosterSkin(RectTransform skin,int index)
        {
            skin.anchorMin=skin.anchorMax=skin.pivot=new Vector2(0.5f,0.5f);
            skin.anchoredPosition=BoosterSkinOffset[index];skin.sizeDelta=new Vector2(183,188);
            skin.localScale=new Vector3(BoosterSkinScale,BoosterSkinScale,1);
        }
        // Ảnh đặt ở đúng kích thước gốc, tâm tại offset (như các con của Booster_Launcher, anchor giữa).
        private static RawImage BoosterPart(RectTransform parent,string name,string key,Vector2 offset)
        {
            var image=new GameObject(name,typeof(RectTransform)).AddComponent<RawImage>();
            image.transform.SetParent(parent,false);image.raycastTarget=false;
            var t=RugPlayableAssets.Load<Texture2D>("RugMaster/Art/Booster/"+key);image.texture=t;
            var r=image.rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0.5f,0.5f);
            r.anchoredPosition=offset;r.sizeDelta=t!=null?new Vector2(t.width,t.height):new Vector2(100,100);
            return image;
        }
        // Còn lượt: huy hiệu xanh lá ghi số; hết lượt: dấu + vàng (như Pixeland).
        private void SetBoosterCount(int index,int value)
        {
            if(boosterCounts[index]==null)EnsureBoosterSkins();
            if(boosterCounts[index]!=null)boosterCounts[index].text=Mathf.Max(0,value).ToString();
            if(boosterInfos[index]!=null)boosterInfos[index].SetActive(value>0);
            // Hết lượt vẫn bấm được: hiện dấu + -> bấm mở store (InstallFullGame) và được +1 lượt (PressBooster).
            if(boosterAdds[index]!=null)boosterAdds[index].SetActive(value<=0); boosters[index].GetComponent<Button>().interactable=true;
        }
        private void AnimateBoosters()
        {
            for(int i=0;i<boosters.Length;i++)
            {
                if(boosters[i]==null)continue;
                float age=Time.unscaledTime-boosterPressAt[i];
                var r=boosters[i].rectTransform;
                if(age<0||age>0.4f){r.localScale=Vector3.one;r.localEulerAngles=Vector3.zero;continue;}
                float k=age/0.4f;
                if(boosterPressUsed[i])
                {float s=1+0.18f*Mathf.Sin(k*Mathf.PI)*(1-k);r.localScale=new Vector3(s,s,1);r.localEulerAngles=Vector3.zero;}
                else
                {r.localScale=Vector3.one*(1-0.06f*Mathf.Sin(k*Mathf.PI));r.localEulerAngles=new Vector3(0,0,7*Mathf.Sin(k*Mathf.PI*5)*(1-k));}
            }
        }
        /// <summary>Thông báo ngắn phía trên hàng nút dưới cùng, tự mờ đi.</summary>
        public void ShowToast(string message)
        {
            if(string.IsNullOrEmpty(message))return;
            var parent=overlayRoot!=null?overlayRoot:content;
            if(parent==null)return;
            if(toast==null)
            {
                toast=Rect(parent,"Toast message",140,1470,800,112);
                var bg=Image(toast,"Toast background",new Rect(0,0,800,112),Texture2D.whiteTexture);
                bg.color=new Color(45/255f,28/255f,15/255f,0.85f);
                toastText=Rect(toast,"Toast text",24,0,752,112).gameObject.AddComponent<Text>();
                toastText.font=fredoka!=null?fredoka:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
                toastText.fontSize=44;toastText.color=Color.white;toastText.alignment=TextAnchor.MiddleCenter;toastText.raycastTarget=false;
                toastText.horizontalOverflow=HorizontalWrapMode.Wrap;toastText.verticalOverflow=VerticalWrapMode.Overflow;
                toastGroup=toast.gameObject.AddComponent<CanvasGroup>();toastGroup.blocksRaycasts=false;toastGroup.interactable=false;
            }
            toastText.text=message;
            toast.gameObject.SetActive(true);
            toast.SetAsLastSibling();
            toastShownAt=Time.unscaledTime;
            UpdateToast();
        }
        private void UpdateToast()
        {
            if(toast==null||!toast.gameObject.activeSelf)return;
            float age=Time.unscaledTime-toastShownAt;
            if(age>=ToastDuration){toast.gameObject.SetActive(false);return;}
            toastGroup.alpha=Mathf.Min(Mathf.Clamp01(age/0.12f),Mathf.Clamp01((ToastDuration-age)/0.35f));
            toast.anchoredPosition=new Vector2(140,-1470+12*(1-Mathf.Clamp01(age/0.2f)));
        }

        

        // Quit trong popup Setting: về Home. Các manager (BoosterManager/LevelManager) DontDestroyOnLoad cả GameObject
        // "Rug Master" (chứa luôn view/canvas), nên phải huỷ nó, nếu không UI gameplay sẽ đè lên Home và lần vào
        // GamePlay sau sẽ bị Singleton huỷ mất GameManager mới.
        // Giống Pixeland (UIQuitLevel -> UIManager.ShowHomeUI -> ShowLoading): hiện màn loading rồi mới về Home;
        // gameplay đứng yên sau màn loading và object "Rug Master" bị huỷ ngay trước khi Home kích hoạt.
        
        
        private static void BindButton(Transform t,Action action)
        {
            if(t==null)return;var b=t.GetComponent<Button>();b.onClick.RemoveAllListeners();b.onClick.AddListener(()=>{action();});
        }

        // HUD level kiểu Pixeland (MainGameUI(F)/TopUI/Level): khung frame_lv 190x75 sát mép trái, chữ trắng "Lv.N"
        // (MIKADO-BOLD viền xanh đậm). Khung + material lấy từ PopupCatalog; thiếu thì giữ kiểu cũ "Level N" trên header-blank.
        // Không còn bấm vào để mở bảng chọn level (Level Picker vẫn mở bằng phím L ở Mode Development).
        
        private void BindHeader(int level) { header.gameObject.SetActive(false); }
        

        public void Bind(RugSimulation model,int displayedLevel)
        {
            simulation=model;fiberViews.Clear();sourceLevel=Constants.SourceIndex(displayedLevel);
            basePile.texture=RugOriginalAssets.Texture($"base-{sourceLevel:000}");
            BindHeader(displayedLevel);
            tuftMesh.Bind(model);shadowMesh.Bind(model);
            if(trimEffects!=null)trimEffects.Bind(model);
            if(shadeTexture!=null)DestroyImmediate(shadeTexture);
            shadeTexture=new Texture2D(model.Definition.cols,model.Definition.rows,TextureFormat.RGBA32,false)
                {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,name="Live uncut occlusion"};
            shade.texture=shadeTexture;
            BindPileImage();
            lastTrimmed=lastMeter=lastHead=lastRearranges=lastSweeps=-1;lastBoosterLocked[0]=lastBoosterLocked[1]=-1;
            pendingUnlockPopup=null;wonAt=-1;settingsOpen=false;jamPopupShown=winHandled=false;
            
            selector.gameObject.SetActive(true);foreach(var b in boosters)b.gameObject.SetActive(true);
            EnsureBoosterSkins();
            ClearChildren(basketsRoot);
            basketViews.Clear();
            ClearChildren(marksRoot);
            // Kẹp / ruy băng vẽ lúc chạy từ ảnh kit (RugObstacleMarks). Editor "Rebuild Original UI Scene" không dựng (texture khung sinh lúc chạy).
            obstacleMarks?.Clear();
            obstacleMarks=new RugObstacleMarks(marksRoot);
            if(Application.isPlaying)obstacleMarks.Build(model);
            ClearModal();
            if(winPanel!=null&&Application.isPlaying)winPanel.Hide(true);

            Refresh();
            if(initialized)StartTutorialIfNeeded();
            if(presentation!=null)presentation.Bind(model,initialized);
        }

        private void Update()
        {
            if(canvas==null)return;
            // Scene cũ chưa có CanvasScaler thì tự scale như trước.
            if(canvas.GetComponent<CanvasScaler>()==null)canvas.scaleFactor=Mathf.Min(Screen.width/1080f,Screen.height/1920f);
            FitWood();
            if(initialized){AnimateBoosters();UpdateToast();}
            if(simulation==null||!initialized)return;
            var levels=LevelManager.Ins;
            if(simulation!=levels.Simulation){Bind(levels.Simulation,levels.CurrentLevel);return;}
            Refresh();
            if(simulation.State!=PlayState.Jammed)jamPopupShown=false;
            else if(!jamPopupShown&&!settingsOpen&&modalRoot.childCount==0)ShowJam();
            if(simulation.State==PlayState.Won&&presentation==null)
            {
                if(wonAt<0){wonAt=Time.unscaledTime;ShowClear();}
                if(!winHandled&&Time.unscaledTime-wonAt>3.4f){winHandled=true;RugGameFlow.OnWinPresentationFinished();}
            }
        }

        private void Refresh()
        {
            if(simulation==null)return;
            if(lastTrimmed!=simulation.TrimmedCount)
            {
                lastTrimmed=simulation.TrimmedCount;
                if(shadePixels==null||shadePixels.Length!=simulation.Cells.Length)
                    shadePixels=new Color32[simulation.Cells.Length];
                var pixels=shadePixels;
                foreach(var c in simulation.Cells)
                    pixels[(simulation.Definition.rows-1-c.Y)*simulation.Definition.cols+c.X]=
                        new Color32(42,22,8,c.Trimmed?(byte)0:(byte)255);
                shadeTexture.SetPixels32(pixels);shadeTexture.Apply(false);
            }
            int percent=simulation.Full?100:Mathf.Min(99,Mathf.FloorToInt(100f*simulation.Fibers.Count/simulation.Capacity));
            if(percent!=lastMeter)
            {
                // Thanh Collection load nằm trên cùng trong ống bao quanh, bên trái miệng hút (sprite 500x72 căn giữa tại anchor).
                lastMeter=percent;SetMeter(percent);
            }
            var input=initialized?ControllerManager.Ins:null;
            int width=input!=null?input.HeadWidth:2;
            string head=width==1?"narrow":width==4?"wide":"normal";
            if(lastHead!=width){lastHead=width;SetHeadSelector(width);}
            bool placed=input!=null&&input.HasPlacedTool;
            nativeTool.Show(head,placed?input.ToolPosition:new Vector2(880,560),input!=null?input.ToolAngle:-0.14f,
                input==null||input.IsToolRaised,input!=null&&input.IsCutting,simulation.Full&&input!=null&&input.IsDragging,simulation.Elapsed);
            nativeTool.gameObject.SetActive(simulation.State==PlayState.Playing||presentation!=null&&presentation.IsFinishing);
            if(initialized)
            {
                var boosterState=BoosterManager.Ins;
                if(lastRearranges!=boosterState.Rearranges)
                {lastRearranges=boosterState.Rearranges;SetBoosterCount(0,lastRearranges);}
                if(lastSweeps!=boosterState.Sweeps)
                {lastSweeps=boosterState.Sweeps;SetBoosterCount(1,lastSweeps);}
                // Khoá chỉ đổi theo level: tính lại một lần sau mỗi lần Bind (không tìm RemoteConfig mỗi frame).
                for(int i=0;i<2;i++)
                {
                    if(lastBoosterLocked[i]>=0)continue;
                    lastBoosterLocked[i]=0;
                    SetBoosterLocked(i,lastBoosterLocked[i]==1);
                }
                // Level mở khoá booster: popup UnlockBooster một lần, chờ intro / tutorial xong và không có popup khác.
                // Đóng popup thì nút booster vừa mở nảy lên khoe 2 lượt miễn phí của level.
                if(pendingUnlockPopup.HasValue&&!PresentationBlocksInput&&!TutorialBlocksInput&&!GameManager.Ins.IsModalPaused)
                {
                    bool sweep=pendingUnlockPopup.Value;pendingUnlockPopup=null;
                    int index=sweep?1:0;
                    
                }
                // Còn 1-2 cục len khó nhìn mà mấy giây chưa cắt được: vòng sáng chỉ vào chỗ đó (như game gốc).
                bool free=!PresentationBlocksInput&&!TutorialBlocksInput&&!GameManager.Ins.IsModalPaused;
                if(leftoverHint==null)leftoverHint=new RugLeftoverHint(marksRoot);
                leftoverHint.Update(simulation,free);
            }
            RefreshBaskets();RefreshMarks();RefreshFibers();
        }

        private void RefreshBaskets()
        {
            var live=liveBasketIds;live.Clear();
            int sibling=0;
            // Source layering is front baskets first, then queue rows from top to bottom.
            for(int p=0;p<10;p++)for(int c=0;c<3;c++)
            {
                if(p>=simulation.Columns[c].Count)continue;
                var b=simulation.Columns[c][p];live.Add(b.Id);
                if(!basketViews.TryGetValue(b.Id,out var v))
                {
                    v=new BasketVisual();v.root=Rect(basketsRoot,"Basket "+b.Id+" - "+b.Color,0,0,0,0);
                    v.back=Image(v.root,"Back and table shadow",new Rect());
                    var heap=Rect(v.root,"Collected fiber heap",0,0,0,0);v.heap=heap.gameObject.AddComponent<RugHeapGraphic>();v.heap.raycastTarget=false;
                    v.front=Image(v.root,"Front wall and colored tag",new Rect());
                    v.number=CreateBasketNumber(v.root,b.Color);
                    v.queueShadow=Image(v.root,"Queue shadow",new Rect());
                    v.queue=Image(v.root,"Queue basket",new Rect());
                    v.queueTag=Image(v.root,"Queue tag or folded color",new Rect());
                    v.bow=Image(v.root,"Ribbon bow",new Rect());v.seal=Image(v.root,"Completion seal",new Rect());
                    basketViews[b.Id]=v;
                }
                bool front=p==0;
                float qy=1696+(p-1)*132*0.92f;
                float targetY=front?1554:qy;
                float targetX=RugSimulation.BasketX[c];
                // Rổ đổi chỗ (rổ trước đã xong -> tiến lên một hàng; booster Rearrange -> đổi cột/hàng): trượt dần từ chỗ
                // đang hiển thị tới chỗ mới; đổi giữa rổ con và rổ hàng đầu thì scale nối tiếp từ cỡ cũ (to dần / nhỏ dần).
                if(v.position>=0&&(v.position!=p||v.column!=c)&&!b.Completing)
                {
                    float from=v.shownScale;
                    if(v.position>0&&front)from*=QueueToFrontScale;
                    else if(v.position==0&&!front)from/=QueueToFrontScale;
                    v.moveFromX=v.shownX;v.moveFromY=v.shownY;v.moveFromScale=from;
                    v.moveStart=simulation.Elapsed;
                }
                v.column=c;
                float move=v.moveStart<0?1:Mathf.Clamp01((simulation.Elapsed-v.moveStart)/BasketAdvanceDuration);
                if(move>=1)v.moveStart=-1;
                float ease=1-Mathf.Pow(1-move,3);
                float x=move>=1?targetX:Mathf.Lerp(v.moveFromX,targetX,ease);
                float y=move>=1?targetY:Mathf.Lerp(v.moveFromY,targetY,ease);
                float advanceScale=move>=1?1:Mathf.LerpUnclamped(v.moveFromScale,1,RugPilePose.OutBack(move,1.4f));
                v.shownX=x;v.shownY=y;v.shownScale=advanceScale;
                v.root.anchoredPosition=new Vector2(x,-y);
                if(v.root.GetSiblingIndex()!=sibling)v.root.SetSiblingIndex(sibling);
                sibling++;
                bool lastInQueue=p==simulation.Columns[c].Count-1;
                bool changed=v.position!=p||v.count!=b.Remaining||v.revealed!=b.Revealed||
                    v.tied!=b.Tied||v.completing!=b.Completing||v.lastInQueue!=lastInQueue;
                if(changed)
                {
                    v.back.gameObject.SetActive(front);v.front.gameObject.SetActive(front);v.heap.gameObject.SetActive(front);
                    v.number.gameObject.SetActive(front&&b.Revealed&&!b.Completing);
                    v.queue.gameObject.SetActive(!front);v.queueTag.gameObject.SetActive(!front);v.queueShadow.gameObject.SetActive(!front);
                    v.heap.Bind(b);
                    if(front)
                    {
                        SetLocalSprite(v.back,"basket-back-"+b.Color,Vector2.zero);
                        SetLocalSprite(v.front,"basket-front-"+b.Color,Vector2.zero);
                        if(v.count!=b.Remaining||v.position!=p)v.number.text=b.Remaining.ToString();
                        SetKit(v.bow,"bow",new Vector2(0,-243+70),210f/483/0.75f);
                    }
                    else
                    {
                        // Rổ con dùng chung ảnh rổ màu mới (basket-back-<màu>) thu nhỏ; chưa lộ màu thì rổ xám basket-queue-hidden.
                        // Thân rổ rộng/đáy trùng với rổ gỗ kit-basket cũ (thân 610px * 165*0.997/1108/0.55) nên chỗ bông rơi không đổi.
                        SetLocalSprite(v.queue,b.Revealed?"basket-back-"+b.Color:"basket-queue-hidden",Vector2.zero,QueueBasketScale,QueueBasketPivot);
                        SetLocalSprite(v.queueShadow,"qbShadow",new Vector2(0,-3),165*0.9f/278,new Vector2(0.5f,0.5f));
                        var sr=v.queueShadow.rectTransform;sr.sizeDelta=new Vector2(sr.sizeDelta.x,sr.sizeDelta.y*0.7f/0.9f);
                        sr.anchoredPosition=new Vector2(sr.anchoredPosition.x,3+60*165*0.7f/278);
                        // Miếng vải "?" luôn ở giữa mặt trước rổ con (ảnh rổ mới: mặt trước y 144.0.260, đáy 260 -> tâm cách đáy 58px * scale).
                        // Rổ gỗ cũ cao hơn nên hàng sau che mất đáy rổ hàng trước, khi đó nhãn của rổ không phải cuối hàng phải đẩy lên cao;
                        // rổ mới thấp hơn khoảng cách hàng nên không bị che, mọi rổ dùng chung một vị trí.
                        float ty=-58f*QueueBasketScale;
                        // Rổ đã có màu nên không cần nhãn màu; rổ chưa lộ màu vẫn có miếng vải gấp.
                        v.queueTag.gameObject.SetActive(!b.Revealed);
                        if(!b.Revealed)SetKit(v.queueTag,"fold",new Vector2(0,ty),132*0.42f/375/0.7f);
                        SetKit(v.bow,"bow",new Vector2(0,-132+8),165*0.6f/483/0.75f);
                    }
                    v.bow.gameObject.SetActive(b.Tied>=0);
                    v.seal.gameObject.SetActive(b.Completing);
                    if(b.Completing)SetSeal(v.seal,front);
                    v.position=p;v.count=b.Remaining;v.revealed=b.Revealed;v.tied=b.Tied;
                    v.completing=b.Completing;v.lastInQueue=lastInQueue;
                    }
                if(b.Completing)
                {
                    float t=1-Mathf.Clamp01(b.CompleteTimer/(front?1.12f:0.5f));
                    float a=t<0.63f?0:Mathf.InverseLerp(0.63f,1,t);
                    v.root.localScale=Vector3.one*Mathf.Lerp(1,0.3f,a*a);
                    v.root.anchoredPosition+=new Vector2(0,140*a*a);
                }
                else
                {
                    float age=simulation.Elapsed-b.LandedAt;
                    float punch=age>=0&&age<0.26f?1-RugPilePose.OutBack(age/0.26f,2.6f):0;
                    v.root.localScale=new Vector3((1+0.04f*punch)*advanceScale,(1-0.05f*punch)*advanceScale,1);
                }
            }
            foreach(var pair in basketViews)pair.Value.root.gameObject.SetActive(live.Contains(pair.Key));
        }

        private Text CreateBasketNumber(Transform parent,string basketColor)
        {
            if(basketNumberFont==null)basketNumberFont=RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-700");
            float hk=(304*0.997f/1108)/(279.2f/1108);
            var rect=Rect(parent,"Remaining fiber number",0,0,180,110);
            rect.pivot=new Vector2(0.5f,0.5f);
            rect.anchoredPosition=new Vector2(0,(58-2*0.78f)*hk);
            var label=rect.gameObject.AddComponent<Text>();
            label.font=basketNumberFont!=null?basketNumberFont:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            label.fontSize=basketNumberFontSize;
            label.alignment=TextAnchor.MiddleCenter;
            label.horizontalOverflow=HorizontalWrapMode.Overflow;
            label.verticalOverflow=VerticalWrapMode.Overflow;
            label.raycastTarget=false;
            bool dark=basketColor=="cream"||basketColor=="rose"||basketColor=="sky"||basketColor=="peach";
            label.color=dark?basketNumberDarkColor:basketNumberLightColor;
            return label;
        }

        private void RefreshMarks()=>obstacleMarks?.Refresh(simulation);

        public void EnsureFiberBatches()
        {
            pipeFiberBatch=EnsureFiberBatch(fibersRoot,pipeFiberBatch);
            dropFiberBatch=EnsureFiberBatch(dropsRoot,dropFiberBatch);
            flyingFiberBatch=EnsureFiberBatch(flyersRoot,flyingFiberBatch);
        }
        private static RugFiberBatchGraphic EnsureFiberBatch(RectTransform root,RugFiberBatchGraphic batch)
        {
            // Keep moving puff vertices from rebuilding the rug/HUD canvas batches.
            var layerCanvas=root.GetComponent<Canvas>();
            if(layerCanvas==null)layerCanvas=root.gameObject.AddComponent<Canvas>();
            layerCanvas.overrideSorting=false;
            if(batch==null)batch=root.GetComponentInChildren<RugFiberBatchGraphic>(true);
            if(batch==null)batch=Rect(root,"Batched fibers",0,0,1080,1920).gameObject.AddComponent<RugFiberBatchGraphic>();
            if(batch.GetComponent<CanvasRenderer>()==null)batch.gameObject.AddComponent<CanvasRenderer>();
            batch.atlas=RugOriginalAssets.Texture("flight-atlas");batch.raycastTarget=false;batch.maskable=false;
            return batch;
        }
        private void RefreshFibers()
        {
            if(pipeFiberBatch==null||dropFiberBatch==null||flyingFiberBatch==null)EnsureFiberBatches();
            pipeFiberBatch.BeginFrame();dropFiberBatch.BeginFrame();flyingFiberBatch.BeginFrame();
            while(fiberViews.Count<simulation.Fibers.Count)fiberViews.Add(new FiberVisual());
            for(int i=0;i<simulation.Fibers.Count;i++)
            {
                var f=simulation.Fibers[i];var visual=fiberViews[i];
                if(visual.id!=f.Id){visual.id=f.Id;visual.sprite=Flight(f.Color,f.Variant);}
                var batch=f.Phase==FiberPhase.Channel||(f.Phase==FiberPhase.Flight&&f.FlightStage==2)?pipeFiberBatch:
                    f.Phase==FiberPhase.Capture&&!f.Sweep&&f.Position.y-18<=RugFiberMotion.LipBottom?dropFiberBatch:flyingFiberBatch;
                var sprite=visual.sprite;
                batch.Add(f.Position,sprite.size*(f.VisualScale/0.28f),sprite.uv,f.Rotation);
            }
            if(fiberTrail==null)fiberTrail=new RugFiberTrail(pipeFiberBatch,dropFiberBatch,flyingFiberBatch,Flight);
            fiberTrail.Update(simulation);
            pipeFiberBatch.EndFrame();dropFiberBatch.EndFrame();flyingFiberBatch.EndFrame();
        }
        private FlightSprite Flight(string color,int variant)
        {
            if(!flightSprites.TryGetValue((color,variant),out var sprite))
            {
                var r=RugOriginalAssets.FlightFrame("fly-"+color+"-"+variant).rect;
                sprite=new FlightSprite{uv=new Rect(r[0]/2048,1-(r[1]+r[3])/2048,r[2]/2048,r[3]/2048),size=new Vector2(r[2],r[3])};
                flightSprites.Add((color,variant),sprite);
            }
            return sprite;
        }

        
        private void ShowJam() { jamPopupShown=true; simulation.Revive(); }
        private void ShowClear()
        {
            ClearModal();nativeTool.gameObject.SetActive(false);
            selector.gameObject.SetActive(false);
            foreach(var b in boosters)b.gameObject.SetActive(false);
        }
        private void ClearModal()
        {ClearChildren(modalRoot);}
        private static void ClearChildren(Transform root)
        {if(root!=null)for(int i=root.childCount-1;i>=0;i--)DestroyImmediate(root.GetChild(i).gameObject);}
        private void Dim()
        {var dim=Image(modalRoot,"Full-screen dim",new Rect(-1080,-1920,3240,5760),Texture2D.whiteTexture);dim.color=new Color(45/255f,28/255f,15/255f,0.5f);dim.raycastTarget=true;}
        private void Panel(Transform parent,Rect r)
        {
            var bottom=Image(parent,"Panel ledge",new Rect(r.x,r.y+18,r.width,r.height),Texture2D.whiteTexture);bottom.color=new Color32(211,184,140,255);
            var panel=Image(parent,"Cream panel",r,Texture2D.whiteTexture);panel.color=new Color32(251,243,225,255);
            var outline=panel.gameObject.AddComponent<Outline>();outline.effectColor=Color.white;outline.effectDistance=new Vector2(8,8);
        }
        private void Text(Transform parent,string value,Rect rect,int size,Color color)
        {
            var t=Rect(parent,value,rect.x,rect.y,rect.width,rect.height).gameObject.AddComponent<Text>();
            t.font=fredoka!=null?fredoka:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            t.fontSize=size;t.text=value;t.color=color;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;
        }
        private void PopupButton(string text,Rect rect,Action action,bool primary)
        {
            var image=Image(modalRoot,text+" button",rect,Texture2D.whiteTexture);
            image.color=primary?new Color32(232,90,75,255):new Color32(248,237,212,255);
            AddButton(image.rectTransform,action);Text(image.transform,text,new Rect(0,0,rect.width,rect.height),58,primary?Color.white:(Color)new Color32(74,52,38,255));
        }

        // Ảnh nguyên lông phủ đúng khung "Rounded pile clipping" (90,226,900,740), nằm dưới 2 mesh pile.
        // Có ảnh bake + material: mesh chỉ vẽ ô đang bị cạo (pop). Thiếu ảnh: mesh vẽ mọi ô như cũ.
        private void BindPileImage()
        {
            var clipping=tuftMesh.transform.parent;
            if(pileImage==null){var found=clipping.Find(PileImageName);if(found!=null)pileImage=found.GetComponent<RawImage>();}
            if(pileImage==null)pileImage=Image(clipping,PileImageName,new Rect(0,0,900,740));
            pileImage.transform.SetSiblingIndex(0);
            var texture=RugOriginalAssets.TryTexture(PileImageKey(sourceLevel));
            var material=RugPlayableAssets.Load<Material>(PileMaskMaterial);
            bool baked=texture!=null&&material!=null;
            pileImage.gameObject.SetActive(baked);
            tuftMesh.drawUncut=shadowMesh.drawUncut=!baked;
            if(!baked)return;
            pileImage.texture=texture;pileImage.color=Color.white;pileImage.raycastTarget=false;
            if(!Application.isPlaying){pileImage.material=material;return;}
            if(pileMaterial==null)pileMaterial=new Material(material){name="Uncut pile mask (runtime)"};
            // Mask = shadeTexture (cols x rows, alpha 255 chưa cạo / 0 đã cạo, hàng 0 ở dưới) phủ Constants.RugRect.
            // Đổi uv ảnh (khung 90,226,900,740; v=1 ở mép trên) sang uv của lưới ô.
            var r=Constants.RugRect;
            pileMaterial.SetTexture("_MaskTex",shadeTexture);
            pileMaterial.SetVector("_MaskST",new Vector4(900/r.width,740/r.height,(90-r.x)/r.width,(r.yMax-226-740)/r.height));
            pileImage.material=pileMaterial;
        }

        private static RugPileGraphic Pile(Transform parent,string name,bool shadow)
        {
            var rect=Rect(parent,name,-90,-226,1080,1920);var p=rect.gameObject.AddComponent<RugPileGraphic>();p.shadows=shadow;p.raycastTarget=false;return p;
        }

        // Render the live Canvas at the reference resolution, independent of the desktop window limit.
        // ScreenCapture supersize retains the window's texture LOD and cannot be used for pixel comparisons.
        public void CaptureReferenceFrame(string path,int width,int height)
        {
            var cameraObject=new GameObject("Reference capture camera");
            var camera=cameraObject.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(238,230,212,255);
            camera.orthographic=true;camera.nearClipPlane=0.1f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=target;
            var previousTarget=RenderTexture.active;
            float previousScale=canvas.scaleFactor;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.scaleFactor=width/1080f;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active=target;
            var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
            System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());
            RenderTexture.active=previousTarget;
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;canvas.scaleFactor=previousScale;
            camera.targetTexture=null;target.Release();
            Destroy(pixels);Destroy(target);Destroy(cameraObject);
        }
        public static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);Position(r,new Rect(x,y,w,h));return r;
        }
        public static void Position(RectTransform t,Rect r)
        {t.anchorMin=t.anchorMax=new Vector2(0,1);t.pivot=new Vector2(0,1);t.anchoredPosition=new Vector2(r.x,-r.y);t.sizeDelta=r.size;}
        private static RawImage Image(Transform parent,string name,Rect r,Texture texture=null)
        {var image=Rect(parent,name,r.x,r.y,r.width,r.height).gameObject.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;return image;}
        private static void AddButton(RectTransform rect,Action action)
        {var b=rect.gameObject.AddComponent<Button>();var g=rect.GetComponent<Graphic>();g.raycastTarget=true;b.targetGraphic=g;b.transition=Selectable.Transition.None;b.onClick.AddListener(()=>{action();});}
        private static void SetLocalSprite(RawImage image,string key,Vector2 anchor,float scale=1,Vector2? forcedPivot=null)
        {
            var t=RugOriginalAssets.Texture(key);image.texture=t;var meta=RugOriginalAssets.Record(key);
            Rect r;
            if(forcedPivot.HasValue){var a=forcedPivot.Value;r=new Rect(-a.x*t.width,-a.y*t.height,t.width,t.height);}
            else r=meta!=null&&meta.rect!=null&&meta.rect.Length==4?new Rect(meta.rect[0],meta.rect[1],meta.rect[2],meta.rect[3]):new Rect(0,0,t.width,t.height);
            Position(image.rectTransform,new Rect(anchor.x+r.x*scale,anchor.y+r.y*scale,r.width*scale,r.height*scale));
        }
        // ---- Tem "xong giỏ" (kit-seal) - giống game gốc ----
        // Dải vải con dấu hoàn thành quấn ngang giữa thành trước rổ như demo: rổ hàng đầu thành trước (basket-front)
        // rộng 310 (kể cả bo cạnh), cao từ y -146 tới +4 so với đáy -> dải rộng 300, tâm y -70. Rổ con: mặt trước y 144..260
        // của ảnh rổ (tâm cách đáy 58px * QueueBasketScale), bề rộng thu theo QueueToFrontScale.
        // Scale theo bề rộng hiển thị cố định nên thay ảnh to/nhỏ khác cỡ vẫn giữ nguyên kích thước trên màn.
        private const float SealWidth=300f,SealFrontY=-70f;
        private static void SetSeal(RawImage image,bool front)
        {
            var sealTex=RugOriginalAssets.Texture("kit-seal");
            float sealWidth=front?SealWidth:SealWidth*QueueToFrontScale;
            SetKit(image,"seal",new Vector2(0,front?SealFrontY:-58f*QueueBasketScale),sealTex!=null&&sealTex.width>0?sealWidth/sealTex.width:0.65f);
        }
        private static void SetKit(RawImage image,string key,Vector2 center,float scale,Vector2? pivot=null)
        {
            string name=key.StartsWith("qtag-")?key:"kit-"+key;
            SetLocalSprite(image,name,center,scale,pivot??new Vector2(0.5f,0.5f));
        }

        // ---- Thanh chọn đầu dao (thay 3 ảnh head-selector-narrow/normal/wide) ----
        // Bố cục cũ trong khung 700x305 của "Three trimmer heads": thẻ nền + 3 ô (tỉ lệ bề rộng như dom-layout.heads),
        // ô đang chọn chỉ có khung (không còn dấu tick). Đổi sang bộ UI Pixeland:
        //  - Thẻ nền: frame_tut (viền vàng, nền kem) như thẻ tutorial.
        //  - Ô đang chọn: Art/HeadSelector/head-slot-selected = viền cam mảnh + nền vàng nhạt trong mờ (tối giản như bản gốc,
        //    khác hẳn khung dày của thẻ nền).
        // Khoảng cách: lề trái thẻ = lề phải nút Sweep tới mép khung 1080 (21), thẻ cách nút Rearrange đúng bằng khoảng
        // giữa hai nút booster (~19.5) - xem BoosterSkinOffset. Vùng bấm "Head hit target N" dời theo 3 ô mới.
        //  - Icon: phần đầu của chính thân dao trong game (tool-body-*, màu xanh), cắt ngang.
        private static readonly int[] HeadWidths={1,2,4};
        private static readonly string[] HeadNames={"narrow","normal","wide"};
        private const float HeadIconCropPx=200,HeadIconScale=0.52f;
        private static readonly Rect HeadPanelRect=new Rect(21,44,642,184);
        private const float HeadPanelInset=14,HeadSlotTop=50,HeadSlotHeight=166;
        private readonly GameObject[] headSlotFrames=new GameObject[3];
        private readonly RectTransform[] headIcons=new RectTransform[3];
        private static Sprite headSlotSprite;
        private void SetHeadSelector(int width)
        {
            if(selector==null)return;
            selector.texture=null;selector.color=Color.clear;selector.raycastTarget=false;
            EnsureHeadSelector();
            for(int i=0;i<3;i++)
            {
                bool on=HeadWidths[i]==width;
                if(headSlotFrames[i]!=null)headSlotFrames[i].SetActive(on);
                if(headIcons[i]!=null)headIcons[i].localScale=Vector3.one*(on?1f:0.92f);
            }
        }
        private void EnsureHeadSelector()
        {
            if(headIcons[0]!=null)return;
            LayoutHeadHitTargets();
            var root=selector.rectTransform;
            var old=root.Find("Pixeland selector");if(old!=null)DestroyImmediate(old.gameObject);
            var group=Rect(root,"Pixeland selector",0,0,700,305);
            
            var panel=Rect(group,"Panel",HeadPanelRect.x,HeadPanelRect.y,HeadPanelRect.width,HeadPanelRect.height).gameObject.AddComponent<UnityEngine.UI.Image>();
            panel.raycastTarget=false;
            panel.sprite=RugPlayableAssets.Load<Sprite>("RugMaster/Art/HeadSelector/head-panel"); panel.type=UnityEngine.UI.Image.Type.Sliced; panel.pixelsPerUnitMultiplier=1.6f; panel.color=Color.white;
            var slots=HeadSlots();
            for(int i=0;i<3;i++)
            {
                var slot=slots[i];
                var frame=Rect(group,"Selected frame "+HeadNames[i],slot.x+8,58,slot.width-16,150).gameObject.AddComponent<UnityEngine.UI.Image>();
                frame.sprite=HeadSlotSprite();frame.type=UnityEngine.UI.Image.Type.Sliced;frame.pixelsPerUnitMultiplier=1f;frame.raycastTarget=false;
                headSlotFrames[i]=frame.gameObject;
                // Icon: phần trên của thân dao (đầu cắt + vai thân), đáy cắt phẳng, căn giữa ô.
                var t=RugOriginalAssets.Texture("tool-body-"+HeadNames[i]);
                float crop=Mathf.Min(HeadIconCropPx,t.height);
                var icon=new GameObject("Head icon "+HeadNames[i],typeof(RectTransform)).AddComponent<RawImage>();
                icon.transform.SetParent(group,false);icon.raycastTarget=false;icon.texture=t;
                icon.uvRect=new Rect(0,1-crop/t.height,1,crop/t.height);
                var r=icon.rectTransform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0.5f,0);
                r.sizeDelta=new Vector2(t.width,crop)*HeadIconScale;
                r.anchoredPosition=new Vector2(slot.center.x,-194);
                headIcons[i]=r;
            }
        }
        // 3 ô trong lòng thẻ (toạ độ khung selector), bề rộng theo tỉ lệ vùng bấm gốc dom-layout.heads.
        private static Rect[] HeadSlots()
        {
            var heads=RugOriginalAssets.Dom.heads;
            float total=0;foreach(var h in heads)total+=h.rect[2];
            float x=HeadPanelRect.x+HeadPanelInset,inner=HeadPanelRect.width-2*HeadPanelInset;
            var slots=new Rect[3];
            for(int i=0;i<3;i++)
            {
                float w=heads[i].rect[2]/total*inner;
                slots[i]=new Rect(x,HeadSlotTop,w,HeadSlotHeight);x+=w;
            }
            return slots;
        }
        // Vùng bấm đổi đầu dao (con của Bottom HUD, toạ độ khung 1080x1920; selector đặt ở y 1615) theo 3 ô mới.
        private void LayoutHeadHitTargets()
        {
            var slots=HeadSlots();
            for(int i=0;i<3;i++)
            {
                var hit=FindUI("Head hit target "+HeadWidths[i]) as RectTransform;
                if(hit!=null)Position(hit,new Rect(slots[i].x,slots[i].y+1615,slots[i].width,slots[i].height));
            }
        }
        // Khung ô chọn 128x128, góc bo 28 + viền 5: 9-slice để kéo theo bề rộng từng ô mà không méo góc.
        private static Sprite HeadSlotSprite()
        {
            if(headSlotSprite==null)headSlotSprite=RugPlayableAssets.Load<Sprite>("RugMaster/Art/HeadSelector/head-slot-selected");
            return headSlotSprite;
        }

        // ---- Thanh Collection load (giống game gốc): ghép track + fill theo % thay cho 101 ảnh meter-N ----
        // Nền xám kit-loadTrack; phần đã đầy là kit-loadFill (xanh < 80%), kit-loadFill-orange (80-99%), kit-loadFill-red (Full),
        // ngưỡng màu đo từ ảnh meter-N gốc. Fill nằm TRONG lòng xám của track (không đè viền trắng): viên thuốc bo tròn 2 đầu,
        // dài từ 1 hình tròn (1%) tới hết lòng track (100%). Chữ "Collection load" + "N%" vẽ 2 lớp: trắng phía trên fill,
        // nâu xám phía phần còn trống (tách tại mép phải fill). Vị trí/cỡ giữ như meter-0 (500x71 tại MeterAnchor).
        // Khác game gốc: viên thuốc vẽ bằng RugMeterFillGraphic (mesh 3 đoạn, đầu bo theo chiều cao) thay cho Sprite.Create 9-slice,
        // và tách 2 lớp chữ bằng Mask stencil thay cho RectMask2D (Luna chỉ cull chứ không cắt theo RectMask2D).
        private const float MeterTextInset=30;
        // Viền trắng của kit-loadTrack (ảnh 313x82): trái/phải 9px, trên 15px, dưới 14px -> tỉ lệ theo chiều cao thanh.
        private const float MeterBorderX=9f/82,MeterBorderTop=15f/82,MeterBorderBottom=14f/82;
        private static readonly Color MeterDarkText=new Color32(94,81,72,255);
        private static readonly Dictionary<string,Sprite> meterSprites=new Dictionary<string,Sprite>();

        public RugMeterFillGraphic meterFill;
        private RectTransform meterFillMask,meterRestMask;
        private Text meterPercentOn,meterPercentOff;
        private void SetMeter(int percent)
        {
            if(meter==null)return;
            SetLocalSprite(meter,"meter-0",MeterAnchor);
            meter.texture=null;meter.color=Color.clear;meter.raycastTarget=false;
            EnsureMeterBar();
            var size=meter.rectTransform.sizeDelta;
            float insetX=size.y*MeterBorderX,innerW=size.x-2*insetX,innerH=size.y*(1-MeterBorderTop-MeterBorderBottom);
            float k=Mathf.Clamp01(percent/100f);
            float fillW=k<=0?0:Mathf.Lerp(innerH,innerW,k);
            string key=percent>=100?"kit-loadFill-red":percent>=80?"kit-loadFill-orange":"kit-loadFill";
            meterFill.gameObject.SetActive(fillW>0);
            meterFill.rectTransform.sizeDelta=new Vector2(fillW,innerH);
            meterFill.SetFill(1,RugOriginalAssets.Texture(key));
            float split=fillW>0?insetX+fillW:0;
            meterFillMask.sizeDelta=new Vector2(split,0);
            meterRestMask.offsetMin=new Vector2(split,0);meterRestMask.offsetMax=Vector2.zero;
            string value=percent>=100?"Full":percent+"%";
            meterPercentOn.text=meterPercentOff.text=value;
        }
        private void EnsureMeterBar()
        {
            if(meterFill!=null&&meterPercentOn!=null)return;
            var root=meter.rectTransform;var size=root.sizeDelta;
            var old=root.Find("Bar");if(old!=null)DestroyImmediate(old.gameObject);
            var bar=Rect(root,"Bar",0,0,0,0);Stretch(bar);
            var track=new GameObject("Track",typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
            track.transform.SetParent(bar,false);Stretch(track.rectTransform);
            track.sprite=MeterSprite("kit-loadTrack");track.type=UnityEngine.UI.Image.Type.Sliced;track.raycastTarget=false;
            // Lòng xám của track; fill neo mép trái, cao bằng lòng track, rộng đặt theo % trong SetMeter.
            var inner=Rect(bar,"Inner",0,0,0,0);Stretch(inner);
            inner.offsetMin=new Vector2(size.y*MeterBorderX,size.y*MeterBorderBottom);
            inner.offsetMax=new Vector2(-size.y*MeterBorderX,-size.y*MeterBorderTop);
            meterFill=Rect(inner,"Fill",0,0,0,0).gameObject.AddComponent<RugMeterFillGraphic>();
            AnchorFixed(meterFill.rectTransform,Vector2.zero,new Vector2(0,0.5f));
            meterFill.raycastTarget=false;meterFill.maskable=false;
            meterFill.SetFill(1,RugOriginalAssets.Texture("kit-loadFill"));
            // Chữ nâu xám: từ mép phải fill tới hết thanh.
            meterRestMask=Rect(bar,"Rest (dark text)",0,0,0,0);Stretch(meterRestMask);
            MeterStencil(meterRestMask);
            MeterTexts(meterRestMask,size,new Vector2(1,0.5f),MeterDarkText,false,out meterPercentOff);
            // Chữ trắng: từ mép trái thanh tới mép phải fill.
            meterFillMask=Rect(bar,"Fill text (clipped)",0,0,0,0);
            meterFillMask.anchorMin=Vector2.zero;meterFillMask.anchorMax=new Vector2(0,1);meterFillMask.pivot=new Vector2(0,0.5f);
            meterFillMask.anchoredPosition=Vector2.zero;meterFillMask.sizeDelta=Vector2.zero;
            MeterStencil(meterFillMask);
            MeterTexts(meterFillMask,size,new Vector2(0,0.5f),Color.white,true,out meterPercentOn);
        }
        // Vùng cắt chữ bằng Mask stencil (graphic trắng ẩn, showMaskGraphic=false) - Luna hỗ trợ đầy đủ.
        private static void MeterStencil(RectTransform rect)
        {
            var shape=rect.gameObject.AddComponent<RawImage>();shape.texture=Texture2D.whiteTexture;shape.raycastTarget=false;
            rect.gameObject.AddComponent<Mask>().showMaskGraphic=false;
        }
        // Khung chữ cỡ cố định bằng cả thanh, neo theo mép (trái cho fill, phải cho phần còn lại) để chữ không chạy theo mask.
        private RectTransform MeterTexts(RectTransform parent,Vector2 size,Vector2 edge,Color color,bool shadow,out Text percent)
        {
            var box=Rect(parent,"Texts",0,0,0,0);AnchorFixed(box,size,edge);
            MeterText(box,"Label","Collection load",TextAnchor.MiddleLeft,color,shadow);
            percent=MeterText(box,"Percent","0%",TextAnchor.MiddleRight,color,shadow);
            return box;
        }
        private Text MeterText(RectTransform box,string name,string value,TextAnchor align,Color color,bool shadow)
        {
            var text=new GameObject(name,typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(box,false);Stretch(text.rectTransform);
            text.rectTransform.offsetMin=new Vector2(MeterTextInset,0);text.rectTransform.offsetMax=new Vector2(-MeterTextInset,-2);
            if(fredoka==null)fredoka=RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            text.font=fredoka!=null?fredoka:RugPlayableAssets.Load<Font>("RugMaster/Fonts/Fredoka-600");
            text.fontSize=40;text.alignment=align;text.color=color;text.raycastTarget=false;text.text=value;
            text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
            if(shadow){var s=text.gameObject.AddComponent<Shadow>();s.effectColor=new Color(0,0,0,0.35f);s.effectDistance=new Vector2(0,-3);}
            return text;
        }
        private static void AnchorFixed(RectTransform r,Vector2 size,Vector2 edge)
        {r.anchorMin=r.anchorMax=r.pivot=edge;r.anchoredPosition=Vector2.zero;r.sizeDelta=size;r.localScale=Vector3.one;}
        // Sprite 9-slice từ texture thường: viền trái/phải = nửa chiều cao (đầu bo tròn), PPU theo chiều cao ảnh / chiều cao thanh
        // để đầu bo giữ đúng tỉ lệ khi thanh thấp hơn ảnh (82 -> 71).
        private Sprite MeterSprite(string key)
        {
            if(meterSprites.TryGetValue(key,out var cached)&&cached!=null)return cached;
            var sprite=RugPlayableAssets.Load<Sprite>("RugMaster/OriginalUI/"+key);
            if(sprite==null)throw new InvalidOperationException("Missing imported UI sprite: "+key);
            meterSprites[key]=sprite;
            return sprite;
        }
        private static Sprite RoundedMaskSprite()
        {
            return RugPlayableAssets.Load<Sprite>("RugMaster/OriginalUI/rounded-mask");
        }
        private void OnDestroy()
        {
            if(initialized)EventManager.RemoveListener<OnLevelLoadedEvent>(OnLevelLoaded);
            if(initialized)EventManager.RemoveListener<OnBoosterUnavailableEvent>(OnBoosterUnavailable);
            if(initialized)RugGameFlow.Completed-=ShowWinPanel;
            if(shadeTexture!=null)Destroy(shadeTexture);
            if(pileMaterial!=null)Destroy(pileMaterial);
            obstacleMarks?.Clear();
        }
        private static Rect ArrayRect(float[] r)=>new Rect(r[0],r[1],r[2],r[3]);
        private void Hit(string name,float[] r,Action action)
        {var image=Image(modalRoot,name,ArrayRect(r),Texture2D.whiteTexture);image.color=Color.clear;AddButton(image.rectTransform,action);}

        

        private void StartTutorialIfNeeded() {}
        public void SetPresentationTutorialVisible(bool visible) {}
        public void ResumeTutorialAfterIntro()=>StartTutorialIfNeeded();
    }
}
